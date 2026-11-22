using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>One parsed list file.</summary>
    public sealed class ParsedList
    {
        internal ParsedList(string fileName, Tier tier, List<ListEntry> entries, string? version, List<ListIssue> issues, List<string> guarded, int hostLines)
        {
            FileName = fileName;
            Tier = tier;
            Entries = entries;
            Version = version;
            Issues = issues;
            GuardedDropped = guarded;
            HostLineCount = hostLines;
        }

        public string FileName { get; }
        public Tier Tier { get; }
        public IReadOnlyList<ListEntry> Entries { get; }
        /// <summary>The "# version: YYYY-MM-DD" value, or null if the line is missing or invalid.</summary>
        public string? Version { get; }
        public IReadOnlyList<ListIssue> Issues { get; }
        /// <summary>Valid addresses that were dropped because the guard covers them.</summary>
        public IReadOnlyList<string> GuardedDropped { get; }
        /// <summary>Every address line in the file, loaded or not.</summary>
        public int HostLineCount { get; }
    }

    /// <summary>
    /// Parses lists/default.txt and lists/strict.txt: UTF-8 without BOM, LF, "#" comments,
    /// "# version: YYYY-MM-DD", "[key]" categories and "host | company | note" lines.
    /// </summary>
    public static class ListParser
    {
        private static readonly Regex VersionLine = new Regex(@"^#\s*version\s*:\s*(.*?)\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex VersionValue = new Regex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex CategoryKey = new Regex(@"^[a-z0-9-]{1,40}$", RegexOptions.CultureInvariant);

        public static ParsedList Parse(byte[]? bytes, string fileName, Tier tier, Guard guard)
        {
            var issues = new List<ListIssue>();
            var entries = new List<ListEntry>();
            var guarded = new List<string>();
            if (bytes == null)
            {
                issues.Add(new ListIssue(fileName, 0, ListIssueKind.MissingFile, "The list file is missing."));
                return new ParsedList(fileName, tier, entries, null, issues, guarded, 0);
            }
            if (bytes.Length > Limits.MaxListFileBytes)
            {
                issues.Add(new ListIssue(fileName, 0, ListIssueKind.FileTooLarge, "The list file is over 256 KB."));
                return new ParsedList(fileName, tier, entries, null, issues, guarded, 0);
            }
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                issues.Add(new ListIssue(fileName, 0, ListIssueKind.ByteOrderMark, "The file starts with a byte order mark; save it as UTF-8 without BOM."));
                start = 3;
            }
            string text;
            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes, start, bytes.Length - start);
            }
            catch (DecoderFallbackException)
            {
                issues.Add(new ListIssue(fileName, 0, ListIssueKind.InvalidUtf8, "The list file is not valid UTF-8."));
                return new ParsedList(fileName, tier, entries, null, issues, guarded, 0);
            }
            return ParseText(text, fileName, tier, guard, issues);
        }

        public static ParsedList Parse(string text, string fileName, Tier tier, Guard guard) =>
            ParseText(text, fileName, tier, guard, new List<ListIssue>());

        private static ParsedList ParseText(string text, string fileName, Tier tier, Guard guard, List<ListIssue> issues)
        {
            var entries = new List<ListEntry>();
            var guarded = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? version = null;
            string? category = null;
            bool reportedCr = false;
            int hostLines = 0;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNo = i + 1;
                string raw = lines[i];
                if (raw.IndexOf('\r') >= 0 && !reportedCr)
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.CarriageReturn, "The file uses CRLF or CR line endings; use LF."));
                    reportedCr = true;
                }
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line[0] == '#')
                {
                    Match m = VersionLine.Match(line);
                    if (!m.Success) continue;
                    string value = m.Groups[1].Value;
                    if (!IsValidVersion(value))
                        issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidVersion, "The version must be a date written as YYYY-MM-DD."));
                    else if (version != null && !string.Equals(version, value, StringComparison.Ordinal))
                        issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidVersion, "The file has two different version lines."));
                    else
                        version = value;
                    continue;
                }

                if (line[0] == '[')
                {
                    string key = line.Length >= 2 && line[line.Length - 1] == ']' ? line.Substring(1, line.Length - 2).Trim() : string.Empty;
                    if (!CategoryKey.IsMatch(key))
                    {
                        issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidCategoryLine, "A category line must look like [ad-server]."));
                        category = Categories.Other;
                    }
                    else if (!Categories.IsKnown(key))
                    {
                        issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.UnknownCategory, "Unknown category [" + key + "]; it loads as [other]."));
                        category = Categories.Other;
                    }
                    else
                    {
                        category = key;
                    }
                    continue;
                }

                hostLines++;
                string[] parts = line.Split('|');
                if (parts.Length > 3)
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.TooManyFields, "At most three fields: address | company | note."));
                    continue;
                }
                string hostField = parts[0].Trim();
                string company = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                string note = parts.Length > 2 ? parts[2].Trim() : string.Empty;

                if (!HostName.TryNormalize(hostField, out string host))
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidHost, "Not a valid address: " + Guard.Shorten(hostField)));
                    continue;
                }
                if (!string.Equals(host, hostField, StringComparison.Ordinal))
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidHost, "Write the address in lowercase without a trailing dot: " + host));
                    continue;
                }
                if (HasBadChars(company) || HasBadChars(note))
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.InvalidField, "Company and note must not contain control characters."));
                    continue;
                }
                if (company.Length > Limits.MaxCompanyLength)
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.FieldTooLong, "The company is longer than 60 characters."));
                    continue;
                }
                if (note.Length > Limits.MaxNoteLength)
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.FieldTooLong, "The note is longer than 80 characters."));
                    continue;
                }
                if (!seen.Add(host))
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.Duplicate, "Listed twice in this file: " + host));
                    continue;
                }
                if (guard.IsGuarded(host))
                {
                    guarded.Add(host);
                    continue;
                }
                if (category == null)
                {
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.NoCategory, "The address comes before any [category] line; it loads as [other]."));
                    category = Categories.Other;
                }
                if (tier == Tier.Strict && note.Length == 0)
                    issues.Add(new ListIssue(fileName, lineNo, ListIssueKind.MissingNote, "Strict addresses need a note on what may stop working: " + host));

                entries.Add(new ListEntry(host, tier, category, company, note, lineNo));
            }

            if (version == null && !issues.Exists(x => x.Kind == ListIssueKind.InvalidVersion))
                issues.Add(new ListIssue(fileName, 0, ListIssueKind.MissingVersion, "The file has no \"# version: YYYY-MM-DD\" line."));

            return new ParsedList(fileName, tier, entries, version, issues, guarded, hostLines);
        }

        internal static bool IsValidVersion(string value) =>
            VersionValue.IsMatch(value) &&
            DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

        private static bool HasBadChars(string field)
        {
            foreach (char c in field)
            {
                if (char.IsControl(c)) return true;
                // Bidirectional overrides and isolates, zero-width marks, line separators and the BOM:
                // they could make a note display differently from what it says.
                int u = c;
                if ((u >= 0x200B && u <= 0x200F) || (u >= 0x202A && u <= 0x202E) ||
                    (u >= 0x2066 && u <= 0x2069) || u == 0x2028 || u == 0x2029 || u == 0xFEFF)
                    return true;
            }
            return false;
        }
    }
}
