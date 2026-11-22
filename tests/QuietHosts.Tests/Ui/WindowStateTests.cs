using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// What the main window and the Fix dialog say in the less common states (a file that can't be read, the older
    /// list, a block from another list version, a failed self-check), how they take Enter, and what screen readers get.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class WindowStateTests
    {
        private static readonly MethodInfo ProcessDialogKey = typeof(Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static List<string> ShownTexts(Form f) => ControlTree.All(f).Where(c => ControlTree.IsMeantToShow(c, f)).Select(c => c.Text).ToList();

        private static ListView TheList(Form f) => ControlTree.All(f).OfType<ListView>().Single();

        private static string StatusOf(ListView list, string host) => list.Items.Cast<ListViewItem>().Single(i => i.Text == host).SubItems[FixDialog.StatusColumn].Text;

        private static IEnumerable<string> Statuses(ListView list) => list.Items.Cast<ListViewItem>().Select(i => i.SubItems[FixDialog.StatusColumn].Text);

        // ---------------------------------------------------------------- Fix dialog: steps and the Status column

        [Theory]
        [InlineData(ProtectionState.CantRead, RefusalReason.None)]
        [InlineData(ProtectionState.Refused, RefusalReason.TooLarge)]
        [InlineData(ProtectionState.Refused, RefusalReason.UnsupportedEncoding)]
        [InlineData(ProtectionState.Refused, RefusalReason.HostsPathMoved)]
        public void When_the_file_cant_be_read_the_fix_dialog_doesnt_say_protection_is_off(ProtectionState state, RefusalReason refusal)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, state, refusal: refusal, allowed: 1);
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        ControlTree.CreateHandles(d);
                        List<string> shown = ShownTexts(d);
                        Assert.Contains(Strings.FixStepCantChangeTitle, shown);
                        Assert.Contains(Strings.FixStepCantChangeBody, shown);
                        Assert.DoesNotContain(Strings.FixStepOffTitle, shown);
                        Assert.DoesNotContain(Strings.FixTurnOnButton, shown);
                        Assert.DoesNotContain(Strings.FixTurnOffButton, shown);
                        Assert.Equal(Strings.FixCantChange, d.FooterText);
                        List<string> statuses = Statuses(TheList(d)).ToList();
                        Assert.Equal(Strings.StatusAllowed, statuses[0]);
                        Assert.All(statuses.Skip(1), t => Assert.Equal(Strings.StatusUnknown, t));
                    }
                });
            }
        }

        [Theory]
        [InlineData(ProtectionState.OlderList)]
        [InlineData(ProtectionState.Removed)]
        public void With_the_older_list_in_the_file_the_fix_dialog_offers_to_replace_it(ProtectionState state)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, state, legacy: true,
                        warnings: state == ProtectionState.Removed ? HostsWarnings.LegacyBlockToo : HostsWarnings.None);
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        ControlTree.CreateHandles(d);
                        List<string> shown = ShownTexts(d);
                        Assert.Contains(Strings.FixStepOlderListTitle, shown);
                        Assert.Contains(Strings.FixStepOlderListBody, shown);
                        Assert.Contains(Strings.FixReplaceOlderListButton, shown);
                        Assert.DoesNotContain(Strings.FixStepOffTitle, shown);
                        Assert.Equal(Strings.FixTurnOnToChange, d.FooterText);
                        // The older list may still block any of them.
                        Assert.All(Statuses(TheList(d)), t => Assert.Equal(Strings.StatusUnknown, t));
                    }
                });
            }
        }

        [Theory]
        [InlineData(ProtectionState.Off)]
        [InlineData(ProtectionState.Removed)]
        public void With_protection_off_no_row_says_blocked(ProtectionState state)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, state, allowed: 2);
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        ControlTree.CreateHandles(d);
                        List<string> shown = ShownTexts(d);
                        Assert.Contains(Strings.FixStepOffTitle, shown);
                        Assert.Contains(Strings.FixStepOffBody, shown);
                        // Removed: "Turn on again"; plain Off (not after a test): "Turn on protection".
                        Assert.Contains(state == ProtectionState.Removed ? Strings.FixTurnOnButton : Strings.FixTurnOnProtectionButton, shown);
                        List<string> statuses = Statuses(TheList(d)).ToList();
                        Assert.Equal(new[] { Strings.StatusAllowed, Strings.StatusAllowed }, statuses.Take(2));
                        Assert.All(statuses.Skip(2), t => Assert.Equal(Strings.StatusNotBlocked, t));
                    }
                });
            }
        }

        [Fact]
        public void The_status_column_follows_the_block_in_the_hosts_file()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    // A block written from another list: the first address of this exe's list isn't in it.
                    List<string> level = p.Lists.ForLevel(Level.Standard).Select(e => e.Host).OrderBy(h => h, StringComparer.Ordinal).ToList();
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, blockedHosts: level.Skip(1));
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.Equal(Strings.StatusNotBlocked, StatusOf(TheList(d), level[0]));
                        Assert.Equal(Strings.StatusBlocked, StatusOf(TheList(d), level[1]));
                    }
                    // With the older block-ads.ps1 list in the file too, an address QuietHosts' block lacks may be blocked there.
                    HostsStatus both = UiScenarios.Status(p.Lists, ProtectionState.On, blockedHosts: level.Skip(1), legacy: true, warnings: HostsWarnings.LegacyBlockToo);
                    using (var d = new FixDialog(new AppController(p.Context, both)))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.Equal(Strings.StatusUnknown, StatusOf(TheList(d), level[0]));
                        Assert.Equal(Strings.StatusBlocked, StatusOf(TheList(d), level[1]));
                    }
                });
            }
        }

        // ---------------------------------------------------------------- Fix dialog: whole notes, columns, Enter

        [Fact]
        public void The_columns_fill_the_list_and_the_selected_row_shows_its_whole_note()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        Sta.ShowOffScreen(d);
                        Assert.True(d.MaximizeBox);
                        ListView list = TheList(d);
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));

                        ListEntry entry = p.Lists.DefaultEntries.Where(e => e.Note.Length > 0).OrderByDescending(e => e.Note.Length).First();
                        d.PreviewSearch(entry.Host);
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));
                        // Rows, but none selected: the hint.
                        Assert.Equal(Strings.FixNoSelection, d.RowDetails);
                        d.PreviewSelectHost(entry.Host);
                        Assert.Equal(Strings.FixRowDetails(entry.Host, entry.Company, Strings.TierName(entry.Tier), entry.Note), d.RowDetails);
                        Assert.EndsWith(entry.Note, d.RowDetails, StringComparison.Ordinal);

                        d.PreviewSearch(string.Empty);
                        Assert.Equal(Strings.FixNoSelection, d.RowDetails);
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));

                        // The line names the company and the level, when the list knows the company.
                        ListEntry withCompany = p.Lists.DefaultEntries.First(e => e.Company.Length > 0 && e.Note.Length > 0);
                        d.PreviewSearch(withCompany.Host);
                        d.PreviewSelectHost(withCompany.Host);
                        Assert.Equal("Known effects of " + withCompany.Host + " (" + withCompany.Company + ", Standard): " + withCompany.Note, d.RowDetails);

                        // At the smallest size the columns still fill the list.
                        d.Size = d.MinimumSize;
                        Application.DoEvents();
                        Assert.Equal(list.ClientSize.Width, list.Columns.Cast<ColumnHeader>().Sum(c => c.Width));
                        d.Close();
                    }
                });
            }
            Assert.Equal("Known effects of a.example-ads.com (Standard): None known", Strings.FixRowDetails("a.example-ads.com", "", "Standard", ""));
            Assert.Equal("Known effects of a.example-ads.com (Example Ads, Strict): links won't open", Strings.FixRowDetails("a.example-ads.com", "Example Ads", "Strict", "links won't open"));
        }

        [Fact]
        public void Enter_never_switches_protection_or_the_level_from_the_fix_dialog()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict);
                    using (var d = new FixDialog(new AppController(p.Context, s)))
                    {
                        // A click (and so Enter on the default button) needs a visible window.
                        Sta.ShowOffScreen(d);
                        var clicks = new Dictionary<string, int>();
                        foreach (Button b in ControlTree.All(d).OfType<Button>()) b.Click += (o, e) => clicks[b.Text] = clicks.TryGetValue(b.Text, out int n) ? n + 1 : 1;

                        foreach (string text in new[] { Strings.FixTurnOffButton, Strings.FixSwitchToStandardButton })
                        {
                            d.ActiveControl = ControlTree.All(d).OfType<Button>().Single(b => b.Text == text);
                            // Shift+Enter too: a form clicks its focused button for both.
                            foreach (Keys keys in new[] { Keys.Enter, Keys.Enter | Keys.Shift })
                            {
                                Assert.True((bool)ProcessDialogKey.Invoke(d, new object[] { keys })!);
                                Assert.False(clicks.ContainsKey(text), keys + " clicked " + text);
                            }
                        }

                        // Enter on Allow still clicks it (the preview context starts nothing).
                        d.PreviewSelect(1);
                        d.ActiveControl = ControlTree.All(d).OfType<Button>().Single(b => b.Text == Strings.AllowButton);
                        ProcessDialogKey.Invoke(d, new object[] { Keys.Enter });
                        Assert.True(clicks.ContainsKey(Strings.AllowButton), "Enter didn't click Allow");
                        d.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- main window: the block list version

        [Fact]
        public void A_newer_list_in_this_copy_is_one_prompt_away()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: "2000-01-01"));
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        List<string> shown = ShownTexts(f);
                        Assert.Contains(Strings.TipNewerListInApp(p.Lists.Version), shown);
                        Assert.Contains(Strings.UseNewerListButton, shown);
                        Assert.Contains(Strings.BlockListFrom("2000-01-01"), shown);
                        // The notice's button: one Windows prompt (the shield), waiting while a change runs.
                        ThemedButton use = ControlTree.All(f).OfType<ThemedButton>().Single(b => b.Text == Strings.UseNewerListButton);
                        Assert.True(use.Shield);
                        Assert.True(use.Enabled);
                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.False(use.Enabled);
                    }
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                    {
                        Assert.DoesNotContain(Strings.UseNewerListButton, ShownTexts(f));
                    }
                });
            }
            BlockLists lists = BlockLists.LoadEmbedded();
            var updated = new LaunchResult(ResultKind.Success, 0, null, new string('a', 64), new string('b', 64));
            Assert.Equal(Strings.ResultListUpdated(lists.Version),
                AppController.ResultFor(ChangeKind.UpdateList, ElevatedCommand.On(Level.Strict), updated, UiScenarios.Status(lists, ProtectionState.On, Level.Strict), true, lists)!.Text);
            Assert.Equal("This copy of QuietHosts has a newer block list (20 Nov 2026).", Strings.TipNewerListInApp("2026-11-20"));
            Assert.Equal("Updated to the block list from 20 Nov 2026. Restart your browser to see the change.", Strings.ResultListUpdated("2026-11-20"));
        }

        [Fact]
        public void A_newer_list_in_the_hosts_file_gets_a_note()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    string newer = UiScenarios.NewerListVersion(p.Lists);
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: newer)), showFirstRun: false))
                    {
                        List<string> shown = ShownTexts(f);
                        Assert.Contains(Strings.NoteNewerListInFile(newer), shown);
                        Assert.Contains(Strings.BlockListFrom(newer), shown);
                        Assert.DoesNotContain(Strings.TipNewerListInApp(p.Lists.Version), shown);
                        Assert.DoesNotContain(Strings.UseNewerListButton, shown);
                        // Its link opens the releases page (external).
                        Assert.Contains(Strings.GetNewestLink, shown);
                        Assert.True(ControlTree.All(f).OfType<ThemedLink>().Single(l => l.Text == Strings.GetNewestLink).External);
                        Assert.Equal("Your hosts file has a newer block list (20 Jan 2027). Changes made with this copy put an older list back.", Strings.NoteNewerListInFile("2027-01-20"));
                    }
                });
            }
        }

        [Theory]
        [InlineData("1")]
        [InlineData("2026-13-01")]
        [InlineData("2026-11-20 restored by hand from another computer, please leave these lines exactly as they are, thank you")]
        public void A_list_version_in_the_header_that_isnt_a_date_is_ignored(string version)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    // Both on the same fixed screen, so only the header's text could change the width.
                    int width;
                    using (var normal = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false) { PreviewWorkingArea = Sta.LargeScreen })
                    {
                        ControlTree.CreateHandles(normal);
                        width = normal.Width;
                    }
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: version)), showFirstRun: false) { PreviewWorkingArea = Sta.LargeScreen })
                    {
                        ControlTree.CreateHandles(f);
                        List<string> shown = ShownTexts(f);
                        Assert.Contains(Strings.BlockListFrom(p.Lists.Version), shown);
                        Assert.DoesNotContain(shown, t => t.StartsWith("This copy of QuietHosts has a newer", StringComparison.Ordinal));
                        Assert.DoesNotContain(shown, t => t.StartsWith("Your hosts file has a newer", StringComparison.Ordinal));
                        Assert.Equal(width, f.Width);
                    }
                });
            }
        }

        // ---------------------------------------------------------------- main window: the self-check's details

        [Fact]
        public void A_failed_self_check_offers_its_details()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Assert.DoesNotContain(ControlTree.All(f), x => x.Text == Strings.CopySelfCheckDetailsLink);
                        c.PreviewSelfCheckFailed(new SelfCheckResult(false, "DnsQuery status 9701"), f);
                        // The link lives in the result bar, a warning.
                        NoticeBar bar = f.PreviewNoticeArea.ResultBar!;
                        Assert.Equal(NoticeSeverity.Warning, bar.Severity);
                        ThemedLink copy = Assert.IsType<ThemedLink>(bar.Action);
                        Assert.Equal(Strings.CopySelfCheckDetailsLink, copy.Text);
                        Assert.True(ControlTree.IsMeantToShow(copy, f));
                        string details = c.SelfCheckDetails();
                        Assert.Contains(Strings.DetailsSelfCheck + ": " + Strings.DetailsFailed + ": DnsQuery status 9701", details);
                        Assert.Contains(Strings.DetailsAction + ": on --level standard", details);
                        Assert.Contains(Strings.DetailsResult + ": Success", details);

                        // After a click: the feedback line under it (a preview copies nothing).
                        f.PreviewCopySelfCheckDetails();
                        Assert.Equal(Strings.DetailsCopied, bar.Feedback);

                        // Any other result hides it, and the feedback with it.
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.Null(bar.Action);
                        Assert.Null(bar.Feedback);
                        Assert.DoesNotContain(ControlTree.All(f), x => x.Text == Strings.CopySelfCheckDetailsLink);
                    }
                });
            }
        }

        // ---------------------------------------------------------------- screen readers

        [Fact]
        public void Dialogs_have_the_dialog_role_and_their_first_focus_carries_the_message()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                    using (var undo = new UndoDialog(on))
                    {
                        Assert.Equal(AccessibleRole.Dialog, undo.AccessibleRole);
                        string spoken = undo.ActiveControl!.AccessibleDescription;
                        // The heading keeps its own question mark, then the body (and the shield sentence of no button: Cancel has the focus).
                        Assert.Equal(Strings.UndoTitle + " " + Strings.UndoBody(on.BlockedCount, on.HasLegacyBlock), spoken);
                    }
                    using (var strict = new StrictDialog(p.Context, StrictDialogMode.Confirm, elevates: true))
                    {
                        Assert.Equal(AccessibleRole.Dialog, strict.AccessibleRole);
                        Assert.Same(strict.AcceptButton, strict.ActiveControl);
                        string spoken = strict.ActiveControl!.AccessibleDescription;
                        Assert.Contains(Strings.StrictIntro(p.Lists.StrictExtraCount), spoken);
                        foreach (string effect in Strings.StrictEffects) Assert.Contains(effect, spoken);
                    }
                    foreach (ResultKind kind in new[] { ResultKind.AccessDenied, ResultKind.FileLocked, ResultKind.Reverted, ResultKind.Unexpected })
                    {
                        using (var error = new ErrorDialog(p.Context, kind, false, () => "details"))
                        {
                            Assert.Equal(AccessibleRole.Dialog, error.AccessibleRole);
                            string spoken = error.ActiveControl!.AccessibleDescription;
                            Assert.StartsWith(Strings.ErrorTitle(kind) + ". " + Strings.ErrorBody(kind, false), spoken, StringComparison.Ordinal);
                        }
                    }
                    using (var how = new HowItWorksDialog(p.Context)) Assert.Equal(AccessibleRole.Dialog, how.AccessibleRole);
                    using (var fix = new FixDialog(new AppController(p.Context, on))) Assert.Equal(AccessibleRole.Dialog, fix.AccessibleRole);
                });
            }
        }

        [Fact]
        public void The_main_button_describes_every_warning_and_tip_shown()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    HostsWarnings all = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
                        HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: all, legacy: true, blockListVersion: "2000-01-01");
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false))
                    {
                        string spoken = ControlTree.All(f).OfType<Button>().Single(b => b.Text == Strings.MainButton(ProtectionState.On)).AccessibleDescription;
                        Assert.StartsWith(Strings.Headline(s.State, s.Refusal) + ". " + Strings.Detail(s), spoken, StringComparison.Ordinal);
                        // Every notice, shown or behind "Show N more notices", as its bar's name (prefix and text), then the shield sentence.
                        Assert.Equal(9, f.PreviewNotices.Count);
                        foreach (Notice n in f.PreviewNotices) Assert.Contains(n.AccessibleName, spoken);
                        foreach (HostsWarnings w in Enum.GetValues(typeof(HostsWarnings)))
                        {
                            if (w != HostsWarnings.None) Assert.Contains(Strings.Warning(w, s.BytesOutsideBlocks), spoken);
                        }
                        Assert.Contains(Strings.PrefixNote + Strings.TipNewerListInApp(p.Lists.Version), spoken);
                        Assert.Contains(Strings.PrefixWarning + Strings.WarningChangeNotFinished, spoken);
                        Assert.Contains(Strings.TipRunningFromDownloads, spoken);
                        Assert.DoesNotContain(Strings.PrefixTip + Strings.TipRunningFromDownloads, spoken);
                        Assert.Contains(Strings.TipRunningFromTemp, spoken);
                        Assert.EndsWith(" " + Strings.ShieldDescription, spoken, StringComparison.Ordinal);
                    }
                });
            }
        }
    }
}
