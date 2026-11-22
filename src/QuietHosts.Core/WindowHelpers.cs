using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace QuietHosts.Core
{
    /// <summary>The run-location tips.</summary>
    [Flags]
    public enum RunLocationTips
    {
        None = 0,
        /// <summary>The exe's folder is the Downloads known folder or under it.</summary>
        RunningFromDownloads = 1,
        /// <summary>The exe's folder is under %TEMP% (for example, opened from inside a zip file).</summary>
        RunningFromTemp = 2,
    }

    public static class RunLocation
    {
        /// <summary>Which tips apply to an exe path (pass the exe's file path, as the window does).</summary>
        public static RunLocationTips Check(string exePath) =>
            Check(exePath, Native.DownloadsFolder(), Path.GetTempPath());

        internal static RunLocationTips Check(string exePath, string? downloadsFolder, string tempFolder)
        {
            var tips = RunLocationTips.None;
            string? folder;
            try { folder = Path.GetDirectoryName(Path.GetFullPath(exePath)); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return tips; }
            if (folder == null) return tips;
            if (!string.IsNullOrEmpty(downloadsFolder) && Within(folder, downloadsFolder!, orSame: true)) tips |= RunLocationTips.RunningFromDownloads;
            if (Within(folder, tempFolder, orSame: true)) tips |= RunLocationTips.RunningFromTemp;
            return tips;
        }

        private static bool Within(string folder, string root, bool orSame)
        {
            string r;
            try { r = Path.GetFullPath(root); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException) { return false; }
            if (orSame ? PathUtil.IsUnderOrSame(folder, r) : PathUtil.IsStrictlyUnder(folder, r)) return true;
            // %TEMP% is sometimes an 8.3 short path: compare the resolved forms too.
            string? f = Directory.Exists(folder) ? Native.FinalPath(folder) : null;
            string? fr = Directory.Exists(r) ? Native.FinalPath(r) : null;
            return f != null && fr != null && (orSame ? PathUtil.IsUnderOrSame(f, fr) : PathUtil.IsStrictlyUnder(f, fr));
        }
    }

    /// <summary>One row of the Fix dialog's list.</summary>
    public sealed class FixRow
    {
        internal FixRow(ListEntry entry, bool allowed)
        {
            Entry = entry;
            Allowed = allowed;
        }

        public ListEntry Entry { get; }
        /// <summary>Address column.</summary>
        public string Host => Entry.Host;
        /// <summary>Company column ("" if unknown).</summary>
        public string Company => Entry.Company;
        /// <summary>"What it is" column: the category's UI words.</summary>
        public string CategoryWords => Entry.CategoryWords;
        /// <summary>Level column: the window words Default as "Standard" and Strict as "Strict".</summary>
        public Tier Tier => Entry.Tier;
        /// <summary>Known effects column: the note, or "" (the window shows "None known").</summary>
        public string Note => Entry.Note;
        /// <summary>Status column: true = "Allowed by you", false = "Blocked".</summary>
        public bool Allowed { get; }
    }

    /// <summary>The Fix dialog's rows and search.</summary>
    public static class FixList
    {
        /// <summary>Rows: what the level blocks, plus allowed addresses; allowed first, then A-Z (ordinal).</summary>
        public static List<FixRow> Build(BlockLists lists, Level level, IEnumerable<string>? allowlist)
        {
            if (lists == null) throw new ArgumentNullException(nameof(lists));
            var allowed = new HashSet<string>(allowlist ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var rows = new List<FixRow>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ListEntry e in lists.ForLevel(level))
            {
                if (seen.Add(e.Host)) rows.Add(new FixRow(e, allowed.Contains(e.Host)));
            }
            foreach (string h in allowed)
            {
                ListEntry? e = lists.Find(h);
                if (e != null && seen.Add(e.Host)) rows.Add(new FixRow(e, true));
            }
            rows.Sort((a, b) =>
            {
                if (a.Allowed != b.Allowed) return a.Allowed ? -1 : 1;
                return string.CompareOrdinal(a.Host, b.Host);
            });
            return rows;
        }

        /// <summary>
        /// Case-insensitive substring search over address, company, "What it is" and note (ToLowerInvariant
        /// and ordinal, so culture-specific casing never changes the result). An empty query matches everything.
        /// </summary>
        public static List<FixRow> Search(IEnumerable<FixRow> rows, string? query)
        {
            string q = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length == 0) return rows.ToList();
            return rows.Where(r =>
                Contains(r.Host, q) || Contains(r.Company, q) || Contains(r.CategoryWords, q) || Contains(r.Note, q)).ToList();
        }

        private static bool Contains(string field, string lowerQuery) =>
            field.Length > 0 && field.ToLowerInvariant().IndexOf(lowerQuery, StringComparison.Ordinal) >= 0;
    }

    /// <summary>Helpers for "Copy details".</summary>
    public static class Diagnostics
    {
        /// <summary>Replaces the user's profile path with %USERPROFILE% (ignoring case).</summary>
        public static string MaskProfilePath(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return MaskPath(text!, profile, "%USERPROFILE%");
        }

        internal static string MaskPath(string text, string path, string replacement)
        {
            if (string.IsNullOrEmpty(path) || path.Length < 4) return text;
            var sb = new System.Text.StringBuilder(text.Length);
            int start = 0;
            while (true)
            {
                int i = text.IndexOf(path, start, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                sb.Append(text, start, i - start).Append(replacement);
                start = i + path.Length;
            }
            sb.Append(text, start, text.Length - start);
            return sb.ToString();
        }

        /// <summary>The Windows build, for example "10.0.26100.1234", read from the registry (read-only).</summary>
        public static string WindowsBuild()
        {
            try
            {
                using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    string? build = key?.GetValue("CurrentBuildNumber") as string;
                    object? ubr = key?.GetValue("UBR");
                    int major = key?.GetValue("CurrentMajorVersionNumber") is int m ? m : 10;
                    int minor = key?.GetValue("CurrentMinorVersionNumber") is int n ? n : 0;
                    if (build == null) return Environment.OSVersion.Version.ToString();
                    return Inv.Num(major) + "." + Inv.Num(minor) + "." + build + (ubr is int u ? "." + Inv.Num(u) : string.Empty);
                }
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return Environment.OSVersion.Version.ToString();
            }
        }
    }
}
