using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>
    /// Backups next to the hosts file: hosts.quiethosts-original.bak (written once, never overwritten or
    /// pruned) and hosts.quiethosts-yyyyMMdd-HHmmss-fff.bak, named from UTC (the 10 newest are kept).
    /// block-ads.ps1's hosts.bak-* files are left alone.
    /// </summary>
    public static class Backups
    {
        public const string OriginalName = "hosts.quiethosts-original.bak";
        public const string SearchPattern = "hosts.quiethosts-*.bak";
        private static readonly Regex Timestamped = new Regex(@"^hosts\.quiethosts-[0-9]{8}-[0-9]{6}-[0-9]{3}\.bak$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsTimestampedName(string fileName) => Timestamped.IsMatch(fileName);

        /// <summary>The timestamped name for a time; the writer passes UTC.</summary>
        public static string NameFor(DateTime time) =>
            "hosts.quiethosts-" + time.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".bak";

        /// <summary>Timestamped backup names in the folder, newest first.</summary>
        public static List<string> ListTimestamped(IFileOps files, string folder)
        {
            var names = files.ListFiles(folder, SearchPattern).Where(IsTimestampedName).ToList();
            names.Sort((a, b) => string.Compare(b, a, StringComparison.OrdinalIgnoreCase));
            return names;
        }

        /// <summary>
        /// Deletes timestamped backups beyond the newest <paramref name="keep"/> and logs each one. The backup named
        /// <paramref name="keepName"/> (the current write's) is never deleted and counts as one of the kept, even if
        /// a clock that went back gave it an older name. Returns how many were deleted.
        /// </summary>
        public static int Prune(IFileOps files, string folder, int keep, List<string> log, string? keepName = null)
        {
            int deleted = 0;
            List<string> names = ListTimestamped(files, folder);
            bool protect = keepName != null && names.Exists(n => string.Equals(n, keepName, StringComparison.OrdinalIgnoreCase));
            int others = protect ? keep - 1 : keep;
            int seen = 0;
            foreach (string name in names)
            {
                if (protect && string.Equals(name, keepName, StringComparison.OrdinalIgnoreCase)) continue;
                if (seen++ < others) continue;
                try
                {
                    files.DeleteFile(Path.Combine(folder, name));
                    deleted++;
                    log.Add("backup: pruned " + name);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Add("prune failed: " + name + ": " + ex.GetType().Name);
                }
            }
            return deleted;
        }
    }

    /// <summary>The outcome of a write (before ElevatedMain adds the verb's registry changes).</summary>
    public sealed class WriteOutcome
    {
        internal WriteOutcome(ResultKind kind, bool written, RefusalReason refusal, string? backupPath, string? beforeSha, string? afterSha, Exception? error)
        {
            Kind = kind;
            Written = written;
            Refusal = refusal;
            BackupPath = backupPath;
            BeforeSha256 = beforeSha;
            AfterSha256 = afterSha;
            Error = error;
        }

        public ResultKind Kind { get; }
        /// <summary>True if the hosts file was changed and verified.</summary>
        public bool Written { get; }
        /// <summary>Why the write was refused (Kind = Refused).</summary>
        public RefusalReason Refusal { get; }
        /// <summary>The verified backup of the file before the write, if one was needed.</summary>
        public string? BackupPath { get; }
        public string? BeforeSha256 { get; }
        public string? AfterSha256 { get; }
        public Exception? Error { get; }
    }
}
