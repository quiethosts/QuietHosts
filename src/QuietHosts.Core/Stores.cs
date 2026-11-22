using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>A snapshot of the per-PC state (HKLM\SOFTWARE\QuietHosts).</summary>
    public sealed class MachineState
    {
        public static MachineState Empty { get; } = new MachineState();

        /// <summary>The key (or sandbox file) exists.</summary>
        public bool Exists { get; init; }
        /// <summary>Enabled = 1: protection saved as on.</summary>
        public bool Enabled { get; init; }
        /// <summary>The saved level; Standard when none is saved.</summary>
        public Level Level { get; init; }
        /// <summary>True if a valid Level value is saved.</summary>
        public bool HasLevel { get; init; }
        /// <summary>Allowed addresses, normalized, unique, sorted, at most 2,000.</summary>
        public IReadOnlyList<string> Allowlist { get; init; } = Array.Empty<string>();
        /// <summary>SHA-256 hex of the hosts file before the write in progress, or null.</summary>
        public string? PendingBefore { get; init; }
        /// <summary>SHA-256 hex of the hosts file the write in progress should produce, or null.</summary>
        public string? PendingAfter { get; init; }
        public bool HasPending => PendingBefore != null || PendingAfter != null;
        /// <summary>Details of the last elevated run (for "Copy details"), or null.</summary>
        public string? LastRun { get; init; }

        internal static List<string> CleanAllowlist(IEnumerable<string>? raw)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string s in raw ?? Enumerable.Empty<string>())
            {
                if (set.Count >= Limits.MaxAllowlist) break;
                if (HostName.TryNormalize(s, out string host)) set.Add(host);
            }
            return set.ToList();
        }

        internal static string? CleanHash(string? value) => Hashing.IsSha256Hex(value) ? value : null;
    }

    /// <summary>
    /// Per-PC state. Only elevated runs write it; the window only calls <see cref="Read"/>.
    /// Implementations: <see cref="RegistryMachineStore"/>, <see cref="InMemoryMachineStore"/>, <see cref="FileMachineStore"/>.
    /// </summary>
    public interface IMachineStore
    {
        /// <summary>Reads everything; missing or bad values read as defaults.</summary>
        MachineState Read();
        void SetEnabled(bool enabled);
        void SetLevel(Level level);
        /// <summary>Saves at most 2,000 normalized entries.</summary>
        void SetAllowlist(IEnumerable<string> hosts);
        void SetPending(string beforeSha256, string afterSha256);
        void ClearPending();
        /// <summary>Saves LastRun, cut to 8,000 characters.</summary>
        void SetLastRun(string text);
        /// <summary>Deletes the whole key (Undo).</summary>
        void DeleteAll();
    }

    /// <summary>A snapshot of the per-user settings (HKCU\Software\QuietHosts).</summary>
    public sealed class UserSettings
    {
        public static UserSettings Empty { get; } = new UserSettings();

        public bool Exists { get; init; }
        public bool FirstRunDone { get; init; }
        /// <summary>The level chosen while protection was off; used at the next Turn on.</summary>
        public Level? PendingLevel { get; init; }
        public int? MainX { get; init; }
        public int? MainY { get; init; }
        public int? FixW { get; init; }
        public int? FixH { get; init; }
        /// <summary>
        /// The notices the user closed, as "id=fingerprint" entries, already cleaned (<see cref="NoticeDismissalText.Clean"/>):
        /// at most one per id, at most 16. The window ignores ids it doesn't know.
        /// </summary>
        public IReadOnlyList<string> DismissedNotices { get; init; } = Array.Empty<string>();
    }

    /// <summary>Per-user settings. The window reads and writes these itself, without elevation.</summary>
    public interface IUserStore
    {
        UserSettings Read();
        void SetFirstRunDone(bool done);
        /// <summary>Saves the pending level, or deletes it when null.</summary>
        void SetPendingLevel(Level? level);
        void SetMainPosition(int x, int y);
        void SetFixSize(int width, int height);
        /// <summary>
        /// Saves the closed notices: <see cref="NoticeDismissalText.Clean"/> of <paramref name="entries"/>, or deletes the
        /// value when nothing is left. The other settings stay as they are.
        /// </summary>
        void SetDismissedNotices(IEnumerable<string> entries);
        /// <summary>Deletes the whole key (after a successful Undo).</summary>
        void DeleteAll();
    }

    /// <summary>
    /// The per-user "DismissedNotices" entries: "id=fingerprint", where the id is a notice's lowercase name and the
    /// fingerprint says which condition was closed (a hash, a size, versions or a hashed path, never file contents).
    /// </summary>
    public static class NoticeDismissalText
    {
        public const int MaxEntries = 16, MaxLength = 128;

        // \A and \z, not ^ and $: in .NET, $ also matches before a final "\n".
        private static readonly Regex Entry = new Regex(@"\A[a-z][a-z0-9-]{0,31}=[A-Za-z0-9.:|_-]{1,95}\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// The entries to keep: those that match the pattern and are at most 128 characters; for each id the last entry
        /// wins (in the place of the first one); at most the first 16. Ordinal and culture-free. Unknown ids are kept: the
        /// window drops them.
        /// </summary>
        public static List<string> Clean(IEnumerable<string>? raw)
        {
            var result = new List<string>();
            if (raw == null) return result;
            var at = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string? entry in raw)
            {
                if (entry == null || entry.Length > MaxLength || !Entry.IsMatch(entry)) continue;
                string id = entry.Substring(0, entry.IndexOf('='));
                if (at.TryGetValue(id, out int index))
                {
                    result[index] = entry;
                }
                else
                {
                    at[id] = result.Count;
                    result.Add(entry);
                }
            }
            if (result.Count > MaxEntries) result.RemoveRange(MaxEntries, result.Count - MaxEntries);
            return result;
        }
    }

    internal static class LastRunText
    {
        public static string Cap(string? text)
        {
            if (text == null) return string.Empty;
            return text.Length <= Limits.MaxLastRunChars ? text : text.Substring(0, Limits.MaxLastRunChars);
        }
    }

    /// <summary>An in-memory per-PC store for tests.</summary>
    public sealed class InMemoryMachineStore : IMachineStore
    {
        private readonly object _gate = new object();
        private bool _exists, _enabled, _hasLevel;
        private Level _level;
        private List<string> _allow = new List<string>();
        private string? _before, _after, _lastRun;

        /// <summary>Counts writes, so tests can see what a run touched.</summary>
        public int WriteCount { get; private set; }
        /// <summary>Called on every write with the value name (tests can inject faults here).</summary>
        public Action<string>? OnWrite { get; set; }

        public MachineState Read()
        {
            lock (_gate)
            {
                if (!_exists) return MachineState.Empty;
                return new MachineState
                {
                    Exists = true,
                    Enabled = _enabled,
                    Level = _level,
                    HasLevel = _hasLevel,
                    Allowlist = _allow.ToList(),
                    PendingBefore = _before,
                    PendingAfter = _after,
                    LastRun = _lastRun,
                };
            }
        }

        public void SetEnabled(bool enabled) => Change("Enabled", () => _enabled = enabled);
        public void SetLevel(Level level) => Change("Level", () => { _level = level; _hasLevel = true; });
        public void SetAllowlist(IEnumerable<string> hosts) => Change("Allowlist", () => _allow = MachineState.CleanAllowlist(hosts));
        public void SetPending(string beforeSha256, string afterSha256) => Change("Pending", () => { _before = MachineState.CleanHash(beforeSha256); _after = MachineState.CleanHash(afterSha256); });
        public void ClearPending() => Change("ClearPending", () => { _before = null; _after = null; });
        public void SetLastRun(string text) => Change("LastRun", () => _lastRun = LastRunText.Cap(text));

        public void DeleteAll()
        {
            OnWrite?.Invoke("DeleteAll");
            lock (_gate)
            {
                WriteCount++;
                _exists = _enabled = _hasLevel = false;
                _level = Level.Standard;
                _allow = new List<string>();
                _before = _after = _lastRun = null;
            }
        }

        private void Change(string name, Action apply)
        {
            OnWrite?.Invoke(name);
            lock (_gate)
            {
                WriteCount++;
                _exists = true;
                apply();
            }
        }
    }

    /// <summary>An in-memory per-user store for tests.</summary>
    public sealed class InMemoryUserStore : IUserStore
    {
        private readonly object _gate = new object();
        private UserSettings _s = UserSettings.Empty;

        public UserSettings Read() { lock (_gate) return _s; }
        public void SetFirstRunDone(bool done) => Update(s => With(s, firstRun: done));
        public void SetPendingLevel(Level? level) => Update(s => With(s, pendingLevel: level, setPending: true));
        public void SetMainPosition(int x, int y) => Update(s => With(s, mainX: x, mainY: y));
        public void SetFixSize(int width, int height) => Update(s => With(s, fixW: width, fixH: height));
        public void SetDismissedNotices(IEnumerable<string> entries) => Update(s => With(s, dismissed: NoticeDismissalText.Clean(entries)));
        public void DeleteAll() { lock (_gate) _s = UserSettings.Empty; }

        private void Update(Func<UserSettings, UserSettings> f) { lock (_gate) _s = f(_s); }

        // Every setter goes through here, so each one must carry over every other value (DismissedNotices included).
        private static UserSettings With(UserSettings s, bool? firstRun = null, Level? pendingLevel = null, bool setPending = false,
            int? mainX = null, int? mainY = null, int? fixW = null, int? fixH = null, IReadOnlyList<string>? dismissed = null) => new UserSettings
            {
                Exists = true,
                FirstRunDone = firstRun ?? s.FirstRunDone,
                PendingLevel = setPending ? pendingLevel : s.PendingLevel,
                MainX = mainX ?? s.MainX,
                MainY = mainY ?? s.MainY,
                FixW = fixW ?? s.FixW,
                FixH = fixH ?? s.FixH,
                DismissedNotices = dismissed ?? s.DismissedNotices,
            };
    }
}
