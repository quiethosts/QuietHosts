using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>
    /// The host-name rule. Hosts files match exact names only: no wildcards,
    /// ports, IP literals or single labels.
    /// </summary>
    public static class HostName
    {
        /// <summary>Longest valid name (DNS limit).</summary>
        public const int MaxLength = 253;

        // Matched ordinally (no IgnoreCase), after lowercasing. The last label starts with a letter,
        // so IPv4 literals never match, and at least one dot is required, so single labels never match.
        private static readonly Regex Shape = new Regex(
            @"^(?=.{4,253}$)(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z](?:[a-z0-9-]{0,61}[a-z0-9])$",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Normalizes a name: rejects raw characters outside 0x21-0x7E (before lowercasing, so the
        /// Kelvin sign or a dotted capital I can never become ASCII), then trims, lowercases with
        /// ToLowerInvariant, strips one trailing dot and checks the shape. Whitespace is rejected,
        /// so callers trim their fields first.
        /// </summary>
        public static bool TryNormalize(string? raw, out string host)
        {
            host = string.Empty;
            if (raw == null || raw.Length == 0 || raw.Length > MaxLength + 1) return false;
            foreach (char c in raw)
            {
                if (c < '!' || c > '~') return false;
            }
            string s = raw.Trim().ToLowerInvariant();
            if (s.Length > 0 && s[s.Length - 1] == '.') s = s.Substring(0, s.Length - 1);
            if (!Shape.IsMatch(s)) return false;
            host = s;
            return true;
        }

        /// <summary>The normalized name, or null if the name breaks the rule.</summary>
        public static string? Normalize(string? raw) => TryNormalize(raw, out string host) ? host : null;

        public static bool IsValid(string? raw) => TryNormalize(raw, out _);

        /// <summary>True if the text is a valid name that is already in normalized form.</summary>
        public static bool IsNormalized(string? text) =>
            text != null && TryNormalize(text, out string host) && string.Equals(host, text, System.StringComparison.Ordinal);
    }
}
