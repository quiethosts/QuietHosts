using System.Collections.Generic;
using System.Linq;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    public class HostsDocumentTests
    {
        internal static byte[] B(string latin1) => Latin1.Encode(latin1);
        internal static string S(byte[] bytes) => Latin1.Decode(bytes);

        internal static string BlockText(BlockContent content, string eol = "\r\n") =>
            string.Join(eol, content.RenderLines(Guard.BuiltIn)) + eol;

        internal static byte[] Apply(byte[]? file, BlockContent? content = null) =>
            HostsDocument.Parse(file).Apply(content ?? Fixtures.Content(), Guard.BuiltIn);

        internal static byte[] Remove(byte[]? file) => HostsDocument.Parse(file).Remove();

        public static IEnumerable<object[]> Corpus() => Fixtures.Corpus();
        public static IEnumerable<object[]> NoFinalNewline() => Fixtures.NoFinalNewline();

        [Theory]
        [MemberData(nameof(Corpus))]
        public void Remove_of_apply_gives_back_the_same_bytes(string name, byte[] file)
        {
            byte[] applied = Apply(file);
            Assert.NotEqual(file, applied);
            Assert.True(Hashing.BytesEqual(file, Remove(applied)), name);
            Assert.True(Hashing.BytesEqual(file, Remove(file)), name + ": removing from a file without blocks changes nothing");
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void Applying_twice_writes_nothing_the_second_time(string name, byte[] file)
        {
            byte[] once = Apply(file);
            byte[] twice = Apply(once);
            Assert.True(Hashing.BytesEqual(once, twice), name);
            Assert.True(HostsDocument.Parse(once).IsCurrent(Fixtures.Content(), Guard.BuiltIn), name);
            HostsDocument doc = HostsDocument.Parse(once);
            Assert.True(doc.HasBlock, name);
            Assert.False(doc.BlockEdited, name);
            Assert.False(doc.MalformedMarkers, name);
            Assert.Equal(Fixtures.Hosts.OrderBy(h => h, System.StringComparer.Ordinal), doc.BlockHosts);
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void Changing_the_level_replaces_the_block_in_place(string name, byte[] file)
        {
            byte[] standard = Apply(file);
            byte[] strict = Apply(standard, Fixtures.Content(Level.Strict, "a.ads-one.net", "z.more-ads.net"));
            HostsDocument doc = HostsDocument.Parse(strict);
            Assert.Equal(Level.Strict, doc.Header!.Level);
            Assert.Equal(new[] { "a.ads-one.net", "z.more-ads.net" }, doc.BlockHosts);
            Assert.True(Hashing.BytesEqual(file, Remove(strict)), name);
        }

        [Theory]
        [MemberData(nameof(NoFinalNewline))]
        public void A_missing_final_line_break_is_added_once(string name, byte[] file)
        {
            string eol = HostsDocument.Parse(file).LineEnding;
            byte[] applied = Apply(file);
            Assert.Equal(S(file) + eol, S(Remove(applied)));
            Assert.True(Hashing.BytesEqual(applied, Apply(applied)), name);
        }

        [Fact]
        public void The_shipped_lists_round_trip_at_full_size()
        {
            BlockLists lists = BlockLists.LoadEmbedded();
            foreach (Level level in new[] { Level.Standard, Level.Strict })
            {
                BlockContent content = BlockPlanner.Plan(lists, level, null, null).ToContent(AppInfo.Version);
                byte[] stock = Fixtures.StockHostsBytes();
                byte[] applied = HostsDocument.Parse(stock).Apply(content, lists.Guard);
                HostsDocument doc = HostsDocument.Parse(applied);
                Assert.Equal(lists.CountFor(level), doc.Header!.HostCount);
                Assert.Equal(lists.CountFor(level), doc.BlockHosts.Count);
                Assert.False(doc.BlockEdited);
                Assert.Equal(stock, doc.Remove());
                Assert.Equal(applied, doc.Apply(content, lists.Guard));
                Assert.All(doc.BlockHosts, h => Assert.False(lists.Guard.IsGuarded(h)));
            }
        }

        [Fact]
        public void Append_adds_one_blank_line_then_the_block()
        {
            string stock = Fixtures.StockHosts();
            string expected = stock + "\r\n" + BlockText(Fixtures.Content());
            Assert.Equal(expected, S(Apply(B(stock))));
        }

        [Fact]
        public void An_empty_or_missing_file_gets_only_the_block()
        {
            string block = BlockText(Fixtures.Content());
            Assert.Equal(block, S(Apply(new byte[0])));
            Assert.Equal(block, S(Apply(null)));
            HostsDocument missing = HostsDocument.Parse(null);
            Assert.False(missing.Exists);
            Assert.Equal(0, missing.Length);
            Assert.Equal("", S(Remove(null)));
        }

        [Fact]
        public void The_block_has_the_exact_generated_shape()
        {
            BlockContent c = Fixtures.Content(Level.Standard, "b.ads-two.com", "a.ads-one.net", "a.ads-one.net");
            IReadOnlyList<string> lines = c.RenderLines(Guard.BuiltIn);
            string sha = Hashing.Sha256Hex("0.0.0.0 a.ads-one.net\n0.0.0.0 b.ads-two.com");
            Assert.Equal(new[]
            {
                "# >>> QuietHosts >>>",
                "# Managed by QuietHosts: use the app to change these lines. " + AppInfo.RepositoryUrl,
                "# QuietHosts 1.0.0 | level=standard | lists=2026-11-20 | hosts=2 | sha256=" + sha,
                "0.0.0.0 a.ads-one.net",
                "0.0.0.0 b.ads-two.com",
                "# <<< QuietHosts <<<",
            }, lines);
            Assert.Equal(sha, c.EntriesSha256);
        }

        [Fact]
        public void Entries_are_sorted_ordinally()
        {
            BlockContent c = Fixtures.Content(Level.Strict, "b.x-ads.com", "a-b.x-ads.com", "a.x-ads.com", "0.x-ads.com");
            Assert.Equal(new[] { "0.x-ads.com", "a-b.x-ads.com", "a.x-ads.com", "b.x-ads.com" }, c.Hosts);
        }

        [Fact]
        public void An_empty_plan_still_writes_markers_and_header()
        {
            BlockContent empty = new BlockContent(Level.Standard, "2026-11-20", new string[0], "1.0.0");
            HostsDocument doc = HostsDocument.Parse(Apply(B("127.0.0.1 localhost\r\n"), empty));
            Assert.True(doc.HasBlock);
            Assert.Equal(0, doc.Header!.HostCount);
            Assert.False(doc.BlockEdited);
            Assert.Empty(doc.BlockHosts);
        }

        [Fact]
        public void The_renderer_throws_for_guarded_or_invalid_names()
        {
            Assert.Throws<System.InvalidOperationException>(() => Fixtures.Content(Level.Standard, "ib.adnxs.com").RenderLines(Guard.BuiltIn));
            Assert.Throws<System.InvalidOperationException>(() => Fixtures.Content(Level.Standard, "printer.local").RenderLines(Guard.BuiltIn));
            Assert.Throws<System.InvalidOperationException>(() => Fixtures.Content(Level.Standard, "x.googleapis.com").RenderLines(Guard.LoadEmbedded()));
            Assert.Throws<System.ArgumentException>(() => Fixtures.Content(Level.Standard, "Upper.ads.com").RenderLines(Guard.BuiltIn));
            Assert.Throws<System.ArgumentException>(() => Fixtures.Content(Level.Standard, "1.2.3.4").RenderLines(Guard.BuiltIn));
            Assert.Throws<System.InvalidOperationException>(() => HostsDocument.Parse(B("a\r\n")).Apply(Fixtures.Content(Level.Standard, "www.microsoft.com"), Guard.BuiltIn));
        }

        [Theory]
        [InlineData("a\r\nb\r\n", "\r\n")]
        [InlineData("a\nb\n", "\n")]
        [InlineData("a\r\nb\n", "\r\n")]
        [InlineData("single line", "\r\n")]
        [InlineData("", "\r\n")]
        [InlineData("a\nb\r", "\r\n")]    // the CR at the end becomes a CRLF once a line break follows
        public void Line_ending_follows_the_file(string file, string expected)
        {
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.Equal(expected, doc.LineEnding);
            string applied = S(doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Contains(BlockText(Fixtures.Content(), expected), applied);
        }

        [Fact]
        public void Lf_files_stay_lf_only()
        {
            string applied = S(Apply(B("127.0.0.1 localhost\n")));
            Assert.DoesNotContain("\r", applied);
        }

        [Fact]
        public void The_utf8_bom_stays_and_a_block_right_after_it_is_found()
        {
            byte[] bomOnly = { 0xEF, 0xBB, 0xBF };
            byte[] applied = Apply(bomOnly);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'#' }, applied.Take(4).ToArray());
            HostsDocument doc = HostsDocument.Parse(applied);
            Assert.True(doc.HasBlock);
            Assert.False(doc.MalformedMarkers);
            Assert.Equal(bomOnly, Remove(applied));
        }

        [Fact]
        public void Ansi_comment_bytes_survive_exactly()
        {
            byte[] file = Bytes.Concat(Fixtures.Windows1252Comment(), Bytes.Ascii("127.0.0.1 localhost\r\n"));
            Assert.Throws<System.Text.DecoderFallbackException>(() => new System.Text.UTF8Encoding(false, true).GetString(file));
            byte[] applied = Apply(file);
            Assert.Equal(file, applied.Take(file.Length).ToArray());
            Assert.Equal(file, Remove(applied));
        }

        [Fact]
        public void Legacy_block_is_replaced_in_place()
        {
            string legacy = Fixtures.LegacyLayout();
            HostsDocument before = HostsDocument.Parse(B(legacy));
            Assert.True(before.HasLegacyBlock);
            Assert.False(before.HasBlock);
            Assert.Equal(3, before.LegacyCount);
            Assert.False(before.MicrosoftLinesOutside);   // ib.adnxs.com is inside the legacy block, not outside
            Assert.Equal(HostsWarnings.None, before.Warnings);

            string applied = S(before.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Equal(Fixtures.StockHosts() + "\r\n" + BlockText(Fixtures.Content()), applied);
            Assert.DoesNotContain("block-ads.ps1", applied);
            Assert.Equal(Fixtures.StockHosts(), S(Remove(B(applied))));
            Assert.Equal(Fixtures.StockHosts(), S(Remove(B(legacy))));
            Assert.Equal(applied, S(Apply(B(applied))));
        }

        [Fact]
        public void Legacy_markers_match_ignoring_case_and_spaces()
        {
            string file = "a\r\n\r\n  # >>> BLOCK-ADS.PS1 >>>  \r\n0.0.0.0 a.ads-one.net\r\n# <<< Block-Ads.ps1 <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.HasLegacyBlock);
            Assert.Equal(1, doc.LegacyCount);
            Assert.Equal("a\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Our_markers_match_case_sensitively()
        {
            HostsDocument doc = HostsDocument.Parse(B("# >>> quiethosts >>>\r\n0.0.0.0 a.ads-one.net\r\n# <<< quiethosts <<<\r\n"));
            Assert.False(doc.HasBlock);
            Assert.False(doc.MalformedMarkers);
            Assert.Contains("a.ads-one.net", doc.NamesMappedOutside);
        }

        [Fact]
        public void Both_blocks_present_removes_the_legacy_one_and_warns()
        {
            byte[] ours = Apply(Fixtures.StockHostsBytes());
            string both = S(ours) + "\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 c.old-ads.net\r\n# <<< block-ads.ps1 <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(both));
            Assert.True(doc.HasBlock);
            Assert.True(doc.HasLegacyBlock);
            Assert.Equal(HostsWarnings.LegacyBlockToo, doc.Warnings);
            Assert.Equal(S(ours), S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
            Assert.Equal(Fixtures.StockHosts(), S(doc.Remove()));
        }

        [Fact]
        public void Legacy_block_before_ours_is_removed_with_its_blank_line()
        {
            string legacyFirst = "x\r\n\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 c.old-ads.net\r\n# <<< block-ads.ps1 <<<\r\n";
            byte[] ours = Apply(B("y\r\n"));
            string file = legacyFirst + S(ours);
            string applied = S(Apply(B(file)));
            Assert.Equal("x\r\n" + S(ours), applied);
        }

        [Fact]
        public void Header_is_parsed_for_the_window()
        {
            HostsDocument doc = HostsDocument.Parse(Apply(Fixtures.StockHostsBytes(), Fixtures.Content(Level.Strict)));
            BlockHeader h = doc.Header!;
            Assert.Equal("1.0.0", h.AppVersion);
            Assert.Equal(Level.Strict, h.Level);
            Assert.Equal("2026-11-20", h.ListVersion);
            Assert.Equal(new System.DateTime(2026, 11, 20), h.ListDate);
            Assert.Equal(4, h.HostCount);
            Assert.Equal(Fixtures.Content().EntriesSha256, h.Sha256);
        }

        [Theory]
        [InlineData("# QuietHosts 1.0.0 | level=standard | lists=2026-11-20 | hosts=1 | sha256=abc")]
        [InlineData("# QuietHosts 1.0.0 | level=medium | lists=2026-11-20 | hosts=1")]
        [InlineData("# QuietHosts | level=standard")]
        [InlineData("# QuietHosts 1.0.0 | level=standard | hosts=x")]
        [InlineData("# QuietHosts 1.0.0 level=standard")]
        public void Damaged_headers_do_not_parse(string line) => Assert.Null(BlockHeader.TryParse(line));

        [Fact]
        public void Edited_entries_raise_EditedBlock()
        {
            string applied = S(Apply(Fixtures.StockHostsBytes()));
            Assert.False(HostsDocument.Parse(B(applied)).BlockEdited);

            string deleted = applied.Replace("0.0.0.0 b.ads-two.com\r\n", "");
            Assert.Equal(HostsWarnings.EditedBlock, HostsDocument.Parse(B(deleted)).Warnings);

            string added = applied.Replace("0.0.0.0 b.ads-two.com\r\n", "0.0.0.0 b.ads-two.com\r\n0.0.0.0 extra.ads.net\r\n");
            Assert.True(HostsDocument.Parse(B(added)).BlockEdited);

            string noHeader = string.Join("\r\n", applied.Split(new[] { "\r\n" }, System.StringSplitOptions.None).Where(l => !l.StartsWith("# QuietHosts ")));
            HostsDocument d = HostsDocument.Parse(B(noHeader));
            Assert.True(d.BlockEdited);
            Assert.Null(d.Header);

            // Applying again restores the generated block exactly.
            Assert.Equal(applied, S(Apply(B(deleted))));
            Assert.Equal(applied, S(Apply(B(noHeader))));
        }

        // ---------------------------------------------------------------- notice fingerprints and lines (T-A-44..47)

        /// <summary>
        /// Two Defender-sensitive lines outside our block, one foreign Defender-sensitive line inside it (it moves out at the
        /// next write), a comment line that names one and an unrelated line.
        /// </summary>
        private static string MicrosoftLinesFile(string firstLine = "0.0.0.0 vortex.data.microsoft.com", string unrelated = "10.0.0.5 nas.home.lan")
        {
            string block = S(Apply(B("127.0.0.1 localhost\r\n")));
            block = block.Replace("0.0.0.0 a.ads-one.net\r\n", "0.0.0.0 a.ads-one.net\r\n127.0.0.1 login.live.com\r\n");
            return firstLine + "  \t\r\n" +
                "# 0.0.0.0 settings-win.data.microsoft.com\r\n" +
                unrelated + "\r\n" +
                "0.0.0.0 watson.telemetry.microsoft.com # added by a tool\r\n" +
                block;
        }

        [Fact]
        public void Lines_that_block_Microsoft_addresses_are_listed_in_file_order()
        {
            HostsDocument doc = HostsDocument.Parse(B(MicrosoftLinesFile()));
            Assert.True(doc.MicrosoftLinesOutside);
            Assert.Equal(3, doc.MicrosoftLineCount);
            Assert.Equal(new[]
            {
                "0.0.0.0 vortex.data.microsoft.com",          // trailing blanks removed
                "0.0.0.0 watson.telemetry.microsoft.com # added by a tool",
                "127.0.0.1 login.live.com",                   // the foreign line inside our block
            }, doc.MicrosoftLines);
            string hash = doc.MicrosoftLinesSha256!;
            Assert.True(Hashing.IsSha256Hex(hash));
            Assert.Equal(Hashing.Sha256Hex(Latin1.Encode(string.Join("\n", doc.MicrosoftLines))), hash);

            // One of those lines changes: another fingerprint. The unrelated line changes: the same one.
            Assert.NotEqual(hash, HostsDocument.Parse(B(MicrosoftLinesFile(firstLine: "0.0.0.0 v10.vortex-win.data.microsoft.com"))).MicrosoftLinesSha256);
            Assert.Equal(hash, HostsDocument.Parse(B(MicrosoftLinesFile(unrelated: "10.0.0.6 printer.home.lan"))).MicrosoftLinesSha256);
        }

        [Fact]
        public void At_most_fifty_Microsoft_lines_are_kept_but_all_are_counted_and_hashed()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 60; i++) sb.Append("0.0.0.0 host").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(".data.microsoft.com\r\n");
            HostsDocument many = HostsDocument.Parse(B(sb.ToString()));
            Assert.Equal(60, many.MicrosoftLineCount);
            Assert.Equal(HostsDocument.MaxMicrosoftLinesShown, many.MicrosoftLines.Count);
            Assert.Equal("0.0.0.0 host49.data.microsoft.com", many.MicrosoftLines[49]);
            string all = string.Join("\n", sb.ToString().Split(new[] { "\r\n" }, System.StringSplitOptions.RemoveEmptyEntries));
            Assert.Equal(Hashing.Sha256Hex(Latin1.Encode(all)), many.MicrosoftLinesSha256);

            // A long line is cut for display but hashed whole.
            string longLine = "0.0.0.0 login.live.com # " + new string('x', 300 - 25);
            Assert.Equal(300, longLine.Length);
            HostsDocument one = HostsDocument.Parse(B(longLine + "\r\n"));
            Assert.Equal(1, one.MicrosoftLineCount);
            Assert.Equal(HostsDocument.MaxMicrosoftLineLength, one.MicrosoftLines[0].Length);
            Assert.Equal(longLine.Substring(0, 200), one.MicrosoftLines[0]);
            Assert.Equal(Hashing.Sha256Hex(Latin1.Encode(longLine)), one.MicrosoftLinesSha256);
        }

        [Fact]
        public void The_block_fingerprint_follows_every_line_of_the_block_and_nothing_outside()
        {
            string applied = S(Apply(Fixtures.StockHostsBytes()));
            HostsDocument doc = HostsDocument.Parse(B(applied));
            string hash = doc.BlockLinesSha256!;
            Assert.True(Hashing.IsSha256Hex(hash));
            string[] lines = applied.Split(new[] { "\r\n" }, System.StringSplitOptions.None);
            int begin = System.Array.IndexOf(lines, HostsDocument.BeginMarker), end = System.Array.IndexOf(lines, HostsDocument.EndMarker);
            Assert.Equal(Hashing.Sha256Hex(Latin1.Encode(string.Join("\n", lines.Skip(begin + 1).Take(end - begin - 1)))), hash);

            // An entry rewritten with a tab (still ours, still blocking): another fingerprint.
            HostsDocument tabbed = HostsDocument.Parse(B(applied.Replace("0.0.0.0 b.ads-two.com\r\n", "0.0.0.0\tb.ads-two.com\r\n")));
            Assert.True(tabbed.BlockEdited);
            Assert.NotEqual(hash, tabbed.BlockLinesSha256);

            // A line outside the block changes: the same fingerprint.
            Assert.Equal(hash, HostsDocument.Parse(B("10.0.0.5 nas.home.lan\r\n" + applied)).BlockLinesSha256);
        }

        [Fact]
        public void Without_a_block_or_Microsoft_lines_the_fingerprints_are_null()
        {
            HostsDocument plain = HostsDocument.Parse(Fixtures.StockHostsBytes());
            Assert.Null(plain.BlockLinesSha256);
            Assert.Null(plain.MicrosoftLinesSha256);
            Assert.Empty(plain.MicrosoftLines);
            Assert.Equal(0, plain.MicrosoftLineCount);

            // A damaged block (a begin marker only) is no well-formed block.
            Assert.Null(HostsDocument.Parse(B("a\r\n# >>> QuietHosts >>>\r\n0.0.0.0 a.ads-one.net\r\n")).BlockLinesSha256);
            Assert.Null(HostsDocument.Parse(null).BlockLinesSha256);
            HostsDocument refused = HostsDocument.Parse(new byte[] { 0xFF, 0xFE, 0x23, 0x00 });
            Assert.True(refused.IsRefused);
            Assert.Null(refused.BlockLinesSha256);
            Assert.Null(refused.MicrosoftLinesSha256);
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void The_new_members_agree_with_the_flags_on_the_corpus(string name, byte[] file)
        {
            foreach (byte[] bytes in new[] { file, Apply(file) })
            {
                HostsDocument doc = HostsDocument.Parse(bytes);
                Assert.True(doc.MicrosoftLinesOutside == (doc.MicrosoftLineCount > 0), name);
                Assert.True((doc.MicrosoftLinesSha256 != null) == doc.MicrosoftLinesOutside, name);
                Assert.True((doc.BlockLinesSha256 != null) == doc.HasBlock, name);
                Assert.Equal(System.Math.Min(doc.MicrosoftLineCount, HostsDocument.MaxMicrosoftLinesShown), doc.MicrosoftLines.Count);
            }
        }
    }
}
