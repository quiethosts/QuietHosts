using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    /// <summary>
    /// Lint over the lists EMBEDDED in the build (the list format in CONTRIBUTING.md). If one of these fails,
    /// the message names the rule and the lines.
    /// </summary>
    public class ShippedListLintTests
    {
        private static readonly Lazy<BlockLists> Lists = new Lazy<BlockLists>(BlockLists.LoadEmbedded);
        private const string ListHeader = "# QuietHosts block list - MIT License - sources: CREDITS.md";
        private const string GuardHeader = "# QuietHosts guard list - MIT License";

        /// <summary>The embedded file's lines, without the empty piece after the final line break.</summary>
        private static string[] EmbeddedLines(string name, out bool finalLineBreak)
        {
            using (Stream? s = typeof(AppInfo).Assembly.GetManifestResourceStream("QuietHosts.Lists." + name))
            {
                Assert.NotNull(s);
                using (var reader = new StreamReader(s!, new System.Text.UTF8Encoding(false, true)))
                {
                    string text = reader.ReadToEnd();
                    finalLineBreak = text.EndsWith("\n", StringComparison.Ordinal);
                    string[] lines = text.Split('\n');
                    return finalLineBreak ? lines.Take(lines.Length - 1).ToArray() : lines;
                }
            }
        }

        private static void AssertNone(string rule, IEnumerable<string> problems)
        {
            var found = problems.ToList();
            Assert.True(found.Count == 0, rule + " (" + found.Count + " problems):" + Environment.NewLine + string.Join(Environment.NewLine, found.Take(40)));
        }

        private static string Describe(IEnumerable<ListIssue> issues) =>
            string.Join(Environment.NewLine, issues.Take(40).Select(i => i.ToString()));

        private static void AssertNoIssues(string rule, Func<ListIssue, bool> filter)
        {
            var found = Lists.Value.Issues.Where(filter).ToList();
            Assert.True(found.Count == 0, rule + " (" + found.Count + " problems):" + Environment.NewLine + Describe(found));
        }

        internal static string RepoRoot()
        {
            // The test runner may shadow-copy the assembly, so try its original location (CodeBase) too.
            var starts = new List<string?>
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetDirectoryName(new Uri(typeof(ShippedListLintTests).Assembly.CodeBase).LocalPath),
                Path.GetDirectoryName(typeof(ShippedListLintTests).Assembly.Location),
                Directory.GetCurrentDirectory(),
            };
            foreach (string? start in starts)
            {
                string? dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (File.Exists(Path.Combine(dir, "QuietHosts.slnx"))) return dir!;
                    dir = Path.GetDirectoryName(dir);
                }
            }
            throw new InvalidOperationException("QuietHosts.slnx not found above the test assembly.");
        }

        [Fact]
        public void Embedded_lists_exist()
        {
            var names = typeof(AppInfo).Assembly.GetManifestResourceNames();
            Assert.Contains("QuietHosts.Lists.default.txt", names);
            Assert.Contains("QuietHosts.Lists.strict.txt", names);
            Assert.Contains("QuietHosts.Lists.guard.txt", names);
        }

        [Fact]
        public void Lists_parse_with_zero_errors() =>
            AssertNoIssues("Rule: every line parses (no errors)", i => i.IsError);

        [Fact]
        public void All_hosts_are_valid_and_lowercase() =>
            AssertNoIssues("Rule: every address passes the host-name rule in normalized form", i => i.Kind == ListIssueKind.InvalidHost);

        [Fact]
        public void Category_keys_are_all_known() =>
            AssertNoIssues("Rule: every [category] key is one of the keys in CONTRIBUTING.md's table, and every address is under one",
                i => i.Kind == ListIssueKind.UnknownCategory || i.Kind == ListIssueKind.NoCategory || i.Kind == ListIssueKind.InvalidCategoryLine);

        [Fact]
        public void Strict_hosts_have_notes() =>
            AssertNoIssues("Rule: every Strict address has a note", i => i.Kind == ListIssueKind.MissingNote);

        [Fact]
        public void No_duplicates_within_or_across_tiers() =>
            AssertNoIssues("Rule: no address is listed twice", i => i.Kind == ListIssueKind.Duplicate);

        [Fact]
        public void Files_are_utf8_without_bom_with_lf_line_endings() =>
            AssertNoIssues("Rule: UTF-8 without BOM, LF line endings",
                i => i.Kind == ListIssueKind.ByteOrderMark || i.Kind == ListIssueKind.CarriageReturn || i.Kind == ListIssueKind.InvalidUtf8);

        [Fact]
        public void Both_lists_have_the_same_version()
        {
            BlockLists lists = Lists.Value;
            Assert.NotNull(lists.DefaultFile.Version);
            Assert.NotNull(lists.StrictFile.Version);
            Assert.Equal(lists.DefaultFile.Version, lists.StrictFile.Version);
            Assert.NotNull(lists.Date);
        }

        [Fact]
        public void No_list_host_hits_the_guard()
        {
            var hits = Lists.Value.GuardedDropped;
            Assert.True(hits.Count == 0, "Rule: no address is guarded. Guarded: " + string.Join(", ", hits));
        }

        [Fact]
        public void No_list_host_is_in_the_reviewed_drop_file()
        {
            string path = Path.Combine(RepoRoot(), "data", "reviewed-drop.txt");
            Assert.True(File.Exists(path), "data/reviewed-drop.txt is missing");
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                string host = line.Split('|')[0].Trim();
                if (HostName.TryNormalize(host, out string h)) dropped.Add(h);
            }
            Assert.NotEmpty(dropped);
            var hits = Lists.Value.Entries.Where(e => dropped.Contains(e.Host)).Select(e => e.Host + " (" + e.Tier + ")").ToList();
            Assert.True(hits.Count == 0, "Rule: no address from data/reviewed-drop.txt is listed. Found: " + string.Join(", ", hits));
        }

        [Fact]
        public void Totals_are_within_limits()
        {
            BlockLists lists = Lists.Value;
            int lines = lists.DefaultFile.HostLineCount + lists.StrictFile.HostLineCount;
            Assert.True(lines <= Limits.MaxListHosts, "Rule: at most 3,000 addresses in total; found " + lines);
            Assert.True(lists.StandardCount > 0, "the Standard list is empty");
            Assert.True(lists.StrictExtraCount > 0, "the Strict list is empty");
            foreach (string name in new[] { "default.txt", "strict.txt", "guard.txt" })
            {
                using (Stream? s = typeof(AppInfo).Assembly.GetManifestResourceStream("QuietHosts.Lists." + name))
                {
                    Assert.NotNull(s);
                    Assert.True(s!.Length <= Limits.MaxListFileBytes, name + " is over 256 KB");
                }
            }
        }

        [Fact]
        public void Field_limits_hold()
        {
            Assert.All(Lists.Value.Entries, e =>
            {
                Assert.True(e.Company.Length <= Limits.MaxCompanyLength, e.Host);
                Assert.True(e.Note.Length <= Limits.MaxNoteLength, e.Host);
            });
            AssertNoIssues("Rule: company at most 60 and note at most 80 characters, no control characters",
                i => i.Kind == ListIssueKind.FieldTooLong || i.Kind == ListIssueKind.InvalidField || i.Kind == ListIssueKind.TooManyFields);
        }

        [Fact]
        public void Known_drops_stay_dropped()
        {
            Assert.Null(Lists.Value.Find("ib.adnxs.com"));
            Assert.Null(Lists.Value.Find("t.oa.opera.com"));
        }

        [Fact]
        public void Ad_Shield_hosts_are_never_in_Standard()
        {
            // Blocking these makes Yahoo show "Oops, something went wrong", so they may only be in Strict, with that note.
            string[] adShield =
            {
                "goshsmarten.com", "uncoverpilot.com", "debutfiddle.com", "athwartwhoafat.com", "4.athwartwhoafat.com",
                "maturequart.com", "0.maturequart.com", "2.maturequart.com", "4.maturequart.com",
            };
            var hits = Lists.Value.DefaultEntries.Where(e => adShield.Contains(e.Host)).Select(e => e.Host).ToList();
            Assert.True(hits.Count == 0, "Rule: the Ad-Shield addresses are never in Standard. Found: " + string.Join(", ", hits));
        }

        [Fact]
        public void Company_and_note_are_printable_ascii()
        {
            bool Ascii(string s) => s.All(c => c >= ' ' && c <= '~');
            AssertNone("Rule: write the company and note in plain ASCII (no curly quotes, dashes or accents)",
                Lists.Value.Entries.Where(e => !Ascii(e.Company) || !Ascii(e.Note)).Select(e => e.Host + " (" + e.Tier + ")"));
        }

        [Theory]
        [InlineData("default.txt")]
        [InlineData("strict.txt")]
        [InlineData("guard.txt")]
        public void Files_start_with_their_header_and_have_no_tabs_or_trailing_spaces(string name)
        {
            string[] lines = EmbeddedLines(name, out bool finalLineBreak);
            Assert.True(finalLineBreak, name + " must end with a line break");
            Assert.True(lines.Length > 0 && lines[0] == (name == "guard.txt" ? GuardHeader : ListHeader),
                name + ": the first line must be '" + (name == "guard.txt" ? GuardHeader : ListHeader) + "'");
            AssertNone("Rule: no tabs or trailing spaces in " + name,
                lines.Select((l, i) => new { l, n = i + 1 })
                     .Where(x => x.l.IndexOf('\t') >= 0 || x.l.TrimEnd().Length != x.l.Length)
                     .Select(x => name + ":" + x.n));
        }

        [Theory]
        [InlineData("default.txt")]
        [InlineData("strict.txt")]
        public void Entry_lines_use_the_exact_layout(string name)
        {
            string[] lines = EmbeddedLines(name, out _);
            var problems = new List<string>();
            var categories = new HashSet<string>(StringComparer.Ordinal);
            int versions = 0;
            bool inCategories = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string where = name + ":" + (i + 1) + ": ";
                string line = lines[i];
                string t = line.Trim();
                if (t.Length == 0) continue;
                if (t[0] == '#')
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^#\s*version\s*:", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    {
                        versions++;
                        if (!line.StartsWith("# version: ", StringComparison.Ordinal)) problems.Add(where + "write the version line as '# version: YYYY-MM-DD'");
                        if (inCategories) problems.Add(where + "the version line belongs in the header, before the first [category]");
                    }
                    continue;
                }
                if (t[0] == '[')
                {
                    inCategories = true;
                    if (!categories.Add(t)) problems.Add(where + t + " appears twice; keep one block per category");
                    continue;
                }
                string[] parts = line.Split('|');
                string host = parts[0].Trim();
                string company = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                string note = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                string expected = parts.Length == 3 ? host + " | " + company + " | " + note
                    : parts.Length == 2 ? host + " | " + company
                    : host;
                if (line != expected) problems.Add(where + "write 'address | company | note' with single spaces around each |");
                if (parts.Length == 2 && company.Length == 0) problems.Add(where + "an empty company field: write just the address");
                if (parts.Length == 3 && note.Length == 0) problems.Add(where + "an empty note field: leave the note out");
            }
            if (versions != 1) problems.Add(name + ": expected exactly one '# version:' line, found " + versions);
            AssertNone("Rule: the list format in CONTRIBUTING.md", problems);
        }
    }
}
