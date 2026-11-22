using System;
using System.Collections.Generic;
using System.Linq;

namespace QuietHosts.Core
{
    /// <summary>The set of addresses a write puts in the block, and what was left out.</summary>
    public sealed class BlockPlan
    {
        internal BlockPlan(Level level, string listVersion, List<string> hosts, int allowed, int mappedOutside, int guarded)
        {
            Level = level;
            ListVersion = listVersion;
            Hosts = hosts;
            SkippedAllowed = allowed;
            SkippedMappedOutside = mappedOutside;
            SkippedGuarded = guarded;
        }

        public Level Level { get; }
        public string ListVersion { get; }
        /// <summary>The addresses to write, sorted ordinally.</summary>
        public IReadOnlyList<string> Hosts { get; }
        public int SkippedAllowed { get; }
        public int SkippedMappedOutside { get; }
        public int SkippedGuarded { get; }

        public BlockContent ToContent(string appVersion) => new BlockContent(Level, ListVersion, Hosts, appVersion);
    }

    /// <summary>
    /// The written set: the level's tiers (Standard = Default; Strict = Default and Strict),
    /// minus guarded names, allowlisted names and names mapped outside the managed blocks.
    /// </summary>
    public static class BlockPlanner
    {
        public static BlockPlan Plan(BlockLists lists, Level level, IEnumerable<string>? allowlist, IEnumerable<string>? mappedOutside)
        {
            if (lists == null) throw new ArgumentNullException(nameof(lists));
            var allowed = new HashSet<string>(allowlist ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var mapped = new HashSet<string>(mappedOutside ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var hosts = new List<string>();
            int skippedAllowed = 0, skippedMapped = 0, skippedGuarded = 0;
            foreach (ListEntry e in lists.ForLevel(level))
            {
                if (lists.Guard.IsGuarded(e.Host)) { skippedGuarded++; continue; }
                if (allowed.Contains(e.Host)) { skippedAllowed++; continue; }
                if (mapped.Contains(e.Host)) { skippedMapped++; continue; }
                hosts.Add(e.Host);
            }
            hosts.Sort(StringComparer.Ordinal);
            return new BlockPlan(level, lists.Version, hosts, skippedAllowed, skippedMapped, skippedGuarded);
        }
    }

    /// <summary>Allowlist rules: exact hosts from the lists, at most 2,000.</summary>
    public static class Allowlist
    {
        /// <summary>Normalizes, dedupes and sorts entries, keeping only hosts that are in the lists (when given).</summary>
        public static List<string> Prune(IEnumerable<string>? entries, BlockLists? lists)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string raw in entries ?? Enumerable.Empty<string>())
            {
                if (!HostName.TryNormalize(raw, out string host)) continue;
                if (lists != null && !lists.Contains(host)) continue;
                result.Add(host);
            }
            return result.ToList();
        }
    }
}
