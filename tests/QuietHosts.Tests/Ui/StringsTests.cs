using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>Every value the window words has text, and the format helpers are culture-free.</summary>
    public class StringsTests
    {
        public static IEnumerable<object[]> States() => Enum.GetValues(typeof(ProtectionState)).Cast<object>().Select(v => new[] { v });
        public static IEnumerable<object[]> Refusals() => Enum.GetValues(typeof(RefusalReason)).Cast<object>().Select(v => new[] { v });
        public static IEnumerable<object[]> Results() => Enum.GetValues(typeof(ResultKind)).Cast<object>().Select(v => new[] { v });
        public static IEnumerable<object[]> Categories() => QuietHosts.Core.Categories.Keys.Select(k => new object[] { k });

        public static IEnumerable<object[]> WarningFlags() =>
            Enum.GetValues(typeof(HostsWarnings)).Cast<HostsWarnings>().Where(w => w != HostsWarnings.None).Select(w => new object[] { w });

        public static IEnumerable<object[]> TipFlags() =>
            Enum.GetValues(typeof(RunLocationTips)).Cast<RunLocationTips>().Where(t => t != RunLocationTips.None).Select(t => new object[] { t });

        private static HostsStatus StatusFor(ProtectionState state, RefusalReason refusal = RefusalReason.None) => new HostsStatus
        {
            State = state,
            Refusal = state == ProtectionState.Refused ? refusal : RefusalReason.None,
            BlockedCount = state == ProtectionState.On ? 1035 : 0,
            BlockLevel = state == ProtectionState.On ? Level.Strict : (Level?)null,
            LegacyCount = 412,
            MovedFolder = refusal == RefusalReason.HostsPathMoved ? @"D:\Elsewhere" : null,
            Allowlist = new[] { "a.ads-one.net", "b.ads-two.com" },
        };

        private static void AssertText(string? text)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("{", text);
            Assert.DoesNotContain("}", text);
        }

        [Theory]
        [MemberData(nameof(States))]
        public void Every_state_has_a_headline_detail_and_button(ProtectionState state)
        {
            foreach (RefusalReason refusal in Enum.GetValues(typeof(RefusalReason)))
            {
                HostsStatus s = StatusFor(state, refusal);
                AssertText(Strings.Headline(state, refusal));
                AssertText(Strings.Detail(s));
                AssertText(Strings.MainButton(state));
                Assert.NotNull(ControlTree.AccessKey(Strings.MainButton(state)));
            }
        }

        [Theory]
        [MemberData(nameof(Refusals))]
        public void Every_refusal_has_a_detail(RefusalReason refusal)
        {
            AssertText(Strings.RefusedDetail(refusal, @"D:\Elsewhere"));
            AssertText(Strings.Headline(ProtectionState.Refused, refusal));
        }

        [Fact]
        public void The_refused_states_have_their_own_texts()
        {
            // Over 16 MB or a moved hosts path: QuietHosts' own safety rule; an encoding it can't write: an inability.
            Assert.Equal("QuietHosts won't change your hosts file", Strings.Headline(ProtectionState.Refused, RefusalReason.HostsPathMoved));
            Assert.Equal("QuietHosts won't change your hosts file", Strings.Headline(ProtectionState.Refused, RefusalReason.TooLarge));
            Assert.Equal("QuietHosts can't change your hosts file", Strings.Headline(ProtectionState.Refused, RefusalReason.UnsupportedEncoding));
            Assert.Equal("QuietHosts can't tell what's in your hosts file", Strings.Headline(ProtectionState.Refused, RefusalReason.None));
            Assert.Equal("Can't read your hosts file", Strings.Headline(ProtectionState.CantRead, RefusalReason.None));
            Assert.Contains(@"(D:\Elsewhere)", Strings.Detail(StatusFor(ProtectionState.Refused, RefusalReason.HostsPathMoved)));
            Assert.Equal("It's saved in a format QuietHosts can't change safely, such as UTF-16. Save it as UTF-8, then select Check again.",
                Strings.Detail(StatusFor(ProtectionState.Refused, RefusalReason.UnsupportedEncoding)));
            Assert.Equal(Strings.DetailUnknown, Strings.Detail(StatusFor(ProtectionState.Refused, RefusalReason.None)));
            Assert.Equal("Chec&k again", Strings.MainButton(ProtectionState.Refused));
            Assert.Equal("&Try again", Strings.MainButton(ProtectionState.CantRead));
            Assert.Equal("&How to do this", Strings.HowToSaveAsUtf8Link);
        }

        [Fact]
        public void The_on_detail_has_the_count_level_and_allowed_addresses()
        {
            Assert.Equal("Blocking 1,035 ad and tracker addresses with Strict (you allowed 2). It stays on after you close this window or restart.",
                Strings.Detail(StatusFor(ProtectionState.On)));
            Assert.Equal("Blocking 1 ad and tracker address with Standard. It stays on after you close this window or restart.",
                Strings.DetailOn(1, Level.Standard, 0));
        }

        [Fact]
        public void The_main_window_texts_follow_the_spec()
        {
            Assert.Equal("Ads and trackers aren't blocked.", Strings.Detail(StatusFor(ProtectionState.Off)));
            Assert.Equal("You turned it off to test a site. Turn it on again when you're done.", Strings.Detail(StatusFor(ProtectionState.Off), offForTest: true));
            // "Off after a test" only says so while protection is off.
            Assert.Equal(Strings.Detail(StatusFor(ProtectionState.On)), Strings.Detail(StatusFor(ProtectionState.On), offForTest: true));
            Assert.Equal("&Turn on protection", Strings.MainButton(ProtectionState.Off));
            Assert.Equal("&Turn on protection", Strings.MainButton(ProtectionState.OlderList));
            Assert.Equal("&Turn off protection", Strings.MainButton(ProtectionState.On));
            Assert.Equal("&Turn on again", Strings.MainButton(ProtectionState.Removed));
            Assert.Equal("Nothing was changed.", Strings.ResultDeclined);
            Assert.Equal("Everything QuietHosts added is removed. To uninstall, delete QuietHosts.exe.", Strings.ResultUndoOk);
            Assert.Equal("QuietHosts was already changing the hosts file. Try again in a moment.", Strings.ResultBusy);
            Assert.Equal("&Fix a site or app\u2026", Strings.FixButton);
            Assert.Equal("Undo &all changes\u2026", Strings.UndoButton);
            Assert.Equal("Check for a &newer version", Strings.CheckForNewerVersionLink);
            Assert.Equal("Sh&ow 2 more notices", Strings.ShowMoreNotices(2));
            Assert.Equal("Sh&ow 1 more notice", Strings.ShowMoreNotices(1));
            Assert.Equal("Tip: QuietHosts is safer in a folder of its own than in Downloads.", Strings.TipRunningFromDownloads);
            Assert.Equal("Your hosts file is large (1.4 MB), mostly from other programs. Very large hosts files can slow Windows down.", Strings.WarningLargeFile(1_480_000));
            foreach (string label in new[] { Strings.RepairButton, Strings.UseNewerListButton, Strings.RemoveOlderListButton, Strings.ShowLinesLink, Strings.GetNewestLink,
                Strings.WhyLink, Strings.UseStrictCardButton, Strings.UseStandardCardButton, Strings.MainButtonTurnOff, Strings.FirstRunTurnOnButton, Strings.CopyLinesButton,
                Strings.FixSwitchToStandardButton, Strings.FixTurnOffButton, Strings.FixTurnOnButton, Strings.FixTurnOnProtectionButton, Strings.FixReplaceOlderListButton,
                Strings.AllowButton, Strings.BlockAgainButton, Strings.UseStrictButton, Strings.RemoveEverythingButton, Strings.TryAgainButton, Strings.ReportOnGitHubButton,
                Strings.CopyButton, Strings.CancelButton, Strings.CloseButton })
                Assert.True(Strings.WithoutAccessKey(label).Length <= 26, label + " is longer than 26 characters");
        }

        [Theory]
        [MemberData(nameof(WarningFlags))]
        public void Every_warning_has_text(HostsWarnings warning) => AssertText(Strings.Warning(warning, 300 * 1024));

        [Theory]
        [MemberData(nameof(TipFlags))]
        public void Every_tip_has_text(RunLocationTips tip) => AssertText(Strings.Tip(tip));

        [Theory]
        [MemberData(nameof(Results))]
        public void Every_result_kind_has_text(ResultKind kind)
        {
            AssertText(Strings.ErrorTitle(kind));
            AssertText(Strings.ErrorBody(kind, false));
            AssertText(Strings.ErrorBody(kind, true));
            AssertText(Strings.ErrorBody(kind, null));
            // The result bar, or an error dialog (no bar) for the kinds that open one.
            BlockLists lists = BlockLists.LoadEmbedded();
            var launch = new LaunchResult(kind, kind == ResultKind.Declined ? (int?)null : ExitCodes.FromResult(kind), null, new string('a', 64), new string('b', 64));
            ChangeResult? bar = AppController.ResultFor(ChangeKind.TurnOff, ElevatedCommand.Off(), launch, StatusFor(ProtectionState.Off), true, lists);
            bool dialog = kind == ResultKind.AccessDenied || kind == ResultKind.FileLocked || kind == ResultKind.Reverted || kind == ResultKind.Unexpected;
            if (dialog) Assert.Null(bar);
            else AssertText(bar!.Text);
        }

        [Fact]
        public void Unexpected_has_two_forms()
        {
            Assert.Equal(Strings.UnexpectedUnchanged, Strings.ErrorBody(ResultKind.Unexpected, false));
            Assert.Equal(Strings.UnexpectedMayBeIncomplete, Strings.ErrorBody(ResultKind.Unexpected, true));
            Assert.Equal(Strings.UnexpectedMayBeIncomplete, Strings.ErrorBody(ResultKind.Unexpected, null));
            Assert.Contains("wasn't changed", Strings.UnexpectedUnchanged);
            Assert.Contains("may be incomplete", Strings.UnexpectedMayBeIncomplete);
            // D9: Repair runs the same command again, and the body says so.
            Assert.Contains("Repair makes the same change again", Strings.UnexpectedMayBeIncomplete);
        }

        [Fact]
        public void Every_launch_stage_has_text()
        {
            foreach (LaunchStage stage in Enum.GetValues(typeof(LaunchStage))) AssertText(Strings.Progress(stage));
            Assert.Equal("Waiting for your permission. If you don't see the Windows prompt, check the taskbar.", Strings.Progress(LaunchStage.WaitingForPermission));
            Assert.Equal("Changing the hosts file\u2026", Strings.Progress(LaunchStage.Changing));
        }

        [Fact]
        public void Levels_and_tiers_use_the_window_words()
        {
            Assert.Equal("Standard", Strings.LevelName(Level.Standard));
            Assert.Equal("Strict", Strings.LevelName(Level.Strict));
            Assert.Equal("Standard", Strings.TierName(Tier.Default));
            Assert.Equal("Strict", Strings.TierName(Tier.Strict));
        }

        [Theory]
        [MemberData(nameof(Categories))]
        public void Every_category_key_has_ui_words(string key)
        {
            string words = Strings.CategoryWords(key);
            AssertText(words);
            if (key != QuietHosts.Core.Categories.Other) Assert.NotEqual(Strings.CategoryWords(QuietHosts.Core.Categories.Other), words);
        }

        [Fact]
        public void Category_words_follow_the_category_table()
        {
            Assert.Equal("Ad auction", Strings.CategoryWords("header-bidding"));
            Assert.Equal("\u201CAround the web\u201D ad box", Strings.CategoryWords("native-ads"));
            Assert.Equal("Ads or tracking", Strings.CategoryWords("adblock-tests"));
            Assert.Equal("Ads or tracking", Strings.CategoryWords(null));
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("tr-TR")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void Numbers_and_dates_ignore_the_culture(string culture)
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.Equal("1,035", Strings.Number(1035));
                Assert.Equal("0", Strings.Number(0));
                Assert.Equal("1,234,567", Strings.Number(1234567));
                Assert.Equal("20 Nov 2026", Strings.Date(new DateTime(2026, 11, 20)));
                Assert.Equal("1 Feb 2027", Strings.Date(new DateTime(2027, 2, 1)));
                Assert.Equal("20 Nov 2026", Strings.ListDate("2026-11-20"));
                Assert.Equal("1.5 MB", Strings.FileSize(1536 * 1024));
                Assert.Equal("Block list from 20 Nov 2026", Strings.BlockListFrom("2026-11-20"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void Counts_use_the_singular_for_one()
        {
            Assert.Equal("1 address", Strings.Addresses(1));
            Assert.Equal("0 addresses", Strings.Addresses(0));
            Assert.Equal("2 addresses", Strings.Addresses(2));
            Assert.Equal("1,035 addresses", Strings.Addresses(1035));
            Assert.Equal("Allowed 1 address. Restart your browser, then reload the page.", Strings.ResultAllowed(1));
            Assert.Equal("Allowed 3 addresses. Restart your browser, then reload the page.", Strings.ResultAllowed(3));
            Assert.Equal("Blocked 1 address again. Restart your browser to see the change.", Strings.ResultBlocked(1));
            Assert.Equal("Blocked 12 addresses again. Restart your browser to see the change.", Strings.ResultBlocked(12));
            Assert.Equal("Strict blocks 1 more ad and tracker address. It's for people who want the most blocking and don't mind fixing a site now and then. With Strict:",
                Strings.StrictIntro(1));
            Assert.StartsWith("Strict blocks 115 more ad and tracker addresses. ", Strings.StrictIntro(115), StringComparison.Ordinal);
            Assert.Equal("Your hosts file has 1 address from block-ads.ps1. Turning on replaces them with the QuietHosts list.", Strings.DetailOlderList(1));
            Assert.Equal("Your hosts file has 412 addresses from block-ads.ps1. Turning on replaces them with the QuietHosts list.", Strings.DetailOlderList(412));
        }

        [Fact]
        public void Sizes_and_list_dates_are_readable()
        {
            Assert.Equal("300 KB", Strings.FileSize(300 * 1024));
            Assert.Equal("257 KB", Strings.FileSize(256 * 1024 + 700));
            Assert.Equal("1.0 MB", Strings.FileSize(1000 * 1024));
            Assert.Equal("16.0 MB", Strings.FileSize(16L * 1024 * 1024));
            Assert.Equal(Strings.UnknownDate, Strings.ListDate(null));
            Assert.Equal(Strings.UnknownDate, Strings.ListDate(""));
            Assert.Equal("v7", Strings.ListDate("v7"));
        }

        [Theory]
        [InlineData("2026-11-20", true)]
        [InlineData("2024-02-29", true)]
        [InlineData("2026-02-29", false)]
        [InlineData("2026-13-01", false)]
        [InlineData("2026-1-20", false)]
        [InlineData("20261120", false)]
        [InlineData("2026-11-20 ", false)]
        [InlineData(" 2026-11-20", false)]
        [InlineData("1", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("٢٠٢٦-١١-٢٠", false)]
        public void Only_real_dates_are_list_versions(string? version, bool expected) => Assert.Equal(expected, Strings.IsListVersion(version));

        [Fact]
        public void The_list_version_lines_name_the_dates()
        {
            Assert.Equal("This copy of QuietHosts has a newer block list (20 Nov 2026).", Strings.TipNewerListInApp("2026-11-20"));
            Assert.Equal("Your hosts file has a newer block list (20 Jan 2027). Changes made with this copy put an older list back.",
                Strings.NoteNewerListInFile("2027-01-20"));
            Assert.Equal("Updated to the block list from 20 Nov 2026. Restart your browser to see the change.", Strings.ResultListUpdated("2026-11-20"));
        }

        [Fact]
        public void Strict_names_four_effects()
        {
            Assert.Equal(4, Strings.StrictEffects.Length);
            Assert.Equal("Links in ads, sponsored Google results and shopping emails may not open.", Strings.StrictEffects[0]);
            Assert.Equal("Some videos, \u201CRecommended stories\u201D boxes and product boxes may stay empty.", Strings.StrictEffects[1]);
            Assert.Equal("Some sites show \u201Cad blocker detected\u201D or have broken buttons, forms or checkout.", Strings.StrictEffects[2]);
            Assert.Equal("Some apps, developer tools and ad or analytics dashboards may show errors.", Strings.StrictEffects[3]);
            Assert.Equal("You can switch back to Standard at any time.", Strings.StrictOutro);
        }

        [Fact]
        public void The_lines_for_a_slow_run_and_for_another_run_say_what_happens()
        {
            Assert.Equal("This is taking longer than usual. If your antivirus asks about QuietHosts, that's this change.", Strings.ChangingSlow);
            Assert.Equal("QuietHosts is already changing the hosts file. This window updates when it's done.", Strings.OtherChangeRunning);
            Assert.Equal(Strings.ChangingHostsFile, Strings.Progress(LaunchStage.TakingLong));
        }

        [Fact]
        public void The_counts_in_the_level_texts_come_from_the_lists()
        {
            BlockLists lists = BlockLists.LoadEmbedded();
            // Strict's description ends with its count (the trade-off comes first); Standard's has no count (D19).
            Assert.EndsWith("Blocks " + Strings.Count(lists.StrictExtraCount, "more address", "more addresses") + ".", Strings.StrictDescription(lists.StrictExtraCount), StringComparison.Ordinal);
            Assert.Equal("Some sites may break. Blocks 1 more address.", Strings.StrictDescription(1));
            Assert.DoesNotContain(Strings.StandardDescription, char.IsDigit);
            Assert.Contains(Strings.AdAddresses(lists.StandardCount), Strings.FirstRunFact1Body(lists.StandardCount));
        }

        [Fact]
        public void Fix_dialog_lines()
        {
            Assert.Equal("Showing 12 of 1,035", Strings.FixShowing(12, 1035));
            Assert.Equal(" \u00B7 2 selected", Strings.FixSelectedSuffix(2));
            Assert.Equal(" \u00B7 1,200 selected", Strings.FixSelectedSuffix(1200));
            Assert.Equal("No blocked address matches \u201Creuters\u201D.", Strings.FixNoMatch("reuters"));
            Assert.Equal("Searching for a website? Test with protection off instead.", Strings.FixNoMatchHint);
            Assert.Equal("&Go to step 3", Strings.FixGoToStep(3));
            Assert.Equal("Select up to 100 addresses at a time.", Strings.FixTooMany(Limits.MaxHostsPerRun));
            Assert.Equal("Allowed addresses load in every browser and app on this PC.", Strings.FixConsequence);
            Assert.Equal("Select an address to see its known effects.", Strings.FixNoSelection);
            Assert.Equal("Show only addresses you a&llowed (3)", Strings.FixShowOnlyAllowed(3));
            Assert.Equal("a.example-ads.com: links won't open", Strings.FixRowToolTip("a.example-ads.com", "links won't open"));
            Assert.Equal("a.example-ads.com", Strings.FixRowToolTip("a.example-ads.com", ""));
            Assert.Equal("Known effects of a.example-ads.com (Example Ads, Strict): links won't open", Strings.FixRowDetails("a.example-ads.com", "Example Ads", "Strict", "links won't open"));
            Assert.Equal("Known effects of a.example-ads.com (Standard): None known", Strings.FixRowDetails("a.example-ads.com", "", "Standard", ""));
            Assert.Equal("Step 2: Try Standard", Strings.FixStepName(2, Strings.FixStep2Title));
            Assert.Equal("3", Strings.StepNumber(3));
            Assert.Equal("Steps: restart your browser; try Standard; test with protection off. Then search for an address to allow.",
                Strings.FixDescription(new[] { Strings.FixStepShortRestart, Strings.FixStepShortStandard, Strings.FixStepShortTest }, canAllow: true));
            Assert.Equal("Steps: restart your browser.", Strings.FixDescription(new[] { Strings.FixStepShortRestart }, canAllow: false));
            Assert.Equal("Fix a site or app that isn't working", Strings.FixTitle);
            Assert.Equal("Try these steps in order", Strings.FixIntro);
            Assert.Equal("&Search blocked addresses", Strings.FixSearchLabel);
            Assert.Equal("Site, company or address, for example yahoo", Strings.FixSearchHint);
            // D13 as the owner accepted it: with protection off QuietHosts can't break the page, after a browser restart.
            Assert.Equal("QuietHosts isn't blocking anything now. If the page still doesn't work after you restart your browser, QuietHosts isn't the cause.", Strings.FixStepOffBody);
        }

        [Fact]
        public void Undo_body_handles_counts_and_the_older_list()
        {
            Assert.Equal("This removes all 1,035 blocked addresses from the hosts file and forgets your level and allowed addresses. " +
                "Lines added by you or other programs stay as they are, and backups of the hosts file are kept.", Strings.UndoBody(1035, false));
            Assert.StartsWith("This removes 1 blocked address from the hosts file and forgets", Strings.UndoBody(1, false), StringComparison.Ordinal);
            Assert.EndsWith("The older block-ads.ps1 list is removed too.", Strings.UndoBody(1035, true), StringComparison.Ordinal);
            Assert.StartsWith("This forgets your level and allowed addresses. Lines added by you", Strings.UndoBody(0, false), StringComparison.Ordinal);
            Assert.Equal("Remove everything QuietHosts added?", Strings.UndoTitle);
        }

        [Fact]
        public void The_error_dialogs_say_what_happened_without_coaching()
        {
            Assert.Equal("The hosts file is in use", Strings.ErrorTitle(ResultKind.FileLocked));
            Assert.Equal("Your antivirus or Windows blocked the change", Strings.ErrorTitle(ResultKind.AccessDenied));
            Assert.Equal("The change was undone right away", Strings.ErrorTitle(ResultKind.Reverted));
            Assert.Equal("QuietHosts couldn't finish", Strings.ErrorTitle(ResultKind.Unexpected));
            Assert.Equal("Something went wrong", Strings.WindowErrorTitle);
            Assert.Equal("Nothing was changed. Some antivirus programs protect the hosts file. If yours asks about QuietHosts, that's this change.",
                Strings.ErrorBody(ResultKind.AccessDenied, false));
            Assert.StartsWith("Nothing was changed. Another program is using the hosts file.", Strings.ErrorBody(ResultKind.FileLocked, false), StringComparison.Ordinal);
            Assert.StartsWith("QuietHosts changed the hosts file, but another program put it back", Strings.ErrorBody(ResultKind.Reverted, true), StringComparison.Ordinal);
            // The report's ethics table: no "trusted programs" advice, no "allow the change" and no "click Yes" anywhere in the window.
            IEnumerable<string> bodies = Enum.GetValues(typeof(ResultKind)).Cast<ResultKind>().SelectMany(k => new[] { Strings.ErrorBody(k, false), Strings.ErrorBody(k, true) });
            foreach (string text in AllConstantTexts().Concat(bodies))
            {
                Assert.DoesNotContain("trusted program", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("allow the change", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("click yes", text, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal("QuietHosts couldn't open the link", Strings.LinkFailedTitle);
            Assert.Equal("Copy this address into your browser:", Strings.LinkFailedBody);
            Assert.Equal("QuietHosts couldn't open the folder", Strings.FolderFailedTitle);
            Assert.Equal("This is the folder:", Strings.FolderFailedBody);
            Assert.Equal("&Copy", Strings.CopyButton);
            Assert.Equal("Copied.", Strings.TextCopied);
            Assert.Equal("Error", Strings.ErrorIconName);
        }

        [Fact]
        public void About_and_how_it_works_follow_the_spec()
        {
            Assert.Equal("Version 1.2.3", Strings.AboutVersion("1.2.3"));
            Assert.Equal("From 20 Nov 2026: 807 addresses in Standard, 121 more in Strict.", Strings.AboutLists("2026-11-20", 807, 121));
            Assert.Equal("Free and open source under the MIT License. Copyright \u00A9 2026 QuietHosts contributors.", Strings.AboutLicense);
            Assert.Equal("Click Undo all changes, then delete QuietHosts.exe.", Strings.AboutUninstall);
            Assert.Equal("This copy is digitally signed by Example Publisher. Windows shows this name when it asks for your permission.", Strings.SignatureSigned("Example Publisher"));
            Assert.Contains("\u201CUnknown publisher\u201D", Strings.SignatureUnsigned);
            Assert.Equal("&How QuietHosts works", Strings.HowItWorksLink);
            Assert.Equal("If something goes &wrong", Strings.AboutHelpLink);
            // How it works keeps the first run's long "What it can't block" text (R5).
            Assert.StartsWith("YouTube video ads, Twitch stream ads and sponsored posts", Strings.HowCantBlockBody, StringComparison.Ordinal);
            Assert.Equal("Keep QuietHosts.exe in a folder of its own", Strings.HowFolderHeading);
            Assert.Contains("Say yes only if the prompt names QuietHosts and you just clicked a QuietHosts button.", Strings.HowAskBody);
            Assert.Equal("Click Undo all changes, then delete QuietHosts.exe. The backups stay next to your hosts file, in case you need them.", Strings.HowUninstallBody);
        }

        [Fact]
        public void Typographic_quotes_and_access_key_removal()
        {
            Assert.Equal("say \u201Chi\u201D and \u201Cbye\u201D", Strings.Typographic("say \"hi\" and \"bye\""));
            Assert.Equal("Turn on", Strings.WithoutAccessKey("&Turn on"));
            Assert.Equal("Q&A", Strings.WithoutAccessKey("Q&&A"));
        }

        [Fact]
        public void Links_point_at_the_repository()
        {
            Assert.Equal(AppInfo.RepositoryUrl + "/releases", Strings.ReleasesUrl);
            Assert.Equal(AppInfo.RepositoryUrl + "/issues/new?template=site-broke.yml", Strings.ReportSiteUrl);
            Assert.Equal(AppInfo.RepositoryUrl + "/issues/new?template=bug.yml", Strings.ReportBugUrl);
            Assert.Equal(AppInfo.RepositoryUrl + "#if-something-goes-wrong", Strings.HelpUrl);
            Assert.StartsWith("https://github.com/", Strings.ProjectUrl, StringComparison.Ordinal);
            Assert.Equal(AppInfo.RepositoryUrl + "#quiethosts-wont-change-your-hosts-file", Strings.Utf8HelpUrl);
            Assert.Equal(AppInfo.RepositoryUrl + "#windows-defender-reports-hostsfilehijack", Strings.DefenderHelpUrl);
            Assert.Equal(AppInfo.RepositoryUrl + "#verify-your-download", Strings.VerifyDownloadUrl);
        }

        /// <summary>Every string constant and every string a no-argument or simple helper returns.</summary>
        private static IEnumerable<string> AllConstantTexts()
        {
            foreach (FieldInfo f in typeof(Strings).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (f.FieldType == typeof(string)) yield return (string)f.GetValue(null)!;
                else if (f.FieldType == typeof(string[])) foreach (string s in (string[])f.GetValue(null)!) yield return s;
            }
        }

        [Fact]
        public void The_window_says_address_never_host()
        {
            // "hosts file" is the file's name; "QuietHosts" is the app. No other "host" in the window's words.
            var word = new Regex(@"\bhosts?\b(?! file)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (string text in AllConstantTexts().Where(t => !t.StartsWith("http", StringComparison.Ordinal)))
            {
                Assert.False(word.IsMatch(text), "Uses \"host\": " + text);
            }
        }

        [Fact]
        public void Texts_have_no_placeholders_paths_or_three_dots()
        {
            foreach (string text in AllConstantTexts())
            {
                Assert.DoesNotContain("{", text);
                Assert.DoesNotContain(@":\", text);
                Assert.DoesNotContain("...", text);
            }
        }

        [Fact]
        public void Access_keys_mark_a_letter()
        {
            foreach (string text in AllConstantTexts().Where(t => t.IndexOf('&') >= 0 && !t.StartsWith("http", StringComparison.Ordinal)))
            {
                char? key = ControlTree.AccessKey(text);
                Assert.True(key.HasValue && char.IsLetterOrDigit(key.Value), "Bad access key: " + text);
                Assert.Equal(text.Length - 1, Strings.WithoutAccessKey(text).Length);
            }
        }
    }
}
