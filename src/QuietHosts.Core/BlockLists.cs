using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace QuietHosts.Core
{
    /// <summary>
    /// The block lists the app ships, parsed and checked. Every count shown anywhere comes
    /// from here. Guarded names are dropped (and counted); duplicates across tiers keep the Default one.
    /// </summary>
    public sealed class BlockLists
    {
        private readonly Dictionary<string, ListEntry> _byHost;

        private BlockLists(Guard guard, ParsedList defaults, ParsedList stricts, List<ListEntry> defaultEntries,
            List<ListEntry> strictEntries, List<ListIssue> issues)
        {
            Guard = guard;
            DefaultFile = defaults;
            StrictFile = stricts;
            DefaultEntries = defaultEntries;
            StrictEntries = strictEntries;
            Issues = issues;
            Entries = defaultEntries.Concat(strictEntries).ToList();
            _byHost = new Dictionary<string, ListEntry>(StringComparer.Ordinal);
            foreach (var e in Entries) _byHost[e.Host] = e;
            GuardedDropped = defaults.GuardedDropped.Concat(stricts.GuardedDropped).ToList();
            Version = defaults.Version ?? stricts.Version ?? string.Empty;
            if (Version.Length > 0 && DateTime.TryParseExact(Version, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                Date = date;
        }

        /// <summary>The guard that filtered these lists (built-in plus guard.txt).</summary>
        public Guard Guard { get; }
        public ParsedList DefaultFile { get; }
        public ParsedList StrictFile { get; }

        /// <summary>The list version, "YYYY-MM-DD" (the same in both lists; "" if missing).</summary>
        public string Version { get; }
        /// <summary>The list date (Kind Unspecified), or null if the version is missing. Format it with InvariantCulture.</summary>
        public DateTime? Date { get; }

        /// <summary>Tier Default: what Standard blocks.</summary>
        public IReadOnlyList<ListEntry> DefaultEntries { get; }
        /// <summary>Tier Strict: what Strict blocks on top of Default.</summary>
        public IReadOnlyList<ListEntry> StrictEntries { get; }
        /// <summary>All entries: Default first, then Strict, each in file order.</summary>
        public IReadOnlyList<ListEntry> Entries { get; }

        /// <summary>{n} addresses in Standard.</summary>
        public int StandardCount => DefaultEntries.Count;
        /// <summary>{m} more in Strict.</summary>
        public int StrictExtraCount => StrictEntries.Count;
        /// <summary>Everything Strict blocks (Default plus Strict).</summary>
        public int StrictTotalCount => DefaultEntries.Count + StrictEntries.Count;

        /// <summary>How many addresses a level's tiers hold (before allowlist and outside mappings).</summary>
        public int CountFor(Level level) => level == Level.Strict ? StrictTotalCount : StandardCount;

        /// <summary>Valid names that the guard dropped while loading.</summary>
        public IReadOnlyList<string> GuardedDropped { get; }
        public int GuardedDroppedCount => GuardedDropped.Count;

        /// <summary>Every problem found in default.txt, strict.txt and guard.txt.</summary>
        public IReadOnlyList<ListIssue> Issues { get; }
        public IEnumerable<ListIssue> Errors => Issues.Where(i => i.IsError);

        public bool Contains(string? host) => host != null && _byHost.ContainsKey(host);

        /// <summary>The entry for an address (normalized), or null.</summary>
        public ListEntry? Find(string? host)
        {
            if (host == null) return null;
            if (_byHost.TryGetValue(host, out ListEntry? entry)) return entry;
            string? normalized = HostName.Normalize(host);
            return normalized != null && _byHost.TryGetValue(normalized, out entry) ? entry : null;
        }

        /// <summary>The entries a level's tiers hold: Standard = Default; Strict = Default and Strict.</summary>
        public IEnumerable<ListEntry> ForLevel(Level level) => level == Level.Strict ? Entries : DefaultEntries;

        /// <summary>Loads the lists embedded in the exe (QuietHosts.Lists.default.txt, strict.txt, guard.txt).</summary>
        public static BlockLists LoadEmbedded()
        {
            Guard guard = Guard.LoadEmbedded(out IReadOnlyList<ListIssue> guardIssues);
            return Load(EmbeddedLists.Read(EmbeddedLists.DefaultName), EmbeddedLists.Read(EmbeddedLists.StrictName), guard, guardIssues);
        }

        /// <summary>Loads lists from bytes (tests and tools). Missing files load as empty lists with an issue.</summary>
        public static BlockLists Load(byte[]? defaultBytes, byte[]? strictBytes, Guard guard, IEnumerable<ListIssue>? guardIssues = null)
        {
            ParsedList d = ListParser.Parse(defaultBytes, EmbeddedLists.DefaultName, Tier.Default, guard);
            ParsedList s = ListParser.Parse(strictBytes, EmbeddedLists.StrictName, Tier.Strict, guard);
            return Combine(d, s, guard, guardIssues);
        }

        /// <summary>Loads lists from text (tests).</summary>
        public static BlockLists Parse(string defaultText, string strictText, Guard guard)
        {
            ParsedList d = ListParser.Parse(defaultText, EmbeddedLists.DefaultName, Tier.Default, guard);
            ParsedList s = ListParser.Parse(strictText, EmbeddedLists.StrictName, Tier.Strict, guard);
            return Combine(d, s, guard, null);
        }

        private static BlockLists Combine(ParsedList d, ParsedList s, Guard guard, IEnumerable<ListIssue>? guardIssues)
        {
            var issues = new List<ListIssue>();
            if (guardIssues != null) issues.AddRange(guardIssues);
            issues.AddRange(d.Issues);
            issues.AddRange(s.Issues);

            var defaults = new List<ListEntry>();
            var stricts = new List<ListEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int total = 0;
            foreach (var e in d.Entries)
            {
                if (!seen.Add(e.Host)) continue;
                if (total >= Limits.MaxListHosts)
                {
                    issues.Add(new ListIssue(d.FileName, e.Line, ListIssueKind.TooManyHosts, "The lists hold more than 3,000 addresses in total."));
                    continue;
                }
                defaults.Add(e);
                total++;
            }
            foreach (var e in s.Entries)
            {
                if (!seen.Add(e.Host))
                {
                    issues.Add(new ListIssue(s.FileName, e.Line, ListIssueKind.Duplicate, "Already in default.txt: " + e.Host));
                    continue;
                }
                if (total >= Limits.MaxListHosts)
                {
                    issues.Add(new ListIssue(s.FileName, e.Line, ListIssueKind.TooManyHosts, "The lists hold more than 3,000 addresses in total."));
                    continue;
                }
                stricts.Add(e);
                total++;
            }
            if (d.Version != null && s.Version != null && !string.Equals(d.Version, s.Version, StringComparison.Ordinal))
                issues.Add(new ListIssue(s.FileName, 0, ListIssueKind.VersionMismatch, "default.txt and strict.txt have different versions."));

            return new BlockLists(guard, d, s, defaults, stricts, issues);
        }
    }
}
