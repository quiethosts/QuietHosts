using System.IO;
using System.Linq;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    public class PlannerAndHelperTests
    {
        [Fact]
        public void Standard_writes_the_default_tier_and_strict_adds_the_strict_tier()
        {
            BlockLists lists = Fixtures.SmallLists();
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com", "track.metrics-three.io" }, BlockPlanner.Plan(lists, Level.Standard, null, null).Hosts);
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com", "pixel.four-ads.org", "track.metrics-three.io", "x.five-ads.net" },
                BlockPlanner.Plan(lists, Level.Strict, null, null).Hosts);
        }

        [Fact]
        public void Allowlisted_and_outside_names_are_left_out()
        {
            BlockLists lists = Fixtures.SmallLists();
            BlockPlan plan = BlockPlanner.Plan(lists, Level.Strict, new[] { "b.ads-two.com", "pixel.four-ads.org" }, new[] { "x.five-ads.net" });
            Assert.Equal(new[] { "a.ads-one.net", "track.metrics-three.io" }, plan.Hosts);
            Assert.Equal(2, plan.SkippedAllowed);
            Assert.Equal(1, plan.SkippedMappedOutside);
            Assert.Equal(0, plan.SkippedGuarded);
            Assert.Equal("2026-11-20", plan.ListVersion);
            BlockContent c = plan.ToContent("1.2.3");
            Assert.Equal("1.2.3", c.AppVersion);
            Assert.Equal(Level.Strict, c.Level);
        }

        [Fact]
        public void Guarded_names_never_reach_the_plan()
        {
            BlockLists lists = BlockLists.Parse("# version: 2026-11-20\n[ad-server]\nib.adnxs.com\na.ads-one.net\n", "# version: 2026-11-20\n", Guard.BuiltIn);
            Assert.Equal(1, lists.GuardedDroppedCount);
            Assert.Equal(new[] { "a.ads-one.net" }, BlockPlanner.Plan(lists, Level.Strict, null, null).Hosts);
        }

        [Fact]
        public void Allowlist_prune_keeps_only_listed_valid_hosts()
        {
            BlockLists lists = Fixtures.SmallLists();
            var pruned = Allowlist.Prune(new[] { "B.ads-two.com", "b.ads-two.com", "gone.example-ads.com", "not a host", "a.ads-one.net." }, lists);
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com" }, pruned);
            Assert.Equal(new[] { "gone.example-ads.com" }, Allowlist.Prune(new[] { "gone.example-ads.com" }, null));
        }

        [Fact]
        public void Fix_list_puts_allowed_first_then_a_to_z()
        {
            BlockLists lists = Fixtures.SmallLists();
            var rows = FixList.Build(lists, Level.Standard, new[] { "track.metrics-three.io", "pixel.four-ads.org" });
            Assert.Equal(new[] { "pixel.four-ads.org", "track.metrics-three.io", "a.ads-one.net", "b.ads-two.com" }, rows.Select(r => r.Host).ToArray());
            Assert.True(rows[0].Allowed);
            Assert.Equal(Tier.Strict, rows[0].Tier);
            Assert.False(rows[2].Allowed);
            Assert.Equal("Ad server", rows[2].CategoryWords);
            Assert.Equal("One Ads", rows[2].Company);
            Assert.Equal("may stall video", rows[3].Note);
        }

        [Fact]
        public void Fix_list_search_matches_address_company_words_and_note()
        {
            var rows = FixList.Build(Fixtures.SmallLists(), Level.Strict, null);
            Assert.Equal(new[] { "b.ads-two.com" }, FixList.Search(rows, "STALL").Select(r => r.Host).ToArray());
            Assert.Equal(new[] { "track.metrics-three.io" }, FixList.Search(rows, "analytics tracker").Select(r => r.Host).ToArray());
            Assert.Equal(new[] { "pixel.four-ads.org" }, FixList.Search(rows, "four").Select(r => r.Host).ToArray());
            Assert.Equal(new[] { "a.ads-one.net" }, FixList.Search(rows, "one ads").Select(r => r.Host).ToArray());
            Assert.Equal(5, FixList.Search(rows, "  ").Count);
            Assert.Empty(FixList.Search(rows, "reuters"));
        }

        [Fact]
        public void Fix_list_search_ignores_the_current_culture()
        {
            var saved = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
                var rows = FixList.Build(Fixtures.SmallLists(), Level.Standard, null);
                Assert.Single(FixList.Search(rows, "TRACK.METRICS-THREE.IO"));
                Assert.Single(FixList.Search(rows, "ANALYTICS"));
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void Profile_paths_are_masked()
        {
            Assert.Equal(@"backup: %USERPROFILE%\x and %USERPROFILE%\y",
                Diagnostics.MaskPath(@"backup: C:\Users\Someone\x and c:\users\someone\y", @"C:\Users\Someone", "%USERPROFILE%"));
            string profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            Assert.DoesNotContain(profile, Diagnostics.MaskProfilePath("at " + profile + @"\AppData"));
            Assert.Equal("", Diagnostics.MaskProfilePath(null));
            Assert.False(string.IsNullOrEmpty(Diagnostics.WindowsBuild()));
        }

        [Fact]
        public void Run_location_tips()
        {
            using (var dir = new TempDir())
            {
                string downloads = dir.Combine("Downloads");
                Directory.CreateDirectory(Path.Combine(downloads, "sub"));
                string other = dir.Combine("Tools");
                Directory.CreateDirectory(other);
                string fakeTemp = dir.Combine("Temp");
                Directory.CreateDirectory(fakeTemp);

                Assert.Equal(RunLocationTips.RunningFromDownloads, RunLocation.Check(Path.Combine(downloads, "QuietHosts.exe"), downloads, fakeTemp));
                Assert.Equal(RunLocationTips.RunningFromDownloads, RunLocation.Check(Path.Combine(downloads, "sub", "QuietHosts.exe"), downloads, fakeTemp));
                Assert.Equal(RunLocationTips.None, RunLocation.Check(Path.Combine(other, "QuietHosts.exe"), downloads, fakeTemp));
                Assert.Equal(RunLocationTips.None, RunLocation.Check(dir.Combine("DownloadsX", "QuietHosts.exe"), downloads, fakeTemp));
                Assert.Equal(RunLocationTips.RunningFromTemp, RunLocation.Check(Path.Combine(fakeTemp, "x", "QuietHosts.exe"), downloads, fakeTemp));
                Assert.Equal(RunLocationTips.None, RunLocation.Check(Path.Combine(other, "QuietHosts.exe"), null, fakeTemp));
            }
            // The real temp folder counts as temp.
            Assert.True((RunLocation.Check(Path.Combine(Path.GetTempPath(), "x", "QuietHosts.exe")) & RunLocationTips.RunningFromTemp) != 0);
        }

        [Fact]
        public void Latin1_round_trips_every_byte()
        {
            byte[] all = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
            Assert.Equal(all, Latin1.Encode(Latin1.Decode(all)));
            Assert.Throws<System.ArgumentException>(() => Latin1.Encode("x" + (char)0x0100));
        }

        [Fact]
        public void Sha256_hex_is_lowercase()
        {
            Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.Sha256Hex(new byte[0]));
            Assert.True(Hashing.IsSha256Hex(Hashing.Sha256Hex("abc")));
            Assert.False(Hashing.IsSha256Hex("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855"));
        }
    }
}
