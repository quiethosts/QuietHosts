using System;
using System.Collections.Generic;
using System.IO;

namespace QuietHosts.Core
{
    /// <summary>
    /// The write pipeline: recover, read, verified backup, pending marker, concurrency check,
    /// in-place write with retries, read-back, 2-second re-check, rollback and DNS flush. The caller
    /// (an elevated run) holds the write mutex. Never changes ACLs or owners; never writes temp files.
    /// </summary>
    public sealed class HostsWriter
    {
        public static readonly TimeSpan RecheckDelay = TimeSpan.FromSeconds(2);
        private readonly AppEnvironment _env;
        private readonly List<string> _log = new List<string>();

        public HostsWriter(AppEnvironment env)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            CheckTarget(env.Location.HostsPath, env.IsReal, HostsLocation.InsideQuietHostsExe());
        }

        /// <summary>
        /// The real hosts file, or anything under the Windows folder, is written only by the real environment inside
        /// QuietHosts.exe, whatever the environment claims (an admin CI runner must never get there).
        /// </summary>
        internal static void CheckTarget(string hostsPath, bool isReal, bool insideQuietHostsExe)
        {
            bool system = HostsLocation.IsRealHostsTarget(hostsPath) || PathUtil.IsUnderWindowsFolder(hostsPath);
            if (system && !(isReal && insideQuietHostsExe))
                throw new InvalidOperationException("Refusing to write the real hosts file (or anything under the Windows folder) outside QuietHosts.exe's real environment.");
        }

        /// <summary>What the writer did, one line per step that matters, for LastRun. Never file contents.</summary>
        public IReadOnlyList<string> Log => _log;

        private string HostsPath => _env.Location.HostsPath;

        /// <summary>
        /// Step 1: if a pending marker exists, compare the file with its hashes, log what happened and
        /// clear it. Runs at the start of every elevated run (Write runs it too). If the file can't be
        /// read, the marker stays.
        /// </summary>
        public void Recover()
        {
            MachineState state = _env.Machine.Read();
            if (!state.HasPending) return;
            if (ReadFile(out byte[]? bytes, out Exception? error) != FileFailure.None)
            {
                _log.Add("recovery: a change didn't finish, and the hosts file can't be read now (" + error?.GetType().Name + "); marker kept");
                return;
            }
            string sha = Hashing.Sha256Hex(bytes ?? Array.Empty<byte>());
            if (string.Equals(sha, state.PendingAfter, StringComparison.Ordinal))
                _log.Add("recovery: the previous change had been written; marker cleared");
            else if (string.Equals(sha, state.PendingBefore, StringComparison.Ordinal))
                _log.Add("recovery: the previous change hadn't been written; marker cleared");
            else
                _log.Add("recovery: the hosts file matched neither hash of the unfinished change; marker cleared; backups are in " + _env.Location.Folder);
            _env.Machine.ClearPending();
        }

        /// <summary>
        /// Runs the pipeline. <paramref name="compute"/> gets the parsed file and returns the bytes it
        /// should have; it may run twice (if another program changes the file meanwhile).
        /// </summary>
        public WriteOutcome Write(Func<HostsDocument, byte[]> compute)
        {
            if (compute == null) throw new ArgumentNullException(nameof(compute));
            Recover();

            string? moved;
            try
            {
                moved = _env.DatabasePath.Read();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                _log.Add("refused: DataBasePath can't be read: " + ex.GetType().Name);
                return Refused(RefusalReason.HostsPathMoved);
            }
            if (HostsDatabase.IsMoved(moved))
            {
                _log.Add("refused: DataBasePath is " + moved);
                return Refused(RefusalReason.HostsPathMoved);
            }

            bool pendingSet = false;
            _writeAttempted = false;
            _settled = false;
            string? backupPath = null, beforeSha = null, afterSha = null;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    // Step 2: read and compute.
                    FileFailure readFailure = ReadFile(out byte[]? read, out Exception? readError);
                    if (readFailure != FileFailure.None) return Fail(FileRetry.ToResult(readFailure), "read failed", readError, backupPath, beforeSha, afterSha);
                    byte[] before = read ?? Array.Empty<byte>();
                    HostsDocument doc = HostsDocument.Parse(read);
                    if (doc.IsRefused)
                    {
                        _log.Add("refused: " + doc.Refusal);
                        return Refused(doc.Refusal);
                    }
                    byte[] after;
                    try
                    {
                        after = compute(doc);
                    }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        return Fail(ResultKind.Unexpected, "computing the new file failed", ex, backupPath, beforeSha, afterSha);
                    }
                    beforeSha = Hashing.Sha256Hex(before);
                    afterSha = Hashing.Sha256Hex(after);
                    if (Hashing.BytesEqual(before, after))
                    {
                        _log.Add("no change needed");
                        return new WriteOutcome(ResultKind.Success, false, RefusalReason.None, null, beforeSha, afterSha, null);
                    }
                    // A file over the limit couldn't be read back (and would be refused afterwards): never write one.
                    if (after.LongLength > Limits.MaxHostsFileBytes)
                    {
                        _log.Add("refused: the changed file would be larger than 16 MB");
                        return Refused(RefusalReason.TooLarge);
                    }

                    // Step 3: verified backup, or stop.
                    if (!Backup(before, out backupPath, out Exception? backupError))
                        return Fail(ResultKind.Unexpected, "backup failed", backupError, backupPath, beforeSha, afterSha);

                    // Step 4: mark pending.
                    _env.Machine.SetPending(beforeSha, afterSha);
                    pendingSet = true;

                    // Step 5: concurrency.
                    readFailure = ReadFile(out byte[]? again, out readError);
                    if (readFailure != FileFailure.None) return Fail(FileRetry.ToResult(readFailure), "re-read failed", readError, backupPath, beforeSha, afterSha);
                    if (!Hashing.BytesEqual(again ?? Array.Empty<byte>(), before))
                    {
                        if (attempt == 0)
                        {
                            _log.Add("the hosts file changed while QuietHosts prepared the change; starting over");
                            continue;
                        }
                        return Fail(ResultKind.FileLocked, "the hosts file keeps changing", null, backupPath, beforeSha, afterSha);
                    }

                    // Steps 6-10.
                    _writeAttempted = true;
                    ResultKind result = WriteAndVerify(before, after, backupPath);
                    if (result != ResultKind.Success)
                        return new WriteOutcome(result, false, RefusalReason.None, backupPath, beforeSha, afterSha, _lastError);

                    // Step 11: flush DNS; a failure is only logged.
                    try
                    {
                        bool flushed = _env.Flusher.Flush(out string detail);
                        _log.Add("dns flush: " + (flushed ? "ok" : "failed") + " (" + detail + ")");
                    }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        _log.Add("dns flush failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                    _log.Add("written and verified");
                    return new WriteOutcome(ResultKind.Success, true, RefusalReason.None, backupPath, beforeSha, afterSha, null);
                }
                return Fail(ResultKind.FileLocked, "the hosts file keeps changing", null, backupPath, beforeSha, afterSha);
            }
            finally
            {
                // Step 12 (the rest is the caller's): clear pending. After a write attempt that left the file
                // neither as it was nor as intended, or unreadable, the marker stays: the window then shows
                // ChangeNotFinished, and the next run's recovery logs what it finds.
                if (pendingSet && (!_writeAttempted || _settled))
                {
                    try { _env.Machine.ClearPending(); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException)) { _log.Add("clearing the pending marker failed: " + ex.GetType().Name); }
                }
                else if (pendingSet)
                {
                    _log.Add("pending marker kept: the hosts file may be neither the old nor the new version");
                }
            }
        }

        private Exception? _lastError;
        // Set by Write: a write of the hosts file was attempted in this run.
        private bool _writeAttempted;
        // Set by WriteAndVerify and RollBack: the last read showed the file as it was before or as intended.
        private bool _settled;

        private ResultKind WriteAndVerify(byte[] before, byte[] after, string? backupPath)
        {
            _lastError = null;
            FileFailure failure = WriteBytes(after, out Exception? writeError);
            if (failure != FileFailure.None)
            {
                _lastError = writeError;
                _log.Add("write failed: " + failure + " (" + writeError?.GetType().Name + ": " + writeError?.Message + ")");
                // The window says "nothing was changed" for Locked and AccessDenied, so check it: a failure after
                // the bytes went out (SetLength or the flush) leaves the new file over the old one's tail.
                ResultKind kind = FileRetry.ToResult(failure);
                bool readable = ReadFile(out byte[]? state, out Exception? stateError) == FileFailure.None;
                if (readable && Hashing.BytesEqual(state ?? Array.Empty<byte>(), before))
                {
                    _settled = true;
                    return kind;
                }
                if (!readable && kind != ResultKind.Unexpected)
                {
                    // Most likely another program holds the file, so the write never opened it; the marker stays.
                    _log.Add("the hosts file can't be read to check it: " + stateError?.GetType().Name);
                    return kind;
                }
                _log.Add("the failed write may have changed the file; rolling back");
                return RollBack(before, backupPath) ? kind : ResultKind.Unexpected;
            }

            // Step 9: read back at once, then again after 2 seconds.
            if (ReadFile(out byte[]? readBack, out Exception? rbError) != FileFailure.None)
            {
                _lastError = rbError;
                _log.Add("read-back failed: " + rbError?.GetType().Name);
                return ResultKind.Unexpected;
            }
            byte[] now = readBack ?? Array.Empty<byte>();
            if (Hashing.BytesEqual(now, before))
            {
                _settled = true;
                _log.Add("the write was silently blocked: the file is unchanged");
                return ResultKind.AccessDenied;
            }
            if (!Hashing.BytesEqual(now, after))
            {
                _log.Add("read-back differs from what was written; rolling back");
                return RollBack(before, backupPath) ? ResultKind.Reverted : ResultKind.Unexpected;
            }

            _env.Clock.Sleep(RecheckDelay);
            if (ReadFile(out byte[]? later, out Exception? laterError) != FileFailure.None)
            {
                _lastError = laterError;
                _log.Add("the 2-second re-read failed: " + laterError?.GetType().Name);
                return ResultKind.Unexpected;
            }
            byte[] then = later ?? Array.Empty<byte>();
            if (!Hashing.BytesEqual(then, after))
            {
                _settled = Hashing.BytesEqual(then, before);
                _log.Add("something changed the file back within 2 seconds; not fighting it");
                return ResultKind.Reverted;
            }
            _settled = true;
            return ResultKind.Success;
        }

        // Step 10: write `before` back (steps 6-8) and check it. The check runs whatever the write reported: its bytes
        // may all have gone out before SetLength or the flush failed, and then the file is as it was.
        private bool RollBack(byte[] before, string? backupPath)
        {
            FileFailure failure = WriteBytes(before, out Exception? error);
            if (ReadFile(out byte[]? check, out _) == FileFailure.None && Hashing.BytesEqual(check ?? Array.Empty<byte>(), before))
            {
                _settled = true;
                _log.Add("rolled back to the previous file" +
                    (failure != FileFailure.None ? " (the write reported " + error?.GetType().Name + ": " + error?.Message + ")" : ""));
                return true;
            }
            _lastError ??= error;
            _log.Add("rollback failed" + (error != null ? ": " + error.GetType().Name + ": " + error.Message : "") + "; backup: " + (backupPath ?? "none"));
            return false;
        }

        // Steps 6-8: clear the read-only attribute, write in place with retries, restore the attribute.
        private FileFailure WriteBytes(byte[] bytes, out Exception? error)
        {
            FileAttributes? restore = null;
            try
            {
                if (_env.Files.FileExists(HostsPath))
                {
                    FileAttributes attrs = _env.Files.GetAttributes(HostsPath);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        _env.Files.SetAttributes(HostsPath, attrs & ~FileAttributes.ReadOnly);
                        restore = attrs;
                        _log.Add("cleared the read-only attribute");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex;
                return FileRetry.Classify(ex);
            }
            try
            {
                return FileRetry.Run(_env.Clock, () => _env.Files.WriteInPlace(HostsPath, bytes), out error);
            }
            finally
            {
                if (restore.HasValue)
                {
                    try { _env.Files.SetAttributes(HostsPath, restore.Value); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { _log.Add("restoring the read-only attribute failed: " + ex.GetType().Name); }
                }
            }
        }

        private FileFailure ReadFile(out byte[]? bytes, out Exception? error)
        {
            byte[]? result = null;
            FileFailure failure = FileRetry.Run(_env.Clock, () => result = _env.Files.ReadFile(HostsPath, Limits.MaxHostsFileBytes), out error);
            bytes = result;
            return failure;
        }

        // Step 3: a timestamped backup (unless identical to the newest), verified by SHA-256, plus the original backup once.
        private bool Backup(byte[] before, out string? backupPath, out Exception? error)
        {
            backupPath = null;
            error = null;
            string folder = _env.Location.Folder;
            string sha = Hashing.Sha256Hex(before);
            try
            {
                List<string> existing = Backups.ListTimestamped(_env.Files, folder);
                if (existing.Count > 0)
                {
                    string newest = Path.Combine(folder, existing[0]);
                    byte[]? newestBytes = _env.Files.ReadFile(newest, Limits.MaxHostsFileBytes);
                    if (newestBytes != null && string.Equals(Hashing.Sha256Hex(newestBytes), sha, StringComparison.Ordinal))
                    {
                        backupPath = newest;
                        _log.Add("backup: identical to the newest, not written again: " + newest);
                    }
                }
                if (backupPath == null)
                {
                    // Named from UTC, so a time-zone or daylight-saving change can't reorder the names.
                    DateTime time = _env.Clock.UtcNow;
                    string path = Path.Combine(folder, Backups.NameFor(time));
                    for (int i = 0; i < 1000 && _env.Files.FileExists(path); i++)
                    {
                        time = time.AddMilliseconds(1);
                        path = Path.Combine(folder, Backups.NameFor(time));
                    }
                    if (!WriteVerified(path, before, sha, out error)) return false;
                    backupPath = path;
                    _log.Add("backup: " + path);
                }

                string original = Path.Combine(folder, Backups.OriginalName);
                if (!_env.Files.FileExists(original))
                {
                    if (!WriteVerified(original, before, sha, out error)) return false;
                    _log.Add("backup: saved the original file as " + original);
                }

                // This write's own backup is never pruned, even if a clock that went back gave it an older name.
                Backups.Prune(_env.Files, folder, Limits.BackupsKept, _log, Path.GetFileName(backupPath));
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = ex;
                return false;
            }
        }

        private bool WriteVerified(string path, byte[] bytes, string sha, out Exception? error)
        {
            FileFailure failure = FileRetry.Run(_env.Clock, () => _env.Files.CreateNew(path, bytes), out error);
            if (failure != FileFailure.None)
            {
                _log.Add("backup write failed: " + path + ": " + error?.GetType().Name);
                RemovePartialCopy(path, bytes);
                return false;
            }
            byte[]? check = null;
            failure = FileRetry.Run(_env.Clock, () => check = _env.Files.ReadFile(path, Limits.MaxHostsFileBytes), out error);
            if (failure != FileFailure.None || check == null || !string.Equals(Hashing.Sha256Hex(check), sha, StringComparison.Ordinal))
            {
                _log.Add("backup verification failed: " + path);
                error ??= new IOException("The backup didn't read back the same.");
                try { _env.Files.DeleteFile(path); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { _log.Add("removing the bad backup failed: " + ex.GetType().Name); }
                return false;
            }
            return true;
        }

        // After a failed backup write. The path didn't exist when the write started (both callers check), so a file
        // there that holds only the start of these bytes (or all of them) is a partial copy of ours: an attempt created
        // it and then failed, perhaps before a retry that found it there ("file exists"). It is deleted, or a short
        // hosts.quiethosts-original.bak would be kept for good, since that file is never written again. A file with
        // other bytes isn't ours and stays.
        private void RemovePartialCopy(string path, byte[] bytes)
        {
            byte[]? left = null;
            FileFailure failure = FileRetry.Run(_env.Clock, () => left = _env.Files.ReadFile(path, Limits.MaxHostsFileBytes), out Exception? error);
            if (failure != FileFailure.None)
            {
                _log.Add("partial backup may be left: " + path + " (" + error?.GetType().Name + ")");
                return;
            }
            if (left == null || !StartsWith(bytes, left)) return;
            failure = FileRetry.Run(_env.Clock, () => _env.Files.DeleteFile(path), out error);
            _log.Add(failure == FileFailure.None
                ? "backup: removed the partial copy " + path
                : "partial backup left: " + path + " (" + error?.GetType().Name + ")");
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
        {
            if (prefix.Length > bytes.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i]) return false;
            }
            return true;
        }

        private WriteOutcome Refused(RefusalReason reason) =>
            new WriteOutcome(ResultKind.Refused, false, reason, null, null, null, null);

        private WriteOutcome Fail(ResultKind kind, string what, Exception? error, string? backupPath, string? beforeSha, string? afterSha)
        {
            _log.Add(what + (error != null ? ": " + error.GetType().Name + ": " + error.Message : ""));
            return new WriteOutcome(kind, false, RefusalReason.None, backupPath, beforeSha, afterSha, error);
        }
    }
}
