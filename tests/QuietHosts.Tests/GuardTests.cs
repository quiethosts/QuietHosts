using System.Linq;
using System.Text;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    public class GuardTests
    {
        private static Guard Embedded() => Guard.LoadEmbedded();

        [Theory]
        [InlineData("ib.adnxs.com")]                 // Microsoft's Xandr
        [InlineData("adnxs.com")]
        [InlineData("vortex.data.microsoft.com")]
        [InlineData("MICROSOFT.COM")]
        [InlineData("login.live.com")]
        [InlineData("raw.githubusercontent.com")]
        [InlineData("anything.microsoft")]           // Microsoft TLDs
        [InlineData("x.azure")]
        [InlineData("a.b.xbox")]
        public void Microsoft_names_are_guarded(string name)
        {
            Assert.Equal(GuardSource.Microsoft, Guard.BuiltIn.Classify(name));
            Assert.True(Guard.BuiltIn.IsGuarded(name));
            Assert.True(Guard.IsMicrosoft(name));
        }

        [Theory]
        [InlineData("notmicrosoft.com")]
        [InlineData("microsoft.com.evil.net")]
        [InlineData("mymsn.com")]
        [InlineData("adnxs.co")]
        [InlineData("microsoftx.com")]
        public void Label_boundaries_matter(string name)
        {
            Assert.False(Guard.IsMicrosoft(name));
            Assert.Equal(GuardSource.None, Guard.BuiltIn.Classify(name));
        }

        [Theory]
        [InlineData("printer.local")]
        [InlineData("my.test")]
        [InlineData("svc.internal")]
        [InlineData("abc.onion")]
        [InlineData("router.lan")]
        [InlineData("x.home")]
        [InlineData("a.localdomain")]
        [InlineData("b.example")]
        [InlineData("c.invalid")]
        [InlineData("1.0.0.127.in-addr.arpa")]
        [InlineData("localhost")]                    // single label
        [InlineData("localhost.localdomain")]
        [InlineData("127.0.0.1")]                    // IP literal
        [InlineData("not a host")]
        [InlineData("")]
        public void Reserved_names_are_guarded(string name)
        {
            Assert.Equal(GuardSource.Reserved, Guard.BuiltIn.Classify(name));
            Assert.True(Guard.IsReserved(name));
        }

        [Fact]
        public void Example_dot_com_is_not_reserved() => Assert.Equal(GuardSource.None, Guard.BuiltIn.Classify("ads.example.com"));

        [Fact]
        public void Googleapis_names_are_guarded_by_the_embedded_guard_list()
        {
            Guard g = Embedded();
            Assert.Equal(GuardSource.GuardSuffix, g.Classify("x.googleapis.com"));
            Assert.Equal(GuardSource.GuardSuffix, g.Classify("optimizationguide-pa.googleapis.com"));
            Assert.Equal(GuardSource.GuardSuffix, g.Classify("googleapis.com"));
        }

        [Fact]
        public void Exact_entries_guard_only_that_name()
        {
            Guard g = Embedded();
            Assert.Equal(GuardSource.GuardExact, g.Classify("www.google.com"));
            Assert.Equal(GuardSource.GuardExact, g.Classify("accounts.google.com"));
            Assert.Equal(GuardSource.None, g.Classify("ads.google.com"));
            Assert.Equal(GuardSource.None, g.Classify("x.www.google.com"));
        }

        [Fact]
        public void Guard_text_parses_suffixes_exact_names_and_comments()
        {
            Guard g = Guard.Parse("# comment\n\nexample-cdn.net\n=only.this-one.org\n  spaced.example.net  \r\n=  also.exact.io\nbad name\n=*.wild.com\n", out var issues);
            Assert.True(g.IsGuarded("img.example-cdn.net"));
            Assert.True(g.IsGuarded("example-cdn.net"));
            Assert.True(g.IsGuarded("only.this-one.org"));
            Assert.False(g.IsGuarded("sub.only.this-one.org"));
            Assert.True(g.IsGuarded("a.spaced.example.net"));
            Assert.True(g.IsGuarded("also.exact.io"));
            Assert.Equal(new[] { 7, 8 }, issues.Select(i => i.Line).ToArray());
            Assert.All(issues, i => Assert.Equal(ListIssueKind.InvalidHost, i.Kind));
        }

        [Fact]
        public void The_guard_list_cannot_remove_the_built_in_guard()
        {
            Guard g = Guard.Parse("=microsoft.com\n# nothing else\n", out _);
            Assert.True(g.IsGuarded("microsoft.com"));
            Assert.True(g.IsGuarded("www.microsoft.com"));
            Assert.True(g.IsGuarded("printer.local"));
        }

        [Fact]
        public void Guard_bytes_must_be_utf8_and_small()
        {
            Guard.Parse(new byte[] { 0x61, 0xFF, 0x2E, 0x63, 0x6F, 0x6D }, out var issues);
            Assert.Equal(ListIssueKind.InvalidUtf8, Assert.Single(issues).Kind);
            Guard.Parse(new byte[Limits.MaxListFileBytes + 1], out issues);
            Assert.Equal(ListIssueKind.FileTooLarge, Assert.Single(issues).Kind);
        }

        [Fact]
        public void The_embedded_guard_list_parses_without_issues()
        {
            Guard.LoadEmbedded(out var issues);
            Assert.Empty(issues);
        }

        [Fact]
        public void Microsoft_check_is_lenient_for_other_programs_lines()
        {
            Assert.True(Guard.IsMicrosoft("Settings-Win.Data.Microsoft.com."));
            Assert.True(Guard.IsMicrosoft("weird_name.microsoft.com"));
            Assert.False(Guard.IsMicrosoft("caf" + (char)0x00E9 + ".microsoft.com"));
            Assert.False(Guard.IsMicrosoft(null));
            Assert.False(Guard.IsMicrosoft(""));
        }

        [Theory]
        [InlineData("microsoft.com", true)]
        [InlineData("vortex.data.microsoft.com", true)]
        [InlineData("Settings-Win.Data.Microsoft.com.", true)]   // lenient, like IsMicrosoft
        [InlineData("windows.com", true)]
        [InlineData("windowsupdate.com", true)]
        [InlineData("login.live.com", true)]
        [InlineData("bing.com", true)]
        [InlineData("www.msftncsi.com", true)]
        [InlineData("x.login.windows.net", true)]
        [InlineData("anything.microsoft", true)]
        [InlineData("a.b.xbox", true)]
        [InlineData("ib.adnxs.com", false)]
        [InlineData("someone.github.io", false)]
        [InlineData("raw.githubusercontent.com", false)]
        [InlineData("phish.azurewebsites.net", false)]
        [InlineData("x.cloudapp.net", false)]
        [InlineData("dsfdsfxcfddsf.blob.core.windows.net", false)]
        [InlineData("etonynew345zzz.z13.web.core.windows.net", false)]
        [InlineData("windows.net", false)]
        [InlineData("analytics-listener.trafficmanager.net", false)]
        [InlineData("westus-0.in.applicationinsights.azure.com", false)]
        [InlineData("az361816.vo.msecnd.net", false)]
        [InlineData("px.ads.linkedin.com", false)]
        [InlineData("www.clarity.ms", false)]
        [InlineData("track.king.com", false)]
        [InlineData("notmicrosoft.com", false)]
        [InlineData(null, false)]
        public void Defender_sensitive_names_are_Microsofts_own_services(string? name, bool expected) =>
            Assert.Equal(expected, Guard.IsDefenderSensitive(name));

        [Fact]
        public void The_Defender_names_are_a_subset_of_the_Microsoft_guard()
        {
            // The warning never fires for a name QuietHosts itself could write.
            Assert.All(Guard.DefenderSuffixes, s =>
            {
                Assert.True(HostName.IsNormalized(s), s);
                Assert.Equal(GuardSource.Microsoft, Guard.BuiltIn.Classify(s));
                Assert.True(Guard.IsDefenderSensitive("x." + s), s);
            });
            Assert.All(Guard.MicrosoftTlds, tld => Assert.True(Guard.IsDefenderSensitive("x." + tld), tld));
            Assert.Equal(Guard.DefenderSuffixes.Count, Guard.DefenderSuffixes.Distinct().Count());
            // Names Defender is reported to flag that the guard doesn't cover stay out, unless the guard gets them first.
            foreach (string name in new[] { "wns.notify.windows.com.akadns.net", "urs.microsoft.com.nsatc.net", "vsgallery.com", "telemetry.remoteapp.windowsazure.com" })
                Assert.False(Guard.IsDefenderSensitive(name) && !Guard.IsMicrosoft(name), name);
        }

        [Fact]
        public void Hardcoded_lists_are_complete()
        {
            Assert.Contains("adnxs.com", Guard.MicrosoftSuffixes);
            Assert.Contains("githubusercontent.com", Guard.MicrosoftSuffixes);
            Assert.Equal(60, Guard.MicrosoftSuffixes.Count);
            Assert.Equal(new[] { "microsoft", "azure", "bing", "office", "windows", "xbox" }, Guard.MicrosoftTlds);
            Assert.Equal(11, Guard.ReservedTlds.Count);
            Assert.All(Guard.MicrosoftSuffixes, s => Assert.True(HostName.IsNormalized(s), s));
        }

        [Fact]
        public void Guard_text_tolerates_a_bom_free_utf8_file() =>
            Assert.True(Guard.Parse(Encoding.UTF8.GetBytes("example-cdn.net\n"), out _).IsGuarded("a.example-cdn.net"));
    }
}
