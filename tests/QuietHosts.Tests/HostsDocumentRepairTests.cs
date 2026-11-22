using System.Linq;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;
using static QuietHosts.Tests.HostsDocumentTests;

namespace QuietHosts.Tests
{
    public class HostsDocumentRepairTests
    {
        private static readonly string Block = BlockText(Fixtures.Content());

        [Fact]
        public void Unpaired_begin_removes_only_generated_lines_up_to_the_end_of_the_file()
        {
            string file = "a\r\n# >>> QuietHosts >>>\r\n# QuietHosts 1.0.0 | level=standard\r\n0.0.0.0 x.ads-old.com\r\n# my note\r\n0.0.0.0  spaced.ads.com\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.False(doc.HasBlock);
            Assert.True(doc.MalformedMarkers);
            Assert.Equal(HostsWarnings.MalformedMarkers, doc.Warnings);
            Assert.DoesNotContain("x.ads-old.com", doc.NamesMappedOutside);   // it is removed at the next write
            Assert.Contains("spaced.ads.com", doc.NamesMappedOutside);        // not our exact shape: it stays
            Assert.Equal("a\r\n# my note\r\n0.0.0.0  spaced.ads.com\r\n", S(doc.Remove()));
            Assert.Equal("a\r\n# my note\r\n0.0.0.0  spaced.ads.com\r\n\r\n" + Block, S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
        }

        [Fact]
        public void Unpaired_begin_stops_at_the_next_marker()
        {
            string file = "# >>> QuietHosts >>>\r\n0.0.0.0 x.ads-old.com\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 y.ads-old.com\r\n# <<< block-ads.ps1 <<<\r\nz\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.True(doc.HasLegacyBlock);
            Assert.Equal("z\r\n", S(doc.Remove()));
            Assert.Equal(Block + "z\r\n", S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
        }

        [Fact]
        public void Unpaired_end_removes_only_the_marker()
        {
            string file = "a\r\n0.0.0.0 x.ads-old.com\r\n# <<< QuietHosts <<<\r\nb\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.Contains("x.ads-old.com", doc.NamesMappedOutside);
            Assert.Equal("a\r\n0.0.0.0 x.ads-old.com\r\nb\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Duplicated_blocks_keep_the_first_and_remove_the_second()
        {
            string first = S(Apply(B("a\r\n")));
            string file = first + "b\r\n# >>> QuietHosts >>>\r\n0.0.0.0 x.ads-old.com\r\n# kept line\r\n# <<< QuietHosts <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.HasBlock);
            Assert.True(doc.MalformedMarkers);
            Assert.Equal(first + "b\r\n# kept line\r\n", S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
            Assert.Equal("a\r\nb\r\n# kept line\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Nested_markers_are_repaired()
        {
            string file = "a\r\n# >>> QuietHosts >>>\r\n0.0.0.0 x.ads-old.com\r\n# >>> QuietHosts >>>\r\n0.0.0.0 y.ads-old.com\r\n# <<< QuietHosts <<<\r\n0.0.0.0 z.ads-old.com\r\n# <<< QuietHosts <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.True(doc.HasBlock);                         // the inner pair is well formed
            Assert.Equal(new[] { "y.ads-old.com" }, doc.BlockHosts);
            Assert.DoesNotContain("z.ads-old.com", doc.NamesMappedOutside);   // before a stray end: removed too
            string removed = S(doc.Remove());
            Assert.Equal("a\r\n", removed);
            string applied = S(doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Equal("a\r\n" + Block, applied);
            Assert.False(HostsDocument.Parse(B(applied)).MalformedMarkers);
        }

        [Fact]
        public void A_deleted_begin_marker_removes_our_lines_from_the_managed_by_line_on()
        {
            string applied = S(Apply(B("a\r\n0.0.0.0 my.own-block.com\r\n")));
            string damaged = applied.Replace("# >>> QuietHosts >>>\r\n", "");
            HostsDocument doc = HostsDocument.Parse(B(damaged));
            Assert.False(doc.HasBlock);
            Assert.True(doc.MalformedMarkers);
            Assert.Equal(new[] { "my.own-block.com" }, doc.NamesMappedOutside.ToArray());   // our entries aren't "mapped outside"
            // The user's line above stays, and the blank line Apply put before the block goes with it.
            Assert.Equal("a\r\n0.0.0.0 my.own-block.com\r\n", S(doc.Remove()));
            string reapplied = S(doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Equal(applied, reapplied);   // one blank line before the block, as at the first Apply
            Assert.Equal(Fixtures.Hosts.Length, HostsDocument.Parse(B(reapplied)).Header!.HostCount);

            // Without the Managed-by line, the header is the anchor.
            string noManagedBy = string.Join("\r\n", damaged.Split(new[] { "\r\n" }, System.StringSplitOptions.None)
                .Where(l => !l.StartsWith("# Managed by", System.StringComparison.Ordinal)));
            Assert.Equal("a\r\n0.0.0.0 my.own-block.com\r\n", S(HostsDocument.Parse(B(noManagedBy)).Remove()));
        }

        public static System.Collections.Generic.IEnumerable<object[]> Damages() =>
            new[] { "begin", "end", "begin+managed-by", "end+managed-by" }.Select(d => new object[] { d });

        // A damaged marker and its repair give the file back: Off (Remove) restores it byte for byte, and the next
        // write (Apply) is exactly the first one, so damage-and-repair cycles never add blank lines.
        [Theory]
        [MemberData(nameof(Damages))]
        public void A_damaged_block_is_removed_with_its_blank_line(string damage)
        {
            foreach (object[] c in Fixtures.Corpus())
            {
                string name = (string)c[0];
                byte[] file = (byte[])c[1];
                byte[] applied = Apply(file);
                string eol = HostsDocument.Parse(applied).LineEnding;
                string damaged = S(applied);
                foreach (string part in damage.Split('+'))
                {
                    string prefix = part == "begin" ? HostsDocument.BeginMarker : part == "end" ? HostsDocument.EndMarker : HostsDocument.ManagedByLinePrefix;
                    int at = damaged.IndexOf(prefix, System.StringComparison.Ordinal);
                    int lineEnd = damaged.IndexOf(eol, at, System.StringComparison.Ordinal) + eol.Length;
                    damaged = damaged.Remove(at, lineEnd - at);
                }
                HostsDocument doc = HostsDocument.Parse(B(damaged));
                Assert.True(doc.MalformedMarkers, name);
                Assert.True(Hashing.BytesEqual(file, doc.Remove()), name + ": " + damage + ": Remove");
                Assert.True(Hashing.BytesEqual(applied, doc.Apply(Fixtures.Content(), Guard.BuiltIn)), name + ": " + damage + ": Apply");
            }
        }

        [Fact]
        public void A_lone_stray_begin_marker_keeps_the_blank_line_before_it()
        {
            // Nothing after the marker proves the block was ours: only the marker goes.
            string file = "a\r\n\r\n# >>> QuietHosts >>>\r\n# my note\r\n";
            Assert.Equal("a\r\n\r\n# my note\r\n", S(HostsDocument.Parse(B(file)).Remove()));
        }

        [Fact]
        public void A_deleted_begin_marker_with_line_ends_translated_twice_leaves_none_of_our_entries()
        {
            string applied = S(Apply(B("a\r\n0.0.0.0 my.own-block.com\r\n")));
            string damaged = applied.Replace("# >>> QuietHosts >>>\r\n", "").Replace("\r\n", "\r\r\n");
            HostsDocument doc = HostsDocument.Parse(B(damaged));
            Assert.True(doc.MalformedMarkers);
            Assert.Equal(new[] { "my.own-block.com" }, doc.NamesMappedOutside.ToArray());
            string removed = S(doc.Remove());
            Assert.StartsWith("a\r\r\n0.0.0.0 my.own-block.com\r\r\n", removed);
            Assert.All(Fixtures.Hosts, h => Assert.DoesNotContain(h, removed));
            Assert.DoesNotContain("QuietHosts", removed);

            // A tab for the space, from the Managed-by line on, is ours too.
            string tabs = applied.Replace("# >>> QuietHosts >>>\r\n", "").Replace("0.0.0.0 a.ads-one.net", "0.0.0.0\ta.ads-one.net");
            Assert.Equal("a\r\n0.0.0.0 my.own-block.com\r\n", S(HostsDocument.Parse(B(tabs)).Remove()));
        }

        [Fact]
        public void A_rewritten_entry_after_a_stray_begin_marker_stays_without_proof()
        {
            // A stray begin's range runs to the end of the file, so other programs' tab-separated lines after it stay.
            string file = "a\r\n# >>> QuietHosts >>>\r\n0.0.0.0 x.ads-old.com\r\n0.0.0.0\tother.tool-ads.com\r\n";
            Assert.Equal("a\r\n0.0.0.0\tother.tool-ads.com\r\n", S(HostsDocument.Parse(B(file)).Remove()));
        }

        [Fact]
        public void A_stray_end_after_our_block_removes_the_old_tail_a_torn_write_left()
        {
            // A shorter block written over a longer one, cut before SetLength: the old tail follows the new end marker.
            string after = S(Apply(B("a\r\n")));
            string torn = after + "ds-old.com\r\n0.0.0.0 old-1.ads-old.com\r\n0.0.0.0 old-2.ads-old.com\r\n# <<< QuietHosts <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(torn));
            Assert.True(doc.HasBlock);
            Assert.True(doc.MalformedMarkers);
            Assert.False(doc.BlockEdited);
            Assert.DoesNotContain("old-1.ads-old.com", doc.NamesMappedOutside);
            Assert.Equal(after + "ds-old.com\r\n", S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
            Assert.Equal("a\r\nds-old.com\r\n", S(doc.Remove()));
        }

        [Fact]
        public void A_stray_end_keeps_lines_above_our_managed_by_line_and_other_comments()
        {
            string ours = BlockText(Fixtures.Content()).Replace("# >>> QuietHosts >>>\r\n", "");
            string file = "x\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 l.ads-old.com\r\n# <<< block-ads.ps1 <<<\r\n0.0.0.0 my.own-block.com\r\n" + ours;
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.HasLegacyBlock);
            Assert.True(doc.MalformedMarkers);
            Assert.Equal("x\r\n0.0.0.0 my.own-block.com\r\n", S(doc.Remove()));

            // A comment that only starts like our header is no anchor, and with no marker before it, only the end marker goes.
            string loose = "0.0.0.0 my.own-block.com\r\n# QuietHosts rocks\r\n0.0.0.0 my.other-block.com\r\n# <<< QuietHosts <<<\r\n";
            Assert.Equal("0.0.0.0 my.own-block.com\r\n# QuietHosts rocks\r\n0.0.0.0 my.other-block.com\r\n", S(HostsDocument.Parse(B(loose)).Remove()));
        }

        [Fact]
        public void A_stray_legacy_end_after_our_block_removes_the_zero_lines_before_it()
        {
            string ours = S(Apply(B("a\r\n")));
            string file = ours + "0.0.0.0 l.ads-old.com\r\n# kept\r\n# <<< block-ads.ps1 <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.Equal(ours + "# kept\r\n", S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
        }

        [Theory]
        [InlineData("\r", true)]      // line ends translated twice: every line ends with CR CR LF
        [InlineData(" ", false)]      // a trailing space on our entries
        [InlineData("\t\t", false)]   // trailing tabs
        public void Our_entries_with_changed_line_ends_or_trailing_blanks_are_still_ours(string added, bool everyLine)
        {
            string applied = S(Apply(Fixtures.StockHostsBytes()));
            string changed = AppendToLines(applied, added, everyLine);
            HostsDocument doc = HostsDocument.Parse(B(changed));
            Assert.True(doc.HasBlock);
            Assert.True(doc.BlockEdited);                   // EditedBlock still shows
            Assert.Equal(Fixtures.Hosts.OrderBy(h => h, System.StringComparer.Ordinal), doc.BlockHosts);   // and they still block
            Assert.Equal(0, doc.ForeignLinesInBlocks);
            Assert.Empty(doc.NamesMappedOutside);
            Assert.DoesNotContain("0.0.0.0", S(doc.Remove()));
            string reapplied = S(doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Equal(Fixtures.Hosts.Length, HostsDocument.Parse(B(reapplied)).Header!.HostCount);
            Assert.False(HostsDocument.Parse(B(reapplied)).BlockEdited);
        }

        [Fact]
        public void A_tab_for_the_space_is_still_ours_but_two_separators_are_foreign()
        {
            string applied = S(Apply(B("a\r\n")));
            string tab = applied.Replace("0.0.0.0 a.ads-one.net\r\n", "0.0.0.0\ta.ads-one.net\r\n").Replace("0.0.0.0 b.ads-two.com\r\n", "0.0.0.0 B.ads-two.com\r\n");
            HostsDocument doc = HostsDocument.Parse(B(tab));
            Assert.True(doc.BlockEdited);
            Assert.Equal(0, doc.ForeignLinesInBlocks);
            Assert.Equal(Fixtures.Hosts.OrderBy(h => h, System.StringComparer.Ordinal), doc.BlockHosts);   // lowercased
            Assert.Equal(applied, S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));

            string two = applied.Replace("0.0.0.0 a.ads-one.net\r\n", "0.0.0.0  a.ads-one.net\r\n").Replace("0.0.0.0 b.ads-two.com\r\n", "0.0.0.0 \tb.ads-two.com\r\n");
            HostsDocument foreign = HostsDocument.Parse(B(two));
            Assert.Equal(2, foreign.ForeignLinesInBlocks);
            Assert.Contains("a.ads-one.net", foreign.NamesMappedOutside);
            Assert.Contains("b.ads-two.com", foreign.NamesMappedOutside);
        }

        private static string AppendToLines(string text, string added, bool everyLine)
        {
            string[] lines = text.Split(new[] { "\r\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length - 1; i++)   // the last piece is what follows the final line break
            {
                if (everyLine || lines[i].StartsWith("0.0.0.0 ", System.StringComparison.Ordinal)) lines[i] += added;
            }
            return string.Join("\r\n", lines);
        }

        [Fact]
        public void Mismatched_markers_are_both_stray()
        {
            string file = "a\r\n# >>> QuietHosts >>>\r\n0.0.0.0 x.ads-old.com\r\n# <<< block-ads.ps1 <<<\r\nb\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.False(doc.HasBlock);
            Assert.False(doc.HasLegacyBlock);
            Assert.Equal("a\r\nb\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Stray_legacy_begin_removes_its_zero_lines()
        {
            string file = "a\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 x.ads-old.com\r\n0.0.0.0 y.ads-old.com\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.True(doc.MalformedMarkers);
            Assert.Equal("a\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Hand_disabled_lines_move_their_hosts_to_the_allowlist()
        {
            string applied = S(Apply(B("a\r\n")));
            string edited = applied
                .Replace("0.0.0.0 a.ads-one.net\r\n", "#0.0.0.0 a.ads-one.net\r\n")
                .Replace("0.0.0.0 b.ads-two.com\r\n", "#  0.0.0.0\tB.ads-two.com  \r\n");
            HostsDocument doc = HostsDocument.Parse(B(edited));
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com" }, doc.HandDisabledHosts);
            Assert.True(doc.BlockEdited);
            Assert.Equal(0, doc.ForeignLinesInBlocks);
            // The caller allowlists them; the hand-disabled lines themselves are not kept.
            string reapplied = S(doc.Apply(Fixtures.Content(Level.Standard, "pixel.four-ads.org", "track.metrics-three.io"), Guard.BuiltIn));
            Assert.DoesNotContain("#0.0.0.0", reapplied);
            Assert.DoesNotContain("ads-one", reapplied);
            Assert.Equal("a\r\n", S(doc.Remove()));
        }

        [Fact]
        public void A_hand_disabled_line_still_counts_after_line_ends_were_translated_twice()
        {
            string applied = S(Apply(B("a\r\n")));
            string edited = applied.Replace("0.0.0.0 a.ads-one.net\r\n", "#0.0.0.0 a.ads-one.net\r\n").Replace("\r\n", "\r\r\n");
            HostsDocument doc = HostsDocument.Parse(B(edited));
            Assert.Equal(new[] { "a.ads-one.net" }, doc.HandDisabledHosts);
            Assert.Equal(0, doc.ForeignLinesInBlocks);
            Assert.Equal(new[] { "b.ads-two.com", "pixel.four-ads.org", "track.metrics-three.io" }, doc.BlockHosts);
            string reapplied = S(doc.Apply(Fixtures.Content(Level.Standard, "b.ads-two.com", "pixel.four-ads.org", "track.metrics-three.io"), Guard.BuiltIn));
            Assert.DoesNotContain("a.ads-one.net", reapplied);   // the caller allowlists it; the line itself goes
        }

        [Fact]
        public void Foreign_lines_inside_the_block_move_below_the_end_marker_unchanged()
        {
            string applied = S(Apply(B("a\r\n")));
            string edited = applied.Replace("0.0.0.0 b.ads-two.com\r\n", "0.0.0.0 b.ads-two.com\r\n127.0.0.1 dev.mysite.test-x.com\n# keep me\r\n");
            HostsDocument doc = HostsDocument.Parse(B(edited));
            Assert.Equal(2, doc.ForeignLinesInBlocks);
            Assert.Contains("dev.mysite.test-x.com", doc.NamesMappedOutside);
            Assert.False(doc.BlockEdited);   // the entries still match the header
            string reapplied = S(doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Equal(applied + "127.0.0.1 dev.mysite.test-x.com\n# keep me\r\n", reapplied);
            Assert.Equal(reapplied, S(Apply(B(reapplied))));
            Assert.Equal("a\r\n127.0.0.1 dev.mysite.test-x.com\n# keep me\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Foreign_lines_inside_the_legacy_block_are_kept()
        {
            string file = "a\r\n\r\n# >>> block-ads.ps1 >>>\r\n0.0.0.0 x.ads-old.com\r\n# user note\r\n#0.0.0.0 y.ads-old.com\r\n# <<< block-ads.ps1 <<<\r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.Equal(1, doc.LegacyCount);
            Assert.Equal(2, doc.ForeignLinesInBlocks);
            Assert.Empty(doc.HandDisabledHosts);
            Assert.Equal("a\r\n\r\n" + Block + "# user note\r\n#0.0.0.0 y.ads-old.com\r\n", S(doc.Apply(Fixtures.Content(), Guard.BuiltIn)));
            Assert.Equal("a\r\n# user note\r\n#0.0.0.0 y.ads-old.com\r\n", S(doc.Remove()));
        }

        [Fact]
        public void Names_mapped_outside_the_blocks_are_collected()
        {
            string file =
                "127.0.0.1 a.ads-one.net b.ads-two.com # inline comment c.ads-three.com\r\n" +
                "# 0.0.0.0 d.ads-four.com\r\n" +
                "::1\tE.Ads-Five.com.\r\n" +
                "fe80::1%12 f.ads-six.com\r\n" +
                "not-an-ip g.ads-seven.com\r\n" +
                "10.0.0.1\r\n" +
                "   0.0.0.0    h.ads-eight.com   \r\n";
            HostsDocument doc = HostsDocument.Parse(B(file));
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com", "e.ads-five.com", "f.ads-six.com", "h.ads-eight.com" },
                doc.NamesMappedOutside.OrderBy(n => n, System.StringComparer.Ordinal).ToArray());
        }

        [Fact]
        public void The_block_never_shadows_names_mapped_outside()
        {
            BlockLists lists = Fixtures.SmallLists();
            HostsDocument doc = HostsDocument.Parse(B("192.168.1.10 a.ads-one.net\r\n"));
            BlockPlan plan = BlockPlanner.Plan(lists, Level.Standard, null, doc.NamesMappedOutside);
            Assert.Equal(new[] { "b.ads-two.com", "track.metrics-three.io" }, plan.Hosts);
            Assert.Equal(1, plan.SkippedMappedOutside);
        }

        [Theory]
        [InlineData("0.0.0.0 vortex.data.microsoft.com\r\n", true)]
        [InlineData("127.0.0.1 localhost login.live.com\r\n", true)]
        [InlineData("0.0.0.0 ads.example.com # telemetry.microsoft.com\r\n", false)]
        [InlineData("# 0.0.0.0 vortex.data.microsoft.com\r\n", false)]
        [InlineData("0.0.0.0 notmicrosoft.com\r\n", false)]
        public void Microsoft_lines_outside_raise_the_warning(string line, bool expected)
        {
            HostsDocument doc = HostsDocument.Parse(B("# header\r\n" + line));
            Assert.Equal(expected, doc.MicrosoftLinesOutside);
            Assert.Equal(expected, (doc.Warnings & HostsWarnings.MicrosoftLinesOutside) != 0);
        }

        [Fact]
        public void Microsoft_lines_in_a_foreign_line_inside_our_block_also_warn()
        {
            string applied = S(Apply(B("a\r\n")));
            string edited = applied.Replace("# <<< QuietHosts <<<", "0.0.0.0  settings-win.data.microsoft.com\r\n# <<< QuietHosts <<<");
            Assert.True(HostsDocument.Parse(B(edited)).MicrosoftLinesOutside);
        }

        // Only Microsoft's own service names warn (Defender may reset the file for them). Names the Microsoft guard also
        // protects but common ad and phishing lists block (Xandr, GitHub pages, Azure customer sites, LinkedIn, games) don't.
        [Theory]
        [InlineData("ib.adnxs.com", false)]
        [InlineData("someone.github.io", false)]
        [InlineData("raw.githubusercontent.com", false)]
        [InlineData("phish.azurewebsites.net", false)]
        [InlineData("px.ads.linkedin.com", false)]
        [InlineData("track.king.com", false)]
        [InlineData("files.blob.core.windows.net", false)]
        [InlineData("www.microsoft.com", true)]
        [InlineData("telemetry.microsoft.com", true)]
        [InlineData("settings.windows.com", true)]
        [InlineData("download.windowsupdate.com", true)]
        [InlineData("watson.live.com", true)]
        [InlineData("bat.bing.com", true)]
        [InlineData("www.msftncsi.com", true)]
        [InlineData("login.windows.net", true)]
        [InlineData("anything.microsoft", true)]
        public void The_Defender_warning_covers_Microsofts_own_service_names(string name, bool expected)
        {
            HostsDocument outside = HostsDocument.Parse(B("# header\r\n0.0.0.0 " + name + "\r\n"));
            Assert.Equal(expected, outside.MicrosoftLinesOutside);
            Assert.Equal(expected, (outside.Warnings & HostsWarnings.MicrosoftLinesOutside) != 0);

            // The same as a foreign line inside our block: it moves below the block, so it stays in the file.
            string applied = S(Apply(B("a\r\n")));
            HostsDocument inside = HostsDocument.Parse(B(applied.Replace("# <<< QuietHosts <<<", "127.0.0.1\t" + name + "\r\n# <<< QuietHosts <<<")));
            Assert.Equal(1, inside.ForeignLinesInBlocks);
            Assert.Equal(expected, inside.MicrosoftLinesOutside);
        }

        [Fact]
        public void LargeFile_counts_only_bytes_outside_the_blocks()
        {
            string line = "# " + new string('x', 1022) + "\r\n";      // 1,026 bytes
            var sb = new System.Text.StringBuilder();
            while (sb.Length + line.Length <= Limits.LargeFileBytes) sb.Append(line);
            string pad = new string('y', (int)Limits.LargeFileBytes - sb.Length - 2) + "\r\n";
            string exactly = sb + pad;
            Assert.Equal(Limits.LargeFileBytes, exactly.Length);
            HostsDocument atLimit = HostsDocument.Parse(B(exactly));
            Assert.Equal(Limits.LargeFileBytes, atLimit.BytesOutsideBlocks);
            Assert.Equal(HostsWarnings.None, atLimit.Warnings);
            Assert.Equal(HostsWarnings.LargeFile, HostsDocument.Parse(B(exactly + "z\r\n")).Warnings);

            // A large block doesn't count.
            var many = Enumerable.Range(0, 12000).Select(i => "h" + i + ".many-ads.com").ToArray();
            byte[] withBlock = Apply(B("a\r\n"), Fixtures.Content(Level.Strict, many));
            Assert.True(withBlock.Length > Limits.LargeFileBytes);
            HostsDocument doc = HostsDocument.Parse(withBlock);
            Assert.Equal(5, doc.BytesOutsideBlocks);   // "a\r\n" plus the blank line before the block
            Assert.Equal(HostsWarnings.None, doc.Warnings);
        }

        [Theory]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x23, 0x00 }, "UTF-16 LE")]
        [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x23 }, "UTF-16 BE")]
        [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x23, 0x00, 0x00, 0x00 }, "UTF-32 LE")]
        [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x23 }, "UTF-32 BE")]
        [InlineData(new byte[] { 0x23, 0x20, 0x61, 0x00, 0x62, 0x0D, 0x0A }, "NUL byte")]
        public void Unsupported_encodings_are_refused(byte[] file, string name)
        {
            HostsDocument doc = HostsDocument.Parse(file);
            Assert.True(doc.IsRefused, name);
            Assert.Equal(RefusalReason.UnsupportedEncoding, doc.Refusal);
            Assert.Equal(HostsWarnings.None, doc.Warnings);
            Assert.Throws<System.InvalidOperationException>(() => doc.Apply(Fixtures.Content(), Guard.BuiltIn));
            Assert.Throws<System.InvalidOperationException>(() => doc.Remove());
        }

        [Fact]
        public void Files_over_16_MB_are_refused()
        {
            var big = new byte[Limits.MaxHostsFileBytes + 1];
            for (int i = 0; i < big.Length; i++) big[i] = (byte)'#';
            HostsDocument doc = HostsDocument.Parse(big);
            Assert.Equal(RefusalReason.TooLarge, doc.Refusal);
            Assert.Throws<System.InvalidOperationException>(() => doc.Remove());

            var limit = new byte[Limits.MaxHostsFileBytes];
            for (int i = 0; i < limit.Length; i++) limit[i] = (byte)'#';
            Assert.Equal(RefusalReason.None, HostsDocument.Parse(limit).Refusal);
        }

        [Fact]
        public void A_utf8_bom_is_not_refused()
        {
            HostsDocument doc = HostsDocument.Parse(new byte[] { 0xEF, 0xBB, 0xBF, 0x23, 0x0D, 0x0A });
            Assert.False(doc.IsRefused);
        }

        [Fact]
        public void Removing_keeps_a_blank_line_that_is_not_directly_before_the_block()
        {
            string file = "a\r\n\r\n# note\r\n" + Block;
            Assert.Equal("a\r\n\r\n# note\r\n", S(Remove(B(file))));
            string twoBlanks = "a\r\n\r\n\r\n" + Block;
            Assert.Equal("a\r\n\r\n", S(Remove(B(twoBlanks))));
            string spaces = "a\r\n  \r\n" + Block;
            Assert.Equal("a\r\n  \r\n", S(Remove(B(spaces))));
        }
    }
}
