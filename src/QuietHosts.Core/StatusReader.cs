using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace QuietHosts.Core
{
    /// <summary>
    /// Computes <see cref="HostsStatus"/> without admin rights: reads the hosts file shared ReadWrite|Delete
    /// and the stores read-only. Never writes anything. It may wait briefly for a locked file (at most
    /// about 0.6 s), so the window can call it off the UI thread if it wants.
    /// </summary>
    public static class StatusReader
    {
        private static readonly int[] LockedDelaysMs = { 50, 100, 200 };
        private static readonly int[] DeniedDelaysMs = { 250 };

        public static HostsStatus Read(AppEnvironment env, BlockLists? lists = null)
        {
            if (env == null) throw new ArgumentNullException(nameof(env));
            MachineState machine = Try(() => env.Machine.Read(), MachineState.Empty);
            UserSettings user = Try(() => env.User.Read(), UserSettings.Empty);
            bool inProgress = WriteMutex.IsRunInProgress(env.WriteMutexName);
            bool changeNotFinished = machine.HasPending && !inProgress;
            List<string> allow = Allowlist.Prune(machine.Allowlist, lists);
            Level saved = machine.HasLevel ? machine.Level : Level.Standard;

            var common = new HostsStatus
            {
                HostsPath = env.Location.HostsPath,
                HostsFolder = env.Location.Folder,
                SavedEnabled = machine.Enabled,
                SavedLevel = saved,
                PendingLevel = user.PendingLevel,
                SelectedLevel = user.PendingLevel ?? saved,
                Allowlist = allow,
                AllowedInLevelCount = CountInLevel(allow, lists, user.PendingLevel ?? saved),
                FirstRunDone = user.FirstRunDone,
                LastRun = machine.LastRun,
                HasPendingMarker = machine.HasPending,
                RunInProgress = inProgress,
                IsSandbox = env.IsSandbox,
                CanUndo = machine.Exists,
                Warnings = changeNotFinished ? HostsWarnings.ChangeNotFinished : HostsWarnings.None,
            };

            string? dbPath;
            try
            {
                dbPath = env.DatabasePath.Read();
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return With(common, ProtectionState.Refused, RefusalReason.HostsPathMoved, movedFolder: "?", error: ex);
            }
            if (HostsDatabase.IsMoved(dbPath))
                return With(common, ProtectionState.Refused, RefusalReason.HostsPathMoved, movedFolder: dbPath);

            if (!TryReadFile(env, out byte[]? bytes, out Exception? readError))
                return With(common, ProtectionState.CantRead, RefusalReason.None, error: readError);

            HostsDocument doc = HostsDocument.Parse(bytes);
            string sha = Hashing.Sha256Hex(bytes ?? Array.Empty<byte>());
            if (doc.IsRefused)
                return With(common, ProtectionState.Refused, doc.Refusal, fileExists: doc.Exists, size: doc.Length, sha: sha);

            ProtectionState state = doc.HasBlock ? ProtectionState.On
                : machine.Enabled ? ProtectionState.Removed
                : doc.HasLegacyBlock ? ProtectionState.OlderList
                : ProtectionState.Off;

            HostsWarnings warnings = common.Warnings | doc.Warnings;
            if (state == ProtectionState.Removed && doc.HasLegacyBlock) warnings |= HostsWarnings.LegacyBlockToo;

            Level? blockLevel = null;
            if (doc.HasBlock) blockLevel = doc.Header?.Level ?? saved;
            Level selected = blockLevel ?? common.SelectedLevel;

            return new HostsStatus
            {
                State = state,
                Refusal = RefusalReason.None,
                Warnings = warnings,
                HostsPath = common.HostsPath,
                HostsFolder = common.HostsFolder,
                FileExists = doc.Exists,
                FileSize = doc.Length,
                BytesOutsideBlocks = doc.BytesOutsideBlocks,
                FileSha256 = sha,
                BlockedCount = doc.HasBlock ? (doc.Header?.HostCount ?? doc.BlockHosts.Count) : 0,
                BlockLevel = blockLevel,
                BlockListVersion = doc.HasBlock ? doc.Header?.ListVersion : null,
                BlockedHosts = doc.HasBlock ? new HashSet<string>(doc.BlockHosts, StringComparer.Ordinal) : (IReadOnlyCollection<string>)Array.Empty<string>(),
                FirstBlockedHost = doc.HasBlock ? doc.BlockHosts.FirstOrDefault() : null,
                LegacyCount = doc.LegacyCount,
                HasLegacyBlock = doc.HasLegacyBlock,
                SavedEnabled = common.SavedEnabled,
                SavedLevel = common.SavedLevel,
                PendingLevel = common.PendingLevel,
                SelectedLevel = selected,
                Allowlist = allow,
                AllowedInLevelCount = CountInLevel(allow, lists, selected),
                CanUndo = machine.Exists || doc.HasBlock || doc.HasLegacyBlock || doc.MalformedMarkers,
                FirstRunDone = common.FirstRunDone,
                LastRun = common.LastRun,
                HasPendingMarker = common.HasPendingMarker,
                RunInProgress = common.RunInProgress,
                IsSandbox = common.IsSandbox,
                // Fingerprints and lines for the window's notices; never written anywhere. Refused and Can't read keep the defaults.
                BlockLinesSha256 = doc.BlockLinesSha256,
                MicrosoftLines = doc.MicrosoftLines.ToArray(),
                MicrosoftLineCount = doc.MicrosoftLineCount,
                MicrosoftLinesSha256 = doc.MicrosoftLinesSha256,
            };
        }

        /// <summary>SHA-256 hex of the hosts file now, or null if it can't be read (for the two forms of Unexpected).</summary>
        public static string? HashHostsFile(AppEnvironment env) =>
            TryReadFile(env, out byte[]? bytes, out _) ? Hashing.Sha256Hex(bytes ?? Array.Empty<byte>()) : null;

        private static bool TryReadFile(AppEnvironment env, out byte[]? bytes, out Exception? error)
        {
            int locked = 0, denied = 0;
            while (true)
            {
                try
                {
                    bytes = env.Files.ReadFile(env.Location.HostsPath, Limits.MaxHostsFileBytes);
                    error = null;
                    return true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    FileFailure kind = FileRetry.Classify(ex);
                    if (kind == FileFailure.Locked && locked < LockedDelaysMs.Length) { env.Clock.Sleep(TimeSpan.FromMilliseconds(LockedDelaysMs[locked++])); continue; }
                    if (kind == FileFailure.AccessDenied && denied < DeniedDelaysMs.Length) { env.Clock.Sleep(TimeSpan.FromMilliseconds(DeniedDelaysMs[denied++])); continue; }
                    bytes = null;
                    error = ex;
                    return false;
                }
            }
        }

        private static int CountInLevel(IEnumerable<string> allow, BlockLists? lists, Level level)
        {
            if (lists == null) return 0;
            int n = 0;
            foreach (string h in allow)
            {
                ListEntry? e = lists.Find(h);
                if (e != null && (level == Level.Strict || e.Tier == Tier.Default)) n++;
            }
            return n;
        }

        private static HostsStatus With(HostsStatus c, ProtectionState state, RefusalReason refusal, string? movedFolder = null,
            Exception? error = null, bool fileExists = false, long size = 0, string? sha = null) => new HostsStatus
            {
                State = state,
                Refusal = refusal,
                Warnings = c.Warnings,
                MovedFolder = movedFolder,
                ReadError = error,
                FileExists = fileExists,
                FileSize = size,
                FileSha256 = sha,
                HostsPath = c.HostsPath,
                HostsFolder = c.HostsFolder,
                SavedEnabled = c.SavedEnabled,
                SavedLevel = c.SavedLevel,
                PendingLevel = c.PendingLevel,
                SelectedLevel = c.SelectedLevel,
                Allowlist = c.Allowlist,
                AllowedInLevelCount = c.AllowedInLevelCount,
                FirstRunDone = c.FirstRunDone,
                LastRun = c.LastRun,
                HasPendingMarker = c.HasPendingMarker,
                RunInProgress = c.RunInProgress,
                IsSandbox = c.IsSandbox,
                CanUndo = c.CanUndo,
            };

        private static T Try<T>(Func<T> f, T fallback)
        {
            try { return f(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return fallback; }
        }
    }

    /// <summary>The cross-process write mutex.</summary>
    public static class WriteMutex
    {
        /// <summary>
        /// True if an elevated run holds or created the mutex now: TryOpenExisting succeeds or throws
        /// UnauthorizedAccessException (an elevated run's mutex can't be opened by the unelevated window).
        /// </summary>
        public static bool IsRunInProgress(string name)
        {
            try
            {
                if (Mutex.TryOpenExisting(name, out Mutex? mutex))
                {
                    mutex.Dispose();
                    return true;
                }
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is WaitHandleCannotBeOpenedException)
            {
                return false;
            }
        }
    }
}
