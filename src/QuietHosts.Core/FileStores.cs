using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QuietHosts.Core
{
    /// <summary>"key=value" lines with \\, \n and \r escaped; unknown keys are ignored, bad values read as defaults.</summary>
    internal static class KeyValueFile
    {
        public static Dictionary<string, string>? Read(string path)
        {
            string text;
            try
            {
                if (!File.Exists(path)) return null;
                text = File.ReadAllText(path, new UTF8Encoding(false));
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                values[line.Substring(0, eq)] = Unescape(line.Substring(eq + 1));
            }
            return values;
        }

        public static void Write(string path, Dictionary<string, string> values)
        {
            var sb = new StringBuilder();
            foreach (var kv in values.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(Escape(kv.Value)).Append('\n');
            string? dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            string temp = path + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static void Update(string path, Action<Dictionary<string, string>> change)
        {
            var values = Read(path) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            change(values);
            Write(path, values);
        }

        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static int? Int(Dictionary<string, string> v, string key) =>
            v.TryGetValue(key, out string? s) && int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : (int?)null;

        public static string? Str(Dictionary<string, string> v, string key) => v.TryGetValue(key, out string? s) ? s : null;
    }

    /// <summary>Per-PC state in a file (the Debug sandbox's machine.ini).</summary>
    public sealed class FileMachineStore : IMachineStore
    {
        private readonly string _path;

        public FileMachineStore(string path) { _path = Path.GetFullPath(path); }

        public string FilePath => _path;

        public MachineState Read()
        {
            var v = KeyValueFile.Read(_path);
            if (v == null) return MachineState.Empty;
            bool hasLevel = LevelNames.TryParse(KeyValueFile.Str(v, "Level"), out Level level);
            string allow = KeyValueFile.Str(v, "Allowlist") ?? string.Empty;
            return new MachineState
            {
                Exists = true,
                Enabled = KeyValueFile.Int(v, "Enabled") == 1,
                Level = level,
                HasLevel = hasLevel,
                Allowlist = MachineState.CleanAllowlist(allow.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)),
                PendingBefore = MachineState.CleanHash(KeyValueFile.Str(v, "PendingBefore")),
                PendingAfter = MachineState.CleanHash(KeyValueFile.Str(v, "PendingAfter")),
                LastRun = KeyValueFile.Str(v, "LastRun"),
            };
        }

        public void SetEnabled(bool enabled) => KeyValueFile.Update(_path, v => v["Enabled"] = enabled ? "1" : "0");
        public void SetLevel(Level level) => KeyValueFile.Update(_path, v => v["Level"] = LevelNames.ToKeyword(level));
        public void SetAllowlist(IEnumerable<string> hosts) => KeyValueFile.Update(_path, v => v["Allowlist"] = string.Join(" ", MachineState.CleanAllowlist(hosts)));
        public void SetPending(string beforeSha256, string afterSha256) => KeyValueFile.Update(_path, v =>
        {
            v["PendingBefore"] = MachineState.CleanHash(beforeSha256) ?? string.Empty;
            v["PendingAfter"] = MachineState.CleanHash(afterSha256) ?? string.Empty;
        });
        public void ClearPending()
        {
            if (!File.Exists(_path)) return;
            KeyValueFile.Update(_path, v => { v.Remove("PendingBefore"); v.Remove("PendingAfter"); });
        }
        public void SetLastRun(string text) => KeyValueFile.Update(_path, v => v["LastRun"] = LastRunText.Cap(text));
        public void DeleteAll() { if (File.Exists(_path)) File.Delete(_path); }
    }

    /// <summary>Per-user settings in a file (the Debug sandbox's user.ini).</summary>
    public sealed class FileUserStore : IUserStore
    {
        private readonly string _path;

        public FileUserStore(string path) { _path = Path.GetFullPath(path); }

        public UserSettings Read()
        {
            var v = KeyValueFile.Read(_path);
            if (v == null) return UserSettings.Empty;
            return new UserSettings
            {
                Exists = true,
                FirstRunDone = KeyValueFile.Int(v, "FirstRunDone") == 1,
                PendingLevel = LevelNames.TryParse(KeyValueFile.Str(v, "PendingLevel"), out Level l) ? l : (Level?)null,
                MainX = KeyValueFile.Int(v, "MainX"),
                MainY = KeyValueFile.Int(v, "MainY"),
                FixW = KeyValueFile.Int(v, "FixW"),
                FixH = KeyValueFile.Int(v, "FixH"),
                DismissedNotices = NoticeDismissalText.Clean(KeyValueFile.Str(v, "DismissedNotices")?.Split('\n')),
            };
        }

        /// <summary>The cleaned entries joined with "\n" (the file escapes it); no key when nothing is left.</summary>
        public void SetDismissedNotices(IEnumerable<string> entries)
        {
            List<string> clean = NoticeDismissalText.Clean(entries);
            if (clean.Count == 0 && !File.Exists(_path)) return;
            KeyValueFile.Update(_path, v =>
            {
                if (clean.Count > 0) v["DismissedNotices"] = string.Join("\n", clean);
                else v.Remove("DismissedNotices");
            });
        }

        public void SetFirstRunDone(bool done) => KeyValueFile.Update(_path, v => v["FirstRunDone"] = done ? "1" : "0");
        public void SetPendingLevel(Level? level) => KeyValueFile.Update(_path, v =>
        {
            if (level.HasValue) v["PendingLevel"] = LevelNames.ToKeyword(level.Value);
            else v.Remove("PendingLevel");
        });
        public void SetMainPosition(int x, int y) => KeyValueFile.Update(_path, v => { v["MainX"] = Inv.Num(x); v["MainY"] = Inv.Num(y); });
        public void SetFixSize(int width, int height) => KeyValueFile.Update(_path, v => { v["FixW"] = Inv.Num(width); v["FixH"] = Inv.Num(height); });
        public void DeleteAll() { if (File.Exists(_path)) File.Delete(_path); }
    }
}
