using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace QuietHosts.Core
{
    /// <summary>Time and waiting, injected so tests never really wait.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        void Sleep(TimeSpan duration);
    }

    public sealed class SystemClock : IClock
    {
        public static SystemClock Instance { get; } = new SystemClock();
        public DateTime UtcNow => DateTime.UtcNow;
        public void Sleep(TimeSpan duration) { if (duration > TimeSpan.Zero) Thread.Sleep(duration); }
    }

    /// <summary>
    /// The file operations the engine uses, so tests can inject faults. Implementations must not
    /// change ACLs or owners.
    /// </summary>
    public interface IFileOps
    {
        /// <summary>
        /// Reads a file shared ReadWrite|Delete. Returns null if it doesn't exist. Reads at most
        /// <paramref name="maxBytes"/> + 1 bytes, so a longer result means "too large".
        /// </summary>
        byte[]? ReadFile(string path, long maxBytes);

        /// <summary>Writes in place: FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read, all bytes, SetLength, Flush(true).</summary>
        void WriteInPlace(string path, byte[] bytes);

        /// <summary>
        /// Creates a new file (FileMode.CreateNew, write-through) and flushes it to disk. Throws IOException if it
        /// exists. If writing fails after the file was created, the partial file is deleted before the exception,
        /// which is then an IOException that the retries don't repeat.
        /// </summary>
        void CreateNew(string path, byte[] bytes);

        bool FileExists(string path);
        FileAttributes GetAttributes(string path);
        void SetAttributes(string path, FileAttributes attributes);
        /// <summary>File names (not paths) in a folder matching a wildcard pattern; empty if the folder is missing.</summary>
        IReadOnlyList<string> ListFiles(string directory, string pattern);
        void DeleteFile(string path);
        void CreateDirectory(string path);
    }

    public sealed class RealFileOps : IFileOps
    {
        public static RealFileOps Instance { get; } = new RealFileOps();

        public byte[]? ReadFile(string path, long maxBytes)
        {
            FileStream stream;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            using (stream)
            {
                // At most maxBytes + 1, so the caller can tell "too large" without reading a huge file.
                long cap = Math.Min(maxBytes, int.MaxValue - 65536L) + 1;
                int initial = (int)Math.Min(Math.Min(stream.Length, cap), 64L * 1024 * 1024);
                using (var ms = new MemoryStream(Math.Max(initial, 0)))
                {
                    var chunk = new byte[65536];
                    while (ms.Length < cap)
                    {
                        int n = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, cap - ms.Length));
                        if (n <= 0) break;
                        ms.Write(chunk, 0, n);
                    }
                    return ms.ToArray();
                }
            }
        }

        public void WriteInPlace(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.SetLength(bytes.Length);
                stream.Flush(true);
            }
        }

        public void CreateNew(string path, byte[] bytes)
        {
            var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
            try
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
                stream.Dispose();
            }
            catch (Exception failure)
            {
                // CreateNew proved the file is ours: never leave a partial copy (for example on a full disk).
                try { stream.Dispose(); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                // Never a sharing or access-denied error once the file exists: a retry would only find the file there
                // ("file exists"), and that would hide the real error.
                if (failure is IOException || failure is UnauthorizedAccessException)
                    throw new IOException("Writing the new file failed: " + failure.GetType().Name + ": " + failure.Message, failure);
                throw;
            }
        }

        public bool FileExists(string path) => File.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public void SetAttributes(string path, FileAttributes attributes) => File.SetAttributes(path, attributes);

        public IReadOnlyList<string> ListFiles(string directory, string pattern)
        {
            if (!Directory.Exists(directory)) return Array.Empty<string>();
            var names = new List<string>();
            foreach (string p in Directory.GetFiles(directory, pattern)) names.Add(Path.GetFileName(p));
            return names;
        }

        public void DeleteFile(string path) => File.Delete(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    }

    /// <summary>How a failed file operation is classified (by HResult &amp; 0xFFFF).</summary>
    public enum FileFailure
    {
        None,
        /// <summary>Sharing or lock violation (32, 33): retry after 50 ... 1600 ms, then FileLocked.</summary>
        Locked,
        /// <summary>UnauthorizedAccessException: two retries 250 ms apart, then AccessDenied.</summary>
        AccessDenied,
        Other,
    }

    /// <summary>The retry schedule of the write pipeline, shared by writes and reads.</summary>
    public static class FileRetry
    {
        /// <summary>Delays before each retry of a sharing or lock violation (ms).</summary>
        public static readonly IReadOnlyList<int> LockedDelaysMs = new[] { 50, 100, 200, 400, 800, 1600 };
        /// <summary>Delays before each retry of an access-denied failure (ms).</summary>
        public static readonly IReadOnlyList<int> AccessDeniedDelaysMs = new[] { 250, 250 };

        public static FileFailure Classify(Exception ex)
        {
            if (ex is UnauthorizedAccessException) return FileFailure.AccessDenied;
            if (ex is IOException)
            {
                int code = ex.HResult & 0xFFFF;
                if (code == 32 || code == 33) return FileFailure.Locked;
            }
            return FileFailure.Other;
        }

        /// <summary>
        /// Runs an operation with the pipeline's retries. Returns None on success, or the failure class
        /// of the last attempt (with its exception) once the retries for that class are used up.
        /// </summary>
        public static FileFailure Run(IClock clock, Action operation, out Exception? error)
        {
            int locked = 0, denied = 0;
            while (true)
            {
                try
                {
                    operation();
                    error = null;
                    return FileFailure.None;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FileFailure kind = Classify(ex);
                    if (kind == FileFailure.Locked && locked < LockedDelaysMs.Count)
                    {
                        clock.Sleep(TimeSpan.FromMilliseconds(LockedDelaysMs[locked++]));
                        continue;
                    }
                    if (kind == FileFailure.AccessDenied && denied < AccessDeniedDelaysMs.Count)
                    {
                        clock.Sleep(TimeSpan.FromMilliseconds(AccessDeniedDelaysMs[denied++]));
                        continue;
                    }
                    error = ex;
                    return kind;
                }
            }
        }

        public static ResultKind ToResult(FileFailure failure)
        {
            switch (failure)
            {
                case FileFailure.None: return ResultKind.Success;
                case FileFailure.Locked: return ResultKind.FileLocked;
                case FileFailure.AccessDenied: return ResultKind.AccessDenied;
                default: return ResultKind.Unexpected;
            }
        }
    }
}
