using System.Linq;
using System.Text;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    public class ListParserTests
    {
        private static ParsedList ParseDefault(string text, Guard? guard = null) =>
            ListParser.Parse(Encoding.UTF8.GetBytes(text), "default.txt", Tier.Default, guard ?? Guard.BuiltIn);

        private static ParsedList ParseStrict(string text) =>
            ListParser.Parse(Encoding.UTF8.GetBytes(text), "strict.txt", Tier.Strict, Guard.BuiltIn);

        [Fact]
        public void Parses_the_documented_format()
        {
            ParsedList p = ParseDefault(
                "# QuietHosts block list - MIT License - sources: CREDITS.md\n" +
                "# version: 2026-11-20\n" +
                "\n" +
                "[ad-server]\n" +
                "a.ads-one.net\n" +
                "b.ads-two.com | Two Ads\n" +
                "c.ads-three.org | Three | may break video\n" +
                "d.ads-four.io |  | note without company\n" +
                "[analytics]\n" +
                "  e.metrics-five.net   |   Five Inc.   \n");
            Assert.Empty(p.Issues);
            Assert.Equal("2026-11-20", p.Version);
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com", "c.ads-three.org", "d.ads-four.io", "e.metrics-five.net" }, p.Entries.Select(e => e.Host).ToArray());
            ListEntry c = p.Entries[2];
            Assert.Equal("ad-server", c.Category);
            Assert.Equal("Three", c.Company);
            Assert.Equal("may break video", c.Note);
            Assert.Equal(7, c.Line);
            Assert.Equal(Tier.Default, c.Tier);
            Assert.Equal("", p.Entries[3].Company);
            Assert.Equal("note without company", p.Entries[3].Note);
            Assert.Equal("analytics", p.Entries[4].Category);
            Assert.Equal("Five Inc.", p.Entries[4].Company);
            Assert.Equal("Analytics tracker", p.Entries[4].CategoryWords);
        }

        [Fact]
        public void Bad_lines_are_reported_with_line_numbers_and_skipped()
        {
            ParsedList p = ParseDefault(
                "# version: 2026-11-20\n" +          // 1
                "[ad-server]\n" +                    // 2
                "good.ads-one.net\n" +               // 3
                "not a host\n" +                     // 4
                "1.2.3.4\n" +                        // 5
                "x.ads.com | a | b | c\n" +          // 6
                "UPPER.ads-two.com\n" +              // 7
                "good.ads-one.net\n" +               // 8 duplicate
                "y.ads.com | " + new string('c', 61) + "\n" +           // 9
                "z.ads.com | ok | " + new string('n', 81) + "\n" +      // 10
                "w.ads.com | bad" + (char)0x0007 + "company\n" +        // 11
                "[Bad Key]\n" +                      // 12
                "v.ads.com | " + new string('c', 60) + " | " + new string('n', 80) + "\n"); // 13
            var byLine = p.Issues.ToDictionary(i => i.Line, i => i.Kind);
            Assert.Equal(ListIssueKind.InvalidHost, byLine[4]);
            Assert.Equal(ListIssueKind.InvalidHost, byLine[5]);
            Assert.Equal(ListIssueKind.TooManyFields, byLine[6]);
            Assert.Equal(ListIssueKind.InvalidHost, byLine[7]);
            Assert.Equal(ListIssueKind.Duplicate, byLine[8]);
            Assert.Equal(ListIssueKind.FieldTooLong, byLine[9]);
            Assert.Equal(ListIssueKind.FieldTooLong, byLine[10]);
            Assert.Equal(ListIssueKind.InvalidField, byLine[11]);
            Assert.Equal(ListIssueKind.InvalidCategoryLine, byLine[12]);
            Assert.Equal(9, p.Issues.Count);
            Assert.All(p.Issues, i => Assert.True(i.IsError));
            Assert.Equal(new[] { "good.ads-one.net", "v.ads.com" }, p.Entries.Select(e => e.Host).ToArray());
            Assert.Equal("other", p.Entries[1].Category);
            Assert.Equal(10, p.HostLineCount);
        }

        [Fact]
        public void Unknown_categories_load_as_other_but_are_reported()
        {
            ParsedList p = ParseDefault("# version: 2026-11-20\n[adblock-tests]\na.ads-one.net\n");
            ListEntry e = Assert.Single(p.Entries);
            Assert.Equal("other", e.Category);
            Assert.Equal("Ads or tracking", e.CategoryWords);
            ListIssue issue = Assert.Single(p.Issues);
            Assert.Equal(ListIssueKind.UnknownCategory, issue.Kind);
            Assert.False(issue.IsError);
            Assert.Equal(2, issue.Line);
        }

        [Fact]
        public void Hosts_before_any_category_load_as_other()
        {
            ParsedList p = ParseDefault("# version: 2026-11-20\na.ads-one.net\n[ad-server]\nb.ads-two.com\n");
            Assert.Equal(new[] { "other", "ad-server" }, p.Entries.Select(e => e.Category).ToArray());
            Assert.Equal(ListIssueKind.NoCategory, Assert.Single(p.Issues).Kind);
        }

        [Fact]
        public void Version_line_is_required_and_must_be_a_date()
        {
            Assert.Equal(ListIssueKind.MissingVersion, Assert.Single(ParseDefault("[ad-server]\na.ads-one.net\n").Issues).Kind);
            Assert.Equal(ListIssueKind.InvalidVersion, Assert.Single(ParseDefault("# version: 2026-13-40\n").Issues).Kind);
            Assert.Equal(ListIssueKind.InvalidVersion, Assert.Single(ParseDefault("# version: 20261120\n").Issues).Kind);
            Assert.Equal(ListIssueKind.InvalidVersion, ParseDefault("# version: 2026-11-20\n# version: 2026-11-21\n").Issues.Single().Kind);
            Assert.Equal("2026-01-02", ParseDefault("#version:2026-01-02\n").Version);
        }

        [Fact]
        public void Strict_hosts_need_a_note()
        {
            ParsedList p = ParseStrict("# version: 2026-11-20\n[ad-server]\na.ads-one.net | One\nb.ads-two.com | Two | may break video\n");
            Assert.Equal(2, p.Entries.Count);
            ListIssue issue = Assert.Single(p.Issues);
            Assert.Equal(ListIssueKind.MissingNote, issue.Kind);
            Assert.Equal(3, issue.Line);
            Assert.All(p.Entries, e => Assert.Equal(Tier.Strict, e.Tier));
        }

        [Fact]
        public void Guarded_names_are_dropped_and_counted_not_reported()
        {
            ParsedList p = ParseDefault("# version: 2026-11-20\n[ad-server]\nib.adnxs.com\na.ads-one.net\nprinter.local\n");
            Assert.Empty(p.Issues);
            Assert.Equal("a.ads-one.net", Assert.Single(p.Entries).Host);
            Assert.Equal(new[] { "ib.adnxs.com", "printer.local" }, p.GuardedDropped.ToArray());
        }

        [Fact]
        public void File_level_problems()
        {
            Assert.Equal(ListIssueKind.MissingFile, Assert.Single(ListParser.Parse((byte[]?)null, "default.txt", Tier.Default, Guard.BuiltIn).Issues).Kind);
            ParsedList big = ListParser.Parse(new byte[Limits.MaxListFileBytes + 1], "default.txt", Tier.Default, Guard.BuiltIn);
            Assert.Equal(ListIssueKind.FileTooLarge, Assert.Single(big.Issues).Kind);
            ParsedList bad = ListParser.Parse(new byte[] { 0x61, 0xC3, 0x28 }, "default.txt", Tier.Default, Guard.BuiltIn);
            Assert.Equal(ListIssueKind.InvalidUtf8, Assert.Single(bad.Issues).Kind);

            byte[] bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# version: 2026-11-20\n[ad-server]\na.ads-one.net\n")).ToArray();
            ParsedList withBom = ListParser.Parse(bom, "default.txt", Tier.Default, Guard.BuiltIn);
            Assert.Equal(ListIssueKind.ByteOrderMark, Assert.Single(withBom.Issues).Kind);
            Assert.Single(withBom.Entries);

            ParsedList crlf = ParseDefault("# version: 2026-11-20\r\n[ad-server]\r\na.ads-one.net | One\r\n");
            Assert.Equal(ListIssueKind.CarriageReturn, Assert.Single(crlf.Issues).Kind);
            Assert.Equal("One", Assert.Single(crlf.Entries).Company);
        }

        [Fact]
        public void Duplicates_across_tiers_keep_the_default_entry()
        {
            BlockLists lists = BlockLists.Parse(
                "# version: 2026-11-20\n[ad-server]\na.ads-one.net\n",
                "# version: 2026-11-20\n[ad-server]\na.ads-one.net | x | strict note\nb.ads-two.com | y | strict note\n",
                Guard.BuiltIn);
            Assert.Equal(1, lists.StandardCount);
            Assert.Equal(1, lists.StrictExtraCount);
            Assert.Equal(2, lists.StrictTotalCount);
            Assert.Equal(Tier.Default, lists.Find("a.ads-one.net")!.Tier);
            ListIssue dup = Assert.Single(lists.Issues);
            Assert.Equal(ListIssueKind.Duplicate, dup.Kind);
            Assert.Equal("strict.txt", dup.File);
            Assert.Equal(3, dup.Line);
        }

        [Fact]
        public void Different_versions_are_reported()
        {
            BlockLists lists = BlockLists.Parse("# version: 2026-11-20\n", "# version: 2026-11-21\n", Guard.BuiltIn);
            Assert.Equal(ListIssueKind.VersionMismatch, Assert.Single(lists.Issues).Kind);
            Assert.Equal("2026-11-20", lists.Version);
        }

        [Fact]
        public void More_than_3000_hosts_are_capped()
        {
            var sb = new StringBuilder("# version: 2026-11-20\n[ad-server]\n");
            for (int i = 0; i < 2000; i++) sb.Append("h").Append(i).Append(".ads-one.net\n");
            var sb2 = new StringBuilder("# version: 2026-11-20\n[ad-server]\n");
            for (int i = 0; i < 1001; i++) sb2.Append("s").Append(i).Append(".ads-two.net | x | n\n");
            BlockLists lists = BlockLists.Parse(sb.ToString(), sb2.ToString(), Guard.BuiltIn);
            Assert.Equal(3000, lists.StrictTotalCount);
            Assert.Equal(ListIssueKind.TooManyHosts, Assert.Single(lists.Issues).Kind);
        }

        [Fact]
        public void Lists_expose_counts_date_and_lookup()
        {
            BlockLists lists = Infrastructure.Fixtures.SmallLists();
            Assert.Empty(lists.Issues);
            Assert.Equal(3, lists.StandardCount);
            Assert.Equal(2, lists.StrictExtraCount);
            Assert.Equal(3, lists.CountFor(Level.Standard));
            Assert.Equal(5, lists.CountFor(Level.Strict));
            Assert.Equal(new System.DateTime(2026, 11, 20), lists.Date);
            Assert.True(lists.Contains("pixel.four-ads.org"));
            Assert.False(lists.Contains("PIXEL.four-ads.org"));
            Assert.Equal("pixel.four-ads.org", lists.Find("PIXEL.four-ads.org.")!.Host);
            Assert.Null(lists.Find("nope.example.com"));
            Assert.Equal(3, lists.ForLevel(Level.Standard).Count());
            Assert.Equal(5, lists.ForLevel(Level.Strict).Count());
        }

        [Fact]
        public void Category_table_has_words_for_every_key()
        {
            Assert.Equal(22, Categories.Keys.Count);
            Assert.All(Categories.Keys, k => Assert.False(string.IsNullOrEmpty(Categories.Words(k))));
            Assert.Equal("Ad auction", Categories.Words("header-bidding"));
            Assert.Equal("\"Around the web\" ad box", Categories.Words("native-ads"));
            Assert.Equal("Ads or tracking", Categories.Words("other"));
            Assert.Equal("Ads or tracking", Categories.Words("adblock-tests"));
            Assert.Equal("Ads or tracking", Categories.Words(null));
            Assert.Equal(Categories.Keys.Count, Categories.Keys.Distinct().Count());
        }
    }
}
