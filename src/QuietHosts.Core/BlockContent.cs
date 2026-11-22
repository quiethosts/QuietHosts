using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuietHosts.Core
{
    /// <summary>What QuietHosts' block should contain: the level, list version and sorted entries.</summary>
    public sealed class BlockContent
    {
        public BlockContent(Level level, string listVersion, IEnumerable<string> hosts, string appVersion)
        {
            if (hosts == null) throw new ArgumentNullException(nameof(hosts));
            Level = level;
            ListVersion = Clean(listVersion, "unknown");
            AppVersion = Clean(appVersion, "0.0.0");
            var sorted = hosts.Distinct(StringComparer.Ordinal).ToList();
            sorted.Sort(StringComparer.Ordinal);
            Hosts = sorted;
        }

        public Level Level { get; }
        public string ListVersion { get; }
        public string AppVersion { get; }
        /// <summary>Unique entries, sorted ordinally.</summary>
        public IReadOnlyList<string> Hosts { get; }

        /// <summary>The header's sha256: lowercase hex of the entry lines joined by "\n".</summary>
        public string EntriesSha256 => Hashing.Sha256Hex(string.Join("\n", Hosts.Select(h => HostsDocument.EntryPrefix + h)));

        /// <summary>
        /// The block's lines without line endings. Throws if an entry is guarded or not a normalized
        /// host name: the guard is absolute, so this is a bug, never something to write.
        /// </summary>
        public IReadOnlyList<string> RenderLines(Guard guard)
        {
            if (guard == null) throw new ArgumentNullException(nameof(guard));
            foreach (string h in Hosts)
            {
                if (!HostName.IsNormalized(h)) throw new ArgumentException("Not a normalized address: " + Guard.Shorten(h));
                if (guard.IsGuarded(h)) throw new InvalidOperationException("Refusing to write a guarded address: " + h);
            }
            var lines = new List<string>(Hosts.Count + 4)
            {
                HostsDocument.BeginMarker,
                HostsDocument.ManagedByLinePrefix + " " + AppInfo.RepositoryUrl,
                HeaderLine(),
            };
            foreach (string h in Hosts) lines.Add(HostsDocument.EntryPrefix + h);
            lines.Add(HostsDocument.EndMarker);
            foreach (string line in lines)
            {
                foreach (char c in line)
                {
                    if (c < ' ' || c > '~') throw new InvalidOperationException("The block must be printable ASCII.");
                }
            }
            return lines;
        }

        /// <summary>"# QuietHosts 1.0.0 | level=standard | lists=2026-11-20 | hosts=905 | sha256=..."</summary>
        public string HeaderLine() =>
            HostsDocument.HeaderPrefix + AppVersion +
            " | level=" + LevelNames.ToKeyword(Level) +
            " | lists=" + ListVersion +
            " | hosts=" + Inv.Num(Hosts.Count) +
            " | sha256=" + EntriesSha256;

        private static string Clean(string? text, string fallback)
        {
            var sb = new StringBuilder();
            foreach (char c in text ?? string.Empty)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '.' || c == '-' || c == '+';
                if (ok) sb.Append(c);
            }
            return sb.Length == 0 ? fallback : sb.ToString();
        }
    }

    /// <summary>The parsed header line of QuietHosts' block.</summary>
    public sealed class BlockHeader
    {
        private BlockHeader(string appVersion, Level level, string listVersion, int hostCount, string sha256)
        {
            AppVersion = appVersion;
            Level = level;
            ListVersion = listVersion;
            HostCount = hostCount;
            Sha256 = sha256;
        }

        /// <summary>The app version that wrote the block.</summary>
        public string AppVersion { get; }
        public Level Level { get; }
        /// <summary>The list version, "YYYY-MM-DD".</summary>
        public string ListVersion { get; }
        /// <summary>The number of entries the block was written with.</summary>
        public int HostCount { get; }
        public string Sha256 { get; }

        /// <summary>The list date, or null if the version isn't a date.</summary>
        public DateTime? ListDate =>
            DateTime.TryParseExact(ListVersion, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime d) ? d : (DateTime?)null;

        /// <summary>Parses a header line; null if it is damaged or incomplete.</summary>
        public static BlockHeader? TryParse(string line)
        {
            if (line == null || !line.StartsWith(HostsDocument.HeaderPrefix, StringComparison.Ordinal)) return null;
            string[] parts = line.Substring(HostsDocument.HeaderPrefix.Length).Split(new[] { " | " }, StringSplitOptions.None);
            string app = parts[0].Trim();
            if (app.Length == 0 || app.IndexOf(' ') >= 0) return null;
            string? level = null, lists = null, hosts = null, sha = null;
            for (int i = 1; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                int eq = p.IndexOf('=');
                if (eq <= 0) return null;
                string key = p.Substring(0, eq);
                string value = p.Substring(eq + 1);
                switch (key)
                {
                    case "level": level = value; break;
                    case "lists": lists = value; break;
                    case "hosts": hosts = value; break;
                    case "sha256": sha = value; break;
                }
            }
            if (!LevelNames.TryParse(level, out Level lv)) return null;
            if (lists == null || lists.Length == 0) return null;
            if (hosts == null || hosts.Length == 0 || hosts.Length > 6) return null;
            int count = 0;
            foreach (char c in hosts)
            {
                if (c < '0' || c > '9') return null;
                count = count * 10 + (c - '0');
            }
            if (!Hashing.IsSha256Hex(sha)) return null;
            return new BlockHeader(app, lv, lists, count, sha!);
        }
    }
}
