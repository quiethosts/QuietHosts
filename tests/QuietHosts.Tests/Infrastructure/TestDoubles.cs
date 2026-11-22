using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using QuietHosts.Core;

namespace QuietHosts.Tests.Infrastructure
{
    /// <summary>A folder %TEMP%\QuietHosts.Tests-&lt;guid&gt; that is deleted afterwards.</summary>
    public sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "QuietHosts.Tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Combine(params string[] parts)
        {
            string p = Path;
            foreach (string part in parts) p = System.IO.Path.Combine(p, part);
            return p;
        }

        public void Dispose()
        {
            try
            {
                if (!Directory.Exists(Path)) return;
                foreach (string f in Directory.GetFiles(Path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(Path, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>A key under HKCU\Software\QuietHosts.Tests\&lt;guid&gt; that is deleted afterwards.</summary>
    public sealed class TempRegistryKey : IDisposable
    {
        public const string Parent = @"Software\QuietHosts.Tests";

        public TempRegistryKey()
        {
            SubKey = Parent + @"\" + Guid.NewGuid().ToString("N");
        }

        public string SubKey { get; }
        public RegistryKey Base => Registry.CurrentUser;

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(SubKey, false);
                using (RegistryKey? parent = Registry.CurrentUser.OpenSubKey(Parent, true))
                {
                    if (parent != null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
                    {
                        parent.Close();
                        Registry.CurrentUser.DeleteSubKey(Parent, false);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException || ex is ArgumentException) { }
        }
    }

    /// <summary>A clock that never waits: Sleep advances time and is recorded; reads advance 1 ms.</summary>
    public sealed class FakeClock : IClock
    {
        private DateTime _now = new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc);
        public List<TimeSpan> Sleeps { get; } = new List<TimeSpan>();
        /// <summary>Called during each Sleep (for example, to revert the file during the 2-second wait).</summary>
        public Action<TimeSpan>? OnSleep { get; set; }

        public DateTime UtcNow
        {
            get
            {
                _now = _now.AddMilliseconds(1);
                return _now;
            }
        }

        public void Sleep(TimeSpan duration)
        {
            Sleeps.Add(duration);
            _now = _now.Add(duration);
            OnSleep?.Invoke(duration);
        }
    }

    public sealed class CountingFlusher : IDnsFlusher
    {
        public int Calls { get; private set; }
        public bool Succeed { get; set; } = true;

        public bool Flush(out string detail)
        {
            Calls++;
            detail = Succeed ? "test ok" : "test failure";
            return Succeed;
        }
    }

    public sealed class MutableDatabasePath : IHostsDatabasePath
    {
        public string? Value { get; set; }
        public Exception? Throw { get; set; }

        public string? Read()
        {
            if (Throw != null) throw Throw;
            return Value;
        }
    }

    /// <summary>Exceptions with the HResults the writer classifies.</summary>
    public static class Faults
    {
        public static IOException Sharing() => new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020));
        public static IOException Lock() => new IOException("The process cannot access the file because another process has locked a portion of the file.", unchecked((int)0x80070021));
        public static UnauthorizedAccessException Denied() => new UnauthorizedAccessException("Access to the path is denied.");
        public static IOException DiskFull() => new IOException("There is not enough space on the disk.", unchecked((int)0x80070070));
    }

    /// <summary>
    /// Real file operations on temp folders, with faults injected for the hosts file:
    /// exceptions, silently skipped writes, corrupted writes and hooks around reads.
    /// </summary>
    public sealed class FaultyFileOps : IFileOps
    {
        private readonly IFileOps _inner = RealFileOps.Instance;
        private readonly string _hostsPath;

        public FaultyFileOps(string hostsPath) { _hostsPath = Path.GetFullPath(hostsPath); }

        /// <summary>Thrown (one per call) by WriteInPlace on the hosts file before writing.</summary>
        public Queue<Exception> WriteFaults { get; } = new Queue<Exception>();
        /// <summary>Thrown (one per call) by ReadFile on the hosts file.</summary>
        public Queue<Exception> ReadFaults { get; } = new Queue<Exception>();
        /// <summary>
        /// Thrown (one per call) by WriteInPlace on the hosts file AFTER its bytes were written over the old ones
        /// without truncating them: SetLength or the flush failed, so the file is torn.
        /// </summary>
        public Queue<Exception> WriteThenFaults { get; } = new Queue<Exception>();
        /// <summary>Thrown (one per call) by CreateNew (backups).</summary>
        public Queue<Exception> CreateFaults { get; } = new Queue<Exception>();
        /// <summary>For CreateNew number N (from 1): if it returns an exception, half of the bytes are written, then it is thrown.</summary>
        public Func<int, Exception?>? CreatePartialFault { get; set; }
        /// <summary>Writes to the hosts file do nothing (a silently blocking security product).</summary>
        public bool SkipWrites { get; set; }
        /// <summary>Changes the bytes of the next N hosts writes (index = write number, from 1).</summary>
        public Func<int, byte[], byte[]>? TransformWrite { get; set; }
        /// <summary>Writes after the first throw this (a rollback that fails).</summary>
        public Exception? FailWritesAfterFirst { get; set; }
        /// <summary>Writes after the first write all their bytes and set the length, then throw this: only the flush failed.</summary>
        public Exception? FlushFailsAfterFirst { get; set; }
        /// <summary>Thrown (one per call) by DeleteFile.</summary>
        public Queue<Exception> DeleteFaults { get; } = new Queue<Exception>();
        /// <summary>Changes what a backup contains on disk (index = CreateNew number, from 1).</summary>
        public Func<int, byte[], byte[]>? TransformCreate { get; set; }
        /// <summary>Called before each hosts read with the read number (from 1).</summary>
        public Action<int>? BeforeHostsRead { get; set; }

        public int HostsWrites { get; private set; }
        public int HostsReads { get; private set; }
        public int Creates { get; private set; }

        private bool IsHosts(string path) => string.Equals(Path.GetFullPath(path), _hostsPath, StringComparison.OrdinalIgnoreCase);

        public byte[]? ReadFile(string path, long maxBytes)
        {
            if (IsHosts(path))
            {
                HostsReads++;
                BeforeHostsRead?.Invoke(HostsReads);
                if (ReadFaults.Count > 0) throw ReadFaults.Dequeue();
            }
            return _inner.ReadFile(path, maxBytes);
        }

        public void WriteInPlace(string path, byte[] bytes)
        {
            if (IsHosts(path))
            {
                if (WriteFaults.Count > 0) throw WriteFaults.Dequeue();
                HostsWrites++;
                if (HostsWrites > 1 && FailWritesAfterFirst != null) throw FailWritesAfterFirst;
                if (SkipWrites) return;
                if (TransformWrite != null) bytes = TransformWrite(HostsWrites, bytes);
                if (HostsWrites > 1 && FlushFailsAfterFirst != null)
                {
                    _inner.WriteInPlace(path, bytes);
                    throw FlushFailsAfterFirst;
                }
                if (WriteThenFaults.Count > 0)
                {
                    using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                    throw WriteThenFaults.Dequeue();
                }
            }
            _inner.WriteInPlace(path, bytes);
        }

        public void CreateNew(string path, byte[] bytes)
        {
            if (CreateFaults.Count > 0) throw CreateFaults.Dequeue();
            Creates++;
            if (TransformCreate != null) bytes = TransformCreate(Creates, bytes);
            Exception? partial = CreatePartialFault?.Invoke(Creates);
            if (partial != null)
            {
                var half = new byte[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, half, 0, half.Length);
                _inner.CreateNew(path, half);
                throw partial;
            }
            _inner.CreateNew(path, bytes);
        }

        public bool FileExists(string path) => _inner.FileExists(path);
        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
        public void SetAttributes(string path, FileAttributes attributes) => _inner.SetAttributes(path, attributes);
        public IReadOnlyList<string> ListFiles(string directory, string pattern) => _inner.ListFiles(directory, pattern);
        public void DeleteFile(string path)
        {
            if (DeleteFaults.Count > 0) throw DeleteFaults.Dequeue();
            _inner.DeleteFile(path);
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
    }

    /// <summary>A complete test environment under %TEMP% with every fake wired in.</summary>
    public sealed class TestEnv : IDisposable
    {
        public TestEnv(bool requiresElevation = false, TimeSpan? mutexWait = null)
        {
            Dir = new TempDir();
            Location = HostsLocation.ForTesting(Dir.Path);
            Directory.CreateDirectory(Location.Folder);
            Files = new FaultyFileOps(Location.HostsPath);
            Env = new AppEnvironment(Location, Machine, User, Files, Flusher, Clock, DatabasePath,
                @"Local\QuietHosts.Test." + Guid.NewGuid().ToString("N"), mutexWait ?? TimeSpan.FromMilliseconds(200),
                isReal: false, requiresElevation: requiresElevation);
        }

        public TempDir Dir { get; }
        public HostsLocation Location { get; }
        public FaultyFileOps Files { get; }
        public FakeClock Clock { get; } = new FakeClock();
        public InMemoryMachineStore Machine { get; } = new InMemoryMachineStore();
        public InMemoryUserStore User { get; } = new InMemoryUserStore();
        public CountingFlusher Flusher { get; } = new CountingFlusher();
        public MutableDatabasePath DatabasePath { get; } = new MutableDatabasePath();
        public AppEnvironment Env { get; }

        public string HostsPath => Location.HostsPath;

        public void WriteHosts(byte[] bytes) => File.WriteAllBytes(HostsPath, bytes);
        public void WriteHosts(string latin1Text) => File.WriteAllBytes(HostsPath, Latin1.Encode(latin1Text));
        public byte[] ReadHosts() => File.Exists(HostsPath) ? File.ReadAllBytes(HostsPath) : Array.Empty<byte>();
        public string ReadHostsText() => Latin1.Decode(ReadHosts());
        public HostsDocument Document() => HostsDocument.Parse(File.Exists(HostsPath) ? File.ReadAllBytes(HostsPath) : null);

        public string[] BackupNames()
        {
            var names = new List<string>();
            foreach (string p in Directory.GetFiles(Location.Folder, "hosts.quiethosts-*.bak")) names.Add(Path.GetFileName(p));
            names.Sort(StringComparer.Ordinal);
            return names.ToArray();
        }

        public void Dispose() => Dir.Dispose();
    }

    public static class Bytes
    {
        public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);
        public static byte[] Concat(params byte[][] parts)
        {
            int n = 0;
            foreach (var p in parts) n += p.Length;
            var result = new byte[n];
            int o = 0;
            foreach (var p in parts) { Buffer.BlockCopy(p, 0, result, o, p.Length); o += p.Length; }
            return result;
        }
    }
}
