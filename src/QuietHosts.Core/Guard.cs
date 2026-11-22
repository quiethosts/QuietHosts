using System;
using System.Collections.Generic;
using System.Text;

namespace QuietHosts.Core
{
    /// <summary>Why a name is guarded.</summary>
    public enum GuardSource
    {
        None,
        /// <summary>The hardcoded Microsoft suffixes and TLDs.</summary>
        Microsoft,
        /// <summary>Invalid names, IP literals, single labels and reserved TLDs.</summary>
        Reserved,
        /// <summary>A suffix line of lists/guard.txt.</summary>
        GuardSuffix,
        /// <summary>An "=exact" line of lists/guard.txt.</summary>
        GuardExact,
    }

    /// <summary>
    /// Names QuietHosts never writes. A name is guarded if it equals a suffix, ends with
    /// "." plus a suffix, or equals an exact entry. Lists drop guarded names when they load; the
    /// renderer throws for them.
    /// </summary>
    public sealed class Guard
    {
        // Hardcoded, so no data file can weaken it.
        private static readonly string[] MicrosoftSuffixList =
        {
            "microsoft.com", "microsoftonline.com", "microsoftonline-p.com", "microsoftstore.com",
            "windows.com", "windows.net", "windowsupdate.com", "live.com", "live.net", "msn.com",
            "bing.com", "bing.net", "bingapis.com", "office.com", "office.net", "office365.com",
            "outlook.com", "hotmail.com", "skype.com", "xbox.com", "xboxlive.com", "xboxservices.com",
            "linkedin.com", "licdn.com", "github.com", "github.io", "githubusercontent.com",
            "githubassets.com", "npmjs.com", "nuget.org", "azure.com", "azure.net", "azureedge.net",
            "azurefd.net", "azurewebsites.net", "cloudapp.net", "trafficmanager.net", "msedge.net",
            "msecnd.net", "msauth.net", "msftauth.net", "msftconnecttest.com", "msftncsi.com",
            "msocsp.com", "s-microsoft.com", "aka.ms", "clarity.ms", "gfx.ms", "sfx.ms", "adnxs.com",
            "sharepoint.com", "onedrive.com", "1drv.com", "visualstudio.com", "minecraft.net",
            "mojang.com", "activision.com", "blizzard.com", "battle.net", "king.com",
        };

        private static readonly string[] MicrosoftTldList = { "microsoft", "azure", "bing", "office", "windows", "xbox" };

        // Only for the MicrosoftLinesOutside warning, never for what QuietHosts writes: Microsoft's own service names,
        // which Windows Defender's HostsFileHijack detection is reported to check. Every entry is covered by the
        // Microsoft guard above. Left out on purpose: names other programs' ad and phishing lists block for good
        // reasons (adnxs.com, clarity.ms, LinkedIn, GitHub, npm, NuGet, the game companies, Azure customer hosting such
        // as azurewebsites.net, cloudapp.net and blob.core.windows.net), so their lines don't raise a false alarm.
        // Of windows.net only login.windows.net (sign-in) is here; the rest is customers' storage and sites.
        private static readonly string[] DefenderSuffixList =
        {
            "microsoft.com", "microsoftonline.com", "microsoftonline-p.com", "microsoftstore.com",
            "windows.com", "windowsupdate.com", "login.windows.net", "live.com", "live.net", "msn.com",
            "bing.com", "bing.net", "bingapis.com", "office.com", "office.net", "office365.com",
            "outlook.com", "hotmail.com", "skype.com", "xbox.com", "xboxlive.com", "xboxservices.com",
            "msedge.net", "msauth.net", "msftauth.net", "msftconnecttest.com", "msftncsi.com", "msocsp.com",
            "s-microsoft.com", "aka.ms", "gfx.ms", "sfx.ms", "onedrive.com", "1drv.com",
        };

        private static readonly string[] ReservedTldList =
        {
            "localhost", "local", "localdomain", "internal", "arpa", "invalid", "test", "example", "onion", "lan", "home",
        };

        private static readonly HashSet<string> MicrosoftSet = BuildSet(MicrosoftSuffixList, MicrosoftTldList);
        private static readonly HashSet<string> DefenderSet = BuildSet(DefenderSuffixList, MicrosoftTldList);
        private static readonly HashSet<string> ReservedSet = BuildSet(ReservedTldList);

        private readonly HashSet<string> _suffixes;
        private readonly HashSet<string> _exact;

        private Guard(HashSet<string> suffixes, HashSet<string> exact)
        {
            _suffixes = suffixes;
            _exact = exact;
        }

        /// <summary>The Microsoft suffixes (domains), hardcoded.</summary>
        public static IReadOnlyList<string> MicrosoftSuffixes => MicrosoftSuffixList;
        /// <summary>The Microsoft TLDs, hardcoded.</summary>
        public static IReadOnlyList<string> MicrosoftTlds => MicrosoftTldList;
        /// <summary>The suffixes behind the MicrosoftLinesOutside warning (with the Microsoft TLDs); a subset of the Microsoft guard.</summary>
        public static IReadOnlyList<string> DefenderSuffixes => DefenderSuffixList;
        /// <summary>The reserved TLDs.</summary>
        public static IReadOnlyList<string> ReservedTlds => ReservedTldList;

        /// <summary>Only the hardcoded Microsoft and reserved names, without guard.txt.</summary>
        public static Guard BuiltIn { get; } = new Guard(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

        /// <summary>Suffix lines read from guard.txt.</summary>
        public IReadOnlyCollection<string> ListSuffixes => _suffixes;
        /// <summary>"=exact" lines read from guard.txt (without the "=").</summary>
        public IReadOnlyCollection<string> ListExactNames => _exact;

        /// <summary>The built-in guard plus the embedded lists/guard.txt.</summary>
        public static Guard LoadEmbedded() => LoadEmbedded(out _);

        public static Guard LoadEmbedded(out IReadOnlyList<ListIssue> issues)
        {
            byte[]? bytes = EmbeddedLists.Read(EmbeddedLists.GuardName);
            if (bytes == null)
            {
                issues = new[] { new ListIssue(EmbeddedLists.GuardName, 0, ListIssueKind.MissingFile, "The embedded guard list is missing.") };
                return BuiltIn;
            }
            return Parse(bytes, out issues);
        }

        /// <summary>The built-in guard plus guard.txt bytes (UTF-8): "suffix" lines, "=exact.name" lines and "#" comments.</summary>
        public static Guard Parse(byte[] guardBytes, out IReadOnlyList<ListIssue> issues)
        {
            var found = new List<ListIssue>();
            string text;
            if (guardBytes.Length > Limits.MaxListFileBytes)
            {
                found.Add(new ListIssue(EmbeddedLists.GuardName, 0, ListIssueKind.FileTooLarge, "The guard list is over 256 KB."));
                issues = found;
                return BuiltIn;
            }
            try
            {
                text = new UTF8Encoding(false, true).GetString(guardBytes);
            }
            catch (DecoderFallbackException)
            {
                found.Add(new ListIssue(EmbeddedLists.GuardName, 0, ListIssueKind.InvalidUtf8, "The guard list is not valid UTF-8."));
                issues = found;
                return BuiltIn;
            }
            var guard = ParseText(text, found);
            issues = found;
            return guard;
        }

        public static Guard Parse(string guardText, out IReadOnlyList<ListIssue> issues)
        {
            var found = new List<ListIssue>();
            var guard = ParseText(guardText, found);
            issues = found;
            return guard;
        }

        private static Guard ParseText(string text, List<ListIssue> issues)
        {
            var suffixes = new HashSet<string>(StringComparer.Ordinal);
            var exact = new HashSet<string>(StringComparer.Ordinal);
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                bool isExact = line[0] == '=';
                string name = isExact ? line.Substring(1).Trim() : line;
                if (!HostName.TryNormalize(name, out string host))
                {
                    issues.Add(new ListIssue(EmbeddedLists.GuardName, i + 1, ListIssueKind.InvalidHost, "Not a valid address: " + Shorten(name)));
                    continue;
                }
                (isExact ? exact : suffixes).Add(host);
            }
            return new Guard(suffixes, exact);
        }

        /// <summary>True if QuietHosts must never write this name. Names that break the host-name rule are guarded too.</summary>
        public bool IsGuarded(string? name) => Classify(name) != GuardSource.None;

        /// <summary>Which guard source covers the name, or None.</summary>
        public GuardSource Classify(string? name)
        {
            if (!HostName.TryNormalize(name, out string host)) return GuardSource.Reserved;
            if (MatchesSuffix(host, MicrosoftSet)) return GuardSource.Microsoft;
            if (MatchesSuffix(host, ReservedSet)) return GuardSource.Reserved;
            if (_exact.Contains(host)) return GuardSource.GuardExact;
            if (MatchesSuffix(host, _suffixes)) return GuardSource.GuardSuffix;
            return GuardSource.None;
        }

        /// <summary>
        /// True if the name is covered by the hardcoded Microsoft guard. Accepts names from other
        /// programs' lines leniently (ASCII case, one trailing dot); names with other characters are not.
        /// </summary>
        public static bool IsMicrosoft(string? name)
        {
            string? n = LenientName(name);
            return n != null && MatchesSuffix(n, MicrosoftSet);
        }

        /// <summary>
        /// True if another program's line for this name may make Windows Defender reset the hosts file: one of
        /// Microsoft's own service names (<see cref="DefenderSuffixes"/> and the Microsoft TLDs). Only the
        /// MicrosoftLinesOutside warning uses it; what QuietHosts never writes is <see cref="IsGuarded"/>. Lenient like
        /// <see cref="IsMicrosoft"/>.
        /// </summary>
        public static bool IsDefenderSensitive(string? name)
        {
            string? n = LenientName(name);
            return n != null && MatchesSuffix(n, DefenderSet);
        }

        /// <summary>True for names that are reserved: invalid names, IP literals, single labels, reserved TLDs.</summary>
        public static bool IsReserved(string? name)
        {
            if (!HostName.TryNormalize(name, out string host)) return true;
            return MatchesSuffix(host, ReservedSet);
        }

        /// <summary>ASCII-lowercased name without one trailing dot, or null if it has characters outside 0x21-0x7E.</summary>
        internal static string? LenientName(string? name)
        {
            if (name == null || name.Length == 0) return null;
            foreach (char c in name)
            {
                if (c < '!' || c > '~') return null;
            }
            string n = Inv.AsciiLower(name);
            if (n.Length > 1 && n[n.Length - 1] == '.') n = n.Substring(0, n.Length - 1);
            return n;
        }

        private static bool MatchesSuffix(string host, HashSet<string> set)
        {
            if (set.Count == 0) return false;
            if (set.Contains(host)) return true;
            int dot = host.IndexOf('.');
            while (dot >= 0 && dot < host.Length - 1)
            {
                if (set.Contains(host.Substring(dot + 1))) return true;
                dot = host.IndexOf('.', dot + 1);
            }
            return false;
        }

        private static HashSet<string> BuildSet(params string[][] parts)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in parts)
            {
                foreach (var s in part) set.Add(s);
            }
            return set;
        }

        internal static string Shorten(string text) => text.Length <= 80 ? text : text.Substring(0, 80) + "...";
    }
}
