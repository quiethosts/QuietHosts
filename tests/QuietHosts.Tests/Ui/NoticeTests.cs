using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>The notice catalog (spec 3.5.1-3.5.3) as a pure function: which notices show, in which order, with which action.</summary>
    public class NoticeBuilderTests
    {
        private static readonly BlockLists Lists = BlockLists.LoadEmbedded();

        private static IReadOnlyList<Notice> Build(PreviewContext p, HostsStatus s, RunLocationTips tips = RunLocationTips.None, NoticeDismissals? d = null) =>
            NoticeBuilder.Build(s, new UiContext(p.Env, p.Lists, p.Context.ExePath, tips, false, true), d ?? NoticeDismissals.Empty);

        private static Notice Only(IReadOnlyList<Notice> notices, NoticeId id, NoticeSeverity severity, string text, bool closable, string? fingerprint, NoticeAction action)
        {
            Notice n = Assert.Single(notices);
            Assert.Equal(id, n.Id);
            Assert.Equal(severity, n.Severity);
            Assert.Equal(text, n.Message);
            Assert.Equal(closable, n.Closable);
            Assert.Equal(fingerprint, n.Fingerprint);
            Assert.Equal(action, n.Action);
            Assert.Equal(Notice.LabelFor(action), n.ActionLabel);
            Assert.Equal(action == NoticeAction.Repair || action == NoticeAction.UseNewerList || action == NoticeAction.RemoveOlderList, n.ActionElevates);
            Assert.Equal(action == NoticeAction.GetNewest, n.ActionExternal);
            return n;
        }

        // ---------------------------------------------------------------- T-B-02: each flag and tip alone

        [Fact]
        public void Each_flag_and_tip_alone_gives_its_notice()
        {
            using (var p = new PreviewContext())
            {
                BlockLists l = p.Lists;
                string hash = new string('a', 64);
                Assert.Empty(Build(p, UiScenarios.Status(l, ProtectionState.On)));

                // N1: Repair while On (the block at its level) and Off (clean the damaged lines), nothing in the other states.
                foreach ((ProtectionState state, NoticeAction action) in new[]
                {
                    (ProtectionState.On, NoticeAction.Repair), (ProtectionState.Off, NoticeAction.Repair),
                    (ProtectionState.Removed, NoticeAction.None), (ProtectionState.OlderList, NoticeAction.None),
                })
                    Only(Build(p, UiScenarios.Status(l, state, warnings: HostsWarnings.ChangeNotFinished)), NoticeId.ChangeNotFinished, NoticeSeverity.Warning,
                        Strings.WarningChangeNotFinished, false, null, action);

                Only(Build(p, UiScenarios.Status(l, ProtectionState.On), RunLocationTips.RunningFromTemp), NoticeId.RunningFromTemp, NoticeSeverity.Warning,
                    Strings.TipRunningFromTemp, false, null, NoticeAction.None);

                string newer = UiScenarios.NewerListVersion(l);
                Only(Build(p, UiScenarios.Status(l, ProtectionState.On, blockListVersion: newer)), NoticeId.NewerListInFile, NoticeSeverity.Warning,
                    Strings.NoteNewerListInFile(newer), false, null, NoticeAction.GetNewest);

                Only(Build(p, UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock)), NoticeId.EditedBlock, NoticeSeverity.Warning,
                    Strings.WarningEditedBlock, true, UiScenarios.FakeBlockLinesSha256, NoticeAction.Repair);

                HostsStatus ms = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside);
                Assert.NotNull(ms.MicrosoftLinesSha256);
                Only(Build(p, ms), NoticeId.MicrosoftLines, NoticeSeverity.Warning, Strings.WarningMicrosoftLinesOutside, true, ms.MicrosoftLinesSha256, NoticeAction.ShowLines);

                Only(Build(p, UiScenarios.Status(l, ProtectionState.On, blockListVersion: UiScenarios.OlderListVersion)), NoticeId.NewerListInApp, NoticeSeverity.Info,
                    Strings.TipNewerListInApp(l.Version), true, "app:" + AppInfo.Version + "|lists:" + l.Version, NoticeAction.UseNewerList);

                // N7: "Remove it now" only while On.
                Only(Build(p, UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.LegacyBlockToo, legacy: true)), NoticeId.LegacyBlock, NoticeSeverity.Info,
                    Strings.WarningLegacyBlockToo, true, hash, NoticeAction.RemoveOlderList);
                Only(Build(p, UiScenarios.Status(l, ProtectionState.Removed, warnings: HostsWarnings.LegacyBlockToo, legacy: true)), NoticeId.LegacyBlock, NoticeSeverity.Info,
                    Strings.WarningLegacyBlockToo, true, hash, NoticeAction.None);

                foreach ((ProtectionState state, NoticeAction action) in new[]
                {
                    (ProtectionState.On, NoticeAction.Repair), (ProtectionState.Off, NoticeAction.Repair), (ProtectionState.Removed, NoticeAction.None),
                })
                    Only(Build(p, UiScenarios.Status(l, state, warnings: HostsWarnings.MalformedMarkers)), NoticeId.MalformedMarkers, NoticeSeverity.Info,
                        Strings.WarningMalformedMarkers, true, hash, action);

                HostsStatus large = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.LargeFile);
                Only(Build(p, large), NoticeId.LargeFile, NoticeSeverity.Info, Strings.WarningLargeFile(large.BytesOutsideBlocks), true,
                    "size:" + large.BytesOutsideBlocks.ToString(System.Globalization.CultureInfo.InvariantCulture), NoticeAction.None);

                string folder = "path:" + Hashing.Sha256Hex(Encoding.UTF8.GetBytes(Path.GetFullPath(Path.GetDirectoryName(p.Context.ExePath)!).ToLowerInvariant())).Substring(0, 16);
                Notice tip = Only(Build(p, UiScenarios.Status(l, ProtectionState.On), RunLocationTips.RunningFromDownloads), NoticeId.DownloadsTip, NoticeSeverity.Tip,
                    Strings.TipRunningFromDownloads, true, folder, NoticeAction.Why);
                // The fingerprint is a hash, never the path itself; "Tip: " isn't doubled in the bar's name.
                Assert.DoesNotContain("Apps", tip.Fingerprint, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(Strings.TipRunningFromDownloads, tip.AccessibleName);

                // Every fingerprint makes an entry the per-user store keeps.
                foreach (string entry in new[]
                {
                    "downloads-tip=" + tip.Fingerprint, "edited-block=" + UiScenarios.FakeBlockLinesSha256, "microsoft-lines=" + ms.MicrosoftLinesSha256,
                    "newer-list-in-app=" + NoticeBuilder.AppFingerprint(l), "legacy-block=" + hash, "malformed-markers=" + hash, "large-file=size:1480000",
                })
                    Assert.Equal(new[] { entry }, NoticeDismissalText.Clean(new[] { entry }));
            }
        }

        // ---------------------------------------------------------------- T-B-03: order and tiers

        [Fact]
        public void Notices_come_in_tier_and_table_order()
        {
            using (var p = new PreviewContext())
            {
                HostsWarnings every = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
                    HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;
                IReadOnlyList<Notice> all = Build(p, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: every, legacy: true, blockListVersion: UiScenarios.OlderListVersion),
                    RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp);
                Assert.Equal(new[]
                {
                    NoticeId.ChangeNotFinished, NoticeId.RunningFromTemp, NoticeId.EditedBlock, NoticeId.MicrosoftLines,
                    NoticeId.NewerListInApp, NoticeId.LegacyBlock, NoticeId.MalformedMarkers, NoticeId.LargeFile, NoticeId.DownloadsTip,
                }, all.Select(n => n.Id));
                // Warning, then Info, then Tip.
                Assert.Equal(new[]
                {
                    NoticeSeverity.Warning, NoticeSeverity.Warning, NoticeSeverity.Warning, NoticeSeverity.Warning,
                    NoticeSeverity.Info, NoticeSeverity.Info, NoticeSeverity.Info, NoticeSeverity.Info, NoticeSeverity.Tip,
                }, all.Select(n => n.Severity));

                IReadOnlyList<Notice> newerInFile = Build(p, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock,
                    blockListVersion: UiScenarios.NewerListVersion(p.Lists)), RunLocationTips.RunningFromTemp);
                Assert.Equal(new[] { NoticeId.RunningFromTemp, NoticeId.NewerListInFile, NoticeId.EditedBlock }, newerInFile.Select(n => n.Id));
            }
        }

        // ---------------------------------------------------------------- T-B-04: one action that changes the file at a time

        [Fact]
        public void One_repair_and_one_file_changing_action_at_a_time()
        {
            using (var p = new PreviewContext())
            {
                BlockLists l = p.Lists;
                Dictionary<NoticeId, NoticeAction> Actions(HostsStatus s, NoticeDismissals? d = null) =>
                    Build(p, s, RunLocationTips.None, d).ToDictionary(n => n.Id, n => n.Action);

                // Repair: N1 before N4 before N8.
                Dictionary<NoticeId, NoticeAction> a = Actions(UiScenarios.Status(l, ProtectionState.On,
                    warnings: HostsWarnings.ChangeNotFinished | HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.ChangeNotFinished]);
                Assert.Equal(NoticeAction.None, a[NoticeId.EditedBlock]);
                Assert.Equal(NoticeAction.None, a[NoticeId.MalformedMarkers]);
                a = Actions(UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.EditedBlock]);
                Assert.Equal(NoticeAction.None, a[NoticeId.MalformedMarkers]);
                // Off: N1's Repair cleans; the damaged markers' notice has none then (main-notice-repair-off).
                a = Actions(UiScenarios.Status(l, ProtectionState.Off, warnings: HostsWarnings.ChangeNotFinished | HostsWarnings.MalformedMarkers));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.ChangeNotFinished]);
                Assert.Equal(NoticeAction.None, a[NoticeId.MalformedMarkers]);

                // While On: Repair, then Use newer list, then Remove it now (D16; main-one-fix-action).
                a = Actions(UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.LegacyBlockToo, legacy: true,
                    blockListVersion: UiScenarios.OlderListVersion));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.EditedBlock]);
                Assert.Equal(NoticeAction.None, a[NoticeId.NewerListInApp]);
                Assert.Equal(NoticeAction.None, a[NoticeId.LegacyBlock]);
                a = Actions(UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.LegacyBlockToo, legacy: true, blockListVersion: UiScenarios.OlderListVersion));
                Assert.Equal(NoticeAction.UseNewerList, a[NoticeId.NewerListInApp]);
                Assert.Equal(NoticeAction.None, a[NoticeId.LegacyBlock]);
                a = Actions(UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.LegacyBlockToo | HostsWarnings.MalformedMarkers, legacy: true));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.MalformedMarkers]);
                Assert.Equal(NoticeAction.None, a[NoticeId.LegacyBlock]);

                // A closed notice gives its action to the next one that shows.
                HostsStatus both = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers);
                a = Actions(both, NoticeDismissals.Empty.With(NoticeId.EditedBlock, UiScenarios.FakeBlockLinesSha256));
                Assert.False(a.ContainsKey(NoticeId.EditedBlock));
                Assert.Equal(NoticeAction.Repair, a[NoticeId.MalformedMarkers]);

                // Never more than one elevating action, in any combination of these flags while On.
                HostsWarnings[] flags = { HostsWarnings.ChangeNotFinished, HostsWarnings.EditedBlock, HostsWarnings.MalformedMarkers, HostsWarnings.LegacyBlockToo };
                for (int mask = 0; mask < 1 << (flags.Length + 1); mask++)
                {
                    HostsWarnings w = HostsWarnings.None;
                    for (int i = 0; i < flags.Length; i++) if ((mask & (1 << i)) != 0) w |= flags[i];
                    bool older = (mask & (1 << flags.Length)) != 0;
                    IReadOnlyList<Notice> notices = Build(p, UiScenarios.Status(l, ProtectionState.On, warnings: w, legacy: (w & HostsWarnings.LegacyBlockToo) != 0,
                        blockListVersion: older ? UiScenarios.OlderListVersion : null));
                    // Exactly one whenever any of these notices shows: each of them is fixed by the same "on".
                    Assert.True(notices.Count == 0 ? notices.Count(n => n.ActionElevates) == 0 : notices.Count(n => n.ActionElevates) == 1,
                        "not exactly one action that changes the file for " + w + (older ? " and an older list" : ""));
                }
            }
        }

        // ---------------------------------------------------------------- T-B-05 (pure part): what a closed notice hides

        [Fact]
        public void A_closed_notice_stays_hidden_until_its_condition_changes()
        {
            using (var p = new PreviewContext())
            {
                BlockLists l = p.Lists;
                HostsStatus ms = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside);
                NoticeDismissals d = NoticeDismissals.Empty.With(NoticeId.MicrosoftLines, ms.MicrosoftLinesSha256!);
                Assert.Equal(new[] { "microsoft-lines=" + ms.MicrosoftLinesSha256 }, d.Entries);
                Assert.Empty(Build(p, ms, RunLocationTips.None, d));
                HostsStatus other = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside, microsoftLines: new[] { "0.0.0.0 other.example.microsoft.com" });
                Assert.Single(Build(p, other, RunLocationTips.None, d));

                // Large file: hidden until the part from other programs is more than 1.5 times the size it was closed at.
                HostsStatus Large(long bytes) => new HostsStatus { State = ProtectionState.On, Warnings = HostsWarnings.LargeFile, BytesOutsideBlocks = bytes };
                NoticeDismissals closed = NoticeDismissals.Empty.With(NoticeId.LargeFile, NoticeBuilder.SizeFingerprint(1_000_000));
                Assert.Empty(Build(p, Large(1_000_000), RunLocationTips.None, closed));
                Assert.Empty(Build(p, Large(1_500_000), RunLocationTips.None, closed));
                Assert.Single(Build(p, Large(1_500_001), RunLocationTips.None, closed));
                Assert.Empty(Build(p, Large(400_000), RunLocationTips.None, closed));

                // Downloads tip: per folder.
                var tips = new UiContext(p.Env, l, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.RunningFromDownloads, false, true);
                var moved = new UiContext(p.Env, l, @"C:\Apps\Other\QuietHosts.exe", RunLocationTips.RunningFromDownloads, false, true);
                NoticeDismissals tip = NoticeDismissals.Empty.With(NoticeId.DownloadsTip, NoticeBuilder.FolderFingerprint(tips.ExePath));
                Assert.Empty(NoticeBuilder.Build(UiScenarios.Status(l, ProtectionState.On), tips, tip));
                Assert.Single(NoticeBuilder.Build(UiScenarios.Status(l, ProtectionState.On), moved, tip));
                Assert.Equal(NoticeBuilder.FolderFingerprint(@"C:\APPS\QuietHosts\QuietHosts.exe"), NoticeBuilder.FolderFingerprint(tips.ExePath));

                // Notices that can't be closed ignore a stored entry; unknown ids and malformed entries are dropped.
                NoticeDismissals stored = NoticeDismissals.From(new[] { "future-notice=abc", "edited-block=" + UiScenarios.FakeBlockLinesSha256, "large-file=size:12\n", "bad entry" });
                Assert.Equal(new[] { "edited-block=" + UiScenarios.FakeBlockLinesSha256 }, stored.Entries);
                Assert.Empty(Build(p, UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.EditedBlock), RunLocationTips.None, stored));
                Assert.Null(NoticeDismissals.StoredId(NoticeId.ChangeNotFinished));
                Assert.Same(NoticeDismissals.Empty, NoticeDismissals.Empty.With(NoticeId.ChangeNotFinished, "x"));
                // A new close of the same notice replaces the old one.
                Assert.Equal(new[] { "edited-block=new" }, stored.With(NoticeId.EditedBlock, "new").Entries);
            }
        }
    }

    /// <summary>The notices area in the main window (spec 3.5.2-3.5.6), the stored closes, and the Microsoft-lines dialog (6.6).</summary>
    [Collection(WindowCollection.Name)]
    public class NoticeTests
    {
        private static readonly HostsWarnings Every = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
            HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;

        // ---------------------------------------------------------------- T-B-03: at most three bars, then "Show N more notices"

        [Fact]
        public void At_most_three_bars_show_until_show_more_is_clicked()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: Every, legacy: true, blockListVersion: UiScenarios.OlderListVersion);
                    var c = new AppController(p.Context, s, Flow.Returns(ResultKind.Success), () => s);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        NoticeArea area = f.PreviewNoticeArea;
                        Assert.Equal(9, area.Bars.Count);
                        Assert.Equal(3, area.VisibleCount);
                        Assert.Equal(3, area.Bars.Count(b => Flow.Shows(f, b)));
                        Assert.True(Flow.Shows(f, area.ShowMoreLink));
                        Assert.Equal("Sh&ow 6 more notices", area.ShowMoreLink.Text);

                        // The result bar counts: it comes first and pushes one more behind the link.
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.Same(area.ResultBar, area.Bars[0]);
                        Assert.Equal(3, area.VisibleCount);
                        Assert.Equal("Sh&ow 7 more notices", area.ShowMoreLink.Text);

                        // "Show N more notices": every bar, no link, and the focus on the first newly shown bar.
                        NoticeBar fourth = area.Bars[3];
                        area.ShowMoreLink.Focus();
                        f.PreviewExpandNotices();
                        Assert.Equal(10, area.VisibleCount);
                        Assert.All(area.Bars, b => Assert.True(Flow.Shows(f, b)));
                        Assert.False(Flow.Shows(f, area.ShowMoreLink));
                        Assert.True(NoticeArea.FirstFocusable(fourth)!.Focused);

                        // It stays expanded across a re-read.
                        Task run = Flow.Start(f, () => c.RefreshAsync());
                        Flow.PumpUntil(() => run.IsCompleted);
                        Assert.True(area.Expanded);
                        Assert.Equal(area.Bars.Count, area.VisibleCount);
                        Assert.False(Flow.Shows(f, area.ShowMoreLink));
                        f.Close();
                    }

                    // One more: the singular.
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On,
                        warnings: HostsWarnings.EditedBlock | HostsWarnings.LargeFile)), showFirstRun: false))
                    {
                        Assert.Equal(4, f.PreviewNoticeArea.Bars.Count);
                        Assert.Equal(Strings.ShowMoreNotices(1), f.PreviewNoticeArea.ShowMoreLink.Text);
                        Assert.Equal("Sh&ow 1 more notice", Strings.ShowMoreNotices(1));
                    }
                });
            }
        }

        [Fact]
        public void Show_more_notices_fits_the_window_to_its_screen_again()
        {
            // The page grew but nothing refitted the window: it grew past its screen, with the footer row below its visible
            // bottom and no scroll bar (a11y-2).
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: Every, legacy: true, blockListVersion: UiScenarios.OlderListVersion);
                    UiScale sc = UiScale.Current;
                    int margin = sc.Px(Metrics.MainHeightMargin);
                    int collapsed;
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Assert.False(f.ContentScrolls);
                        collapsed = f.Height;
                        f.Close();
                    }
                    // A screen on which the page with three bars fits, but not with all nine.
                    var area = new Rectangle(0, 0, sc.Px(1366), collapsed + margin + sc.Px(40));
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false) { PreviewWorkingArea = area, PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        Assert.False(f.ContentScrolls);
                        NoticeBar fourth = f.PreviewNoticeArea.Bars[3];
                        f.PreviewExpandNotices();
                        Application.DoEvents();
                        Assert.True(f.ContentScrolls, "the expanded page doesn't scroll");
                        Assert.True(f.Height <= area.Height - margin, "the window is " + f.Height + " px tall on a " + area.Height + " px screen");
                        Assert.True(f.PreviewFooter.ShowDivider);
                        foreach (Control b in new Control[] { f.PreviewFixButton, f.PreviewUndoButton, f.PreviewAboutButton })
                        {
                            Rectangle r = f.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
                            Assert.True(r.Top >= 0 && r.Bottom <= f.ClientSize.Height, b.Text + " is outside the window");
                        }
                        // The focus is on the fourth bar, scrolled into view.
                        Control target = NoticeArea.FirstFocusable(fourth)!;
                        Assert.True(target.Focused);
                        ScrollHost host = f.PreviewHost;
                        Rectangle shown = host.RectangleToClient(target.RectangleToScreen(target.ClientRectangle));
                        Assert.True(shown.Top >= 0 && shown.Bottom <= host.ClientSize.Height, "the focused bar isn't scrolled into view");
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void A_notice_closed_while_a_change_runs_goes_at_once()
        {
            // While a change ran a closed notice stayed on screen (only its dismissal was stored) until the change ended
            // (behavior-2): it goes at once; the other bars stay as they are, their file-changing actions waiting (3.5.4).
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.MicrosoftLinesOutside | HostsWarnings.LargeFile);
                    var c = new AppController(p.Context, s);
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        NoticeArea area = f.PreviewNoticeArea;
                        NoticeBar edited = area.BarFor(NoticeId.EditedBlock)!;
                        NoticeBar lines = area.BarFor(NoticeId.MicrosoftLines)!;
                        NoticeBar large = area.BarFor(NoticeId.LargeFile)!;
                        Assert.True(Flow.Shows(f, large));
                        c.PreviewBusy(Strings.ChangingHostsFile);
                        Assert.False(edited.Action!.Enabled);
                        large.CloseButton!.Focus();
                        large.CloseButton.PerformClick();
                        Assert.Null(area.BarFor(NoticeId.LargeFile));
                        Assert.False(!large.IsDisposed && Flow.Shows(f, large), "the closed bar still shows");
                        Assert.NotNull(c.Dismissals.Get(NoticeId.LargeFile));
                        Assert.DoesNotContain(NoticeBar.NameFor(NoticeSeverity.Info, Strings.WarningLargeFile(s.BytesOutsideBlocks)), f.PreviewStatusCard.MainButton.AccessibleDescription);
                        // The others: the same bars, the Repair still waiting; the focus on the bar before the closed one.
                        Assert.Same(edited, area.BarFor(NoticeId.EditedBlock));
                        Assert.Same(lines, area.BarFor(NoticeId.MicrosoftLines));
                        Assert.False(edited.Action!.Enabled);
                        Assert.True(NoticeArea.FirstFocusable(lines)!.Focused);
                        c.PreviewClearBusy();
                        Assert.Null(area.BarFor(NoticeId.LargeFile));
                        Assert.True(edited.Action!.Enabled);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-05: closing stores the condition, per user

        [Fact]
        public void Closing_a_notice_hides_it_and_stores_its_condition()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                BlockLists lists = BlockLists.LoadEmbedded();
                // Not a preview: the window stores what it closes in this test environment's in-memory per-user store.
                var ctx = new UiContext(env, lists, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                // An entry of a later version: ignored, and dropped at the next write.
                env.User.SetDismissedNotices(new[] { "future-notice=abc" });
                Sta.Run(() =>
                {
                    HostsStatus ms = UiScenarios.Status(lists, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside | HostsWarnings.LargeFile);
                    HostsStatus next = ms;
                    var c = new AppController(ctx, ms, Flow.Returns(ResultKind.Success), () => next);
                    Assert.True(c.Dismissals.IsEmpty);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        NoticeBar bar = f.PreviewNoticeArea.BarFor(NoticeId.MicrosoftLines)!;
                        NoticeBar large = f.PreviewNoticeArea.BarFor(NoticeId.LargeFile)!;
                        bar.CloseButton!.Focus();
                        bar.CloseButton.PerformClick();
                        Assert.Null(f.PreviewNoticeArea.BarFor(NoticeId.MicrosoftLines));
                        Assert.True(bar.IsDisposed);
                        Assert.Equal(new[] { "microsoft-lines=" + ms.MicrosoftLinesSha256 }, env.User.Read().DismissedNotices);
                        // The focus moved to the next bar.
                        Assert.True(large.CloseButton!.Focused);

                        // Other lines: it shows again after the next read.
                        next = UiScenarios.Status(lists, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside | HostsWarnings.LargeFile,
                            microsoftLines: new[] { "0.0.0.0 another.example.microsoft.com" });
                        Task run = Flow.Start(f, () => c.RefreshAsync());
                        Flow.PumpUntil(() => run.IsCompleted);
                        Assert.NotNull(f.PreviewNoticeArea.BarFor(NoticeId.MicrosoftLines));

                        // A successful Undo forgets every close and stores none afterwards.
                        Task undo = Flow.Start(f, () => c.RunAsync(ElevatedCommand.Undo(), ChangeKind.Undo, f));
                        Flow.PumpUntil(() => undo.IsCompleted);
                        Assert.True(c.UserSettingsDeleted);
                        Assert.True(c.Dismissals.IsEmpty);
                        f.PreviewNoticeArea.BarFor(NoticeId.LargeFile)!.CloseButton!.PerformClick();
                        Assert.Null(f.PreviewNoticeArea.BarFor(NoticeId.LargeFile));
                        Assert.Empty(env.User.Read().DismissedNotices);
                        f.Close();
                    }

                    // A new window reads the stored closes.
                    env.User.SetDismissedNotices(new[] { "future-notice=abc", "large-file=size:" + UiScenarios.FakeLargeOutside.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                    var again = new AppController(ctx, ms);
                    Assert.Equal(new[] { "large-file=size:1480000" }, again.Dismissals.Entries);
                    using (var f = new MainForm(again, showFirstRun: false))
                    {
                        Assert.Null(f.PreviewNoticeArea.BarFor(NoticeId.LargeFile));
                        Assert.NotNull(f.PreviewNoticeArea.BarFor(NoticeId.MicrosoftLines));
                    }
                });
            }

            // A preview stores nothing.
            using (var p = new PreviewContext())
            {
                var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock));
                c.DismissNotice(NoticeId.EditedBlock, UiScenarios.FakeBlockLinesSha256);
                Assert.Equal(new[] { "edited-block=" + UiScenarios.FakeBlockLinesSha256 }, c.Dismissals.Entries);
                Assert.Empty(p.Env.User.Read().DismissedNotices);
            }
        }

        // ---------------------------------------------------------------- T-B-06: never during a change

        [Fact]
        public void Notices_wait_while_a_change_runs_and_the_result_bar_goes_when_one_starts()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus plain = UiScenarios.Status(p.Lists, ProtectionState.On);
                    HostsStatus edited = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock);
                    var c = new AppController(p.Context, plain, Flow.Returns(ResultKind.Success), () => plain);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.NotNull(f.PreviewNoticeArea.ResultBar);

                        c.PreviewBusy(Strings.WaitingForPermission);
                        Assert.DoesNotContain(f.PreviewNoticeArea.Bars, b => Flow.Shows(f, b));
                        // A status read while the change runs: the bars wait for its end.
                        c.SetStatus(edited);
                        Assert.Null(f.PreviewNoticeArea.BarFor(NoticeId.EditedBlock));
                        Assert.Equal(Strings.Headline(ProtectionState.On, RefusalReason.None), f.PreviewStatusCard.Headline.Text);
                        c.PreviewClearBusy();
                        Assert.NotNull(f.PreviewNoticeArea.BarFor(NoticeId.EditedBlock));

                        // A manual re-read clears the result bar.
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.NotNull(f.PreviewNoticeArea.ResultBar);
                        Task run = Flow.Start(f, () => c.RefreshAsync());
                        Flow.PumpUntil(() => run.IsCompleted);
                        Assert.Null(c.LastResult);
                        Assert.Null(f.PreviewNoticeArea.ResultBar);

                        // The next change's start removes it too.
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Task change = Flow.Start(f, () => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, f));
                        Assert.True(c.LastResult == null || !ReferenceEquals(c.LastResult.Text, Strings.ResultTurnOnOk));
                        Flow.PumpUntil(() => change.IsCompleted);
                        Assert.Equal(Strings.ResultTurnOffOk, c.LastResult!.Text);
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void The_notice_actions_run_their_change_or_open_their_page()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads))
            {
                Sta.Run(() =>
                {
                    BlockLists l = p.Lists;
                    foreach ((HostsStatus s, NoticeId id, ChangeKind kind, Verb verb) in new[]
                    {
                        (UiScenarios.Status(l, ProtectionState.On, Level.Strict, warnings: HostsWarnings.EditedBlock), NoticeId.EditedBlock, ChangeKind.Repair, Verb.On),
                        (UiScenarios.Status(l, ProtectionState.Off, warnings: HostsWarnings.ChangeNotFinished), NoticeId.ChangeNotFinished, ChangeKind.Repair, Verb.Off),
                        (UiScenarios.Status(l, ProtectionState.On, Level.Strict, blockListVersion: UiScenarios.OlderListVersion), NoticeId.NewerListInApp, ChangeKind.UpdateList, Verb.On),
                        (UiScenarios.Status(l, ProtectionState.On, Level.Strict, warnings: HostsWarnings.LegacyBlockToo, legacy: true), NoticeId.LegacyBlock, ChangeKind.RemoveOlderList, Verb.On),
                    })
                    {
                        var commands = new List<ElevatedCommand>();
                        var c = new AppController(p.Context, s, Flow.Returns(ResultKind.Success, commands), () => s);
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            Flow.Click(f, (Button)f.PreviewNoticeArea.BarFor(id)!.Action!);
                            Flow.PumpUntil(() => c.LastResult != null && !c.IsBusy);
                            ElevatedCommand cmd = Assert.Single(commands);
                            Assert.Equal(verb, cmd.Verb);
                            if (verb == Verb.On) Assert.Equal(Level.Strict, cmd.Level);
                            Assert.Equal(kind, c.LastResult!.Kind);
                            f.Close();
                        }
                    }

                    // The links: the releases page, How it works at "Keep QuietHosts.exe in a folder of its own", the lines.
                    HostsStatus links = UiScenarios.Status(l, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside, blockListVersion: UiScenarios.NewerListVersion(l));
                    using (var f = new MainForm(new AppController(p.Context, links), showFirstRun: false))
                    using (var watch = new DialogWatch(d => DialogResult.Cancel))
                    {
                        watch.Describe = d => d is HowItWorksDialog how ? "how:" + how.Start : d.GetType().Name;
                        Sta.ShowOffScreen(f);
                        f.PreviewExpandNotices();
                        var opened = new List<string>();
                        f.PreviewOpenUrl = opened.Add;
                        ((ThemedLink)f.PreviewNoticeArea.BarFor(NoticeId.NewerListInFile)!.Action!).PerformClick();
                        Assert.Equal(new[] { Strings.ReleasesUrl }, opened);
                        ((ThemedLink)f.PreviewNoticeArea.BarFor(NoticeId.DownloadsTip)!.Action!).PerformClick();
                        ((ThemedLink)f.PreviewNoticeArea.BarFor(NoticeId.MicrosoftLines)!.Action!).PerformClick();
                        Assert.Equal(new[] { "how:" + HowItWorksSection.OwnFolder, nameof(MicrosoftLinesDialog) }, watch.Answered);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-19: the Microsoft-lines dialog

        [Fact]
        public void The_microsoft_lines_box_shows_a_scroll_bar_only_where_the_lines_overflow()
        {
            // Three short lines sat between two empty, disabled scroll bars (visual-8).
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    string longLine = "0.0.0.0 " + string.Join(".", Enumerable.Repeat("very-long-telemetry-endpoint-name", 4)) + ".microsoft.com";
                    foreach ((IEnumerable<string>? lines, ScrollBars expected) in new (IEnumerable<string>?, ScrollBars)[]
                    {
                        (null, ScrollBars.None),
                        (UiScenarios.ManyMicrosoftLines(60), ScrollBars.Vertical),
                        (new[] { longLine, "0.0.0.0 a.microsoft.com" }, ScrollBars.Horizontal),
                        (Enumerable.Repeat(longLine, 30), ScrollBars.Both),
                    })
                    {
                        HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside, microsoftLines: lines);
                        using (var d = new MicrosoftLinesDialog(p.Context, s))
                        {
                            Sta.ShowOffScreen(d);
                            Assert.Equal(expected, d.Lines.TextBox.ScrollBars);
                            Assert.Equal(string.Join("\r\n", s.MicrosoftLines), d.Lines.TextBox.Text);
                            d.Close();
                        }
                    }
                });
            }
        }

        [Fact]
        public void The_microsoft_lines_dialog_shows_and_copies_exactly_the_lines()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus many = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside, microsoftLines: UiScenarios.ManyMicrosoftLines(60));
                    Assert.Equal(50, many.MicrosoftLines.Count);
                    using (var d = new MicrosoftLinesDialog(p.Context, many))
                    {
                        Sta.ShowOffScreen(d);
                        string shown = string.Join("\r\n", many.MicrosoftLines);
                        Assert.Equal(shown, d.Lines.TextBox.Text);
                        Assert.True(d.Lines.TextBox.ReadOnly);
                        Assert.False(d.Lines.TextBox.WordWrap);
                        // 50 lines don't fit the box: a vertical scroll bar; they are narrower than it: no horizontal one.
                        Assert.Equal(ScrollBars.Vertical, d.Lines.TextBox.ScrollBars);
                        Assert.Equal(Strings.MicrosoftLinesTitle, d.Lines.TextBox.AccessibleName);
                        Assert.True(Flow.Shows(d, d.More));
                        Assert.Equal("And 10 more lines.", d.More.Text);
                        Assert.Equal(Strings.MicrosoftLinesTitle, d.Text);
                        Assert.Equal(AccessibleRole.Dialog, d.AccessibleRole);
                        Assert.Equal(UiScale.Current.Px(Metrics.MicrosoftLinesDialogWidth), d.ClientSize.Width);

                        // Close is the default and Esc, and has the first focus with the message.
                        Assert.Same(d.CloseControl, d.AcceptButton);
                        Assert.Same(d.CloseControl, d.CancelButton);
                        Assert.Same(d.CloseControl, d.ActiveControl);
                        Assert.Equal(ButtonStyle.Accent, d.CloseControl.Style);
                        Assert.Equal(Strings.MicrosoftLinesTitle + ". " + Strings.MicrosoftLinesIntro, d.CloseControl.AccessibleDescription);

                        // Copy lines: exactly the shown lines, and the line under the box says so (announced).
                        var heard = new List<string>();
                        global::QuietHosts.Ui.AnnouncementSink = (c, t, k) => heard.Add(t);
                        try
                        {
                            string? copied = null;
                            d.PreviewClipboard = text => { copied = text; return true; };
                            d.CopyButton.PerformClick();
                            Assert.Equal(shown, copied);
                            Assert.True(Flow.Shows(d, d.Feedback));
                            Assert.Equal(Strings.LinesCopied, d.Feedback.Text);
                            d.PreviewClipboard = text => false;
                            d.CopyButton.PerformClick();
                            Assert.Equal(Strings.LinesNotCopied, d.Feedback.Text);
                            Assert.Equal(new[] { Strings.LinesCopied, Strings.LinesNotCopied }, heard);
                        }
                        finally
                        {
                            global::QuietHosts.Ui.AnnouncementSink = null;
                        }

                        // How to remove them: the README's steps (a preview starts nothing).
                        string? opened = null;
                        d.PreviewOpenUrl = url => opened = url;
                        d.RemoveLink.PerformClick();
                        Assert.Equal(Strings.DefenderHelpUrl, opened);
                        Assert.True(d.RemoveLink.External);
                        d.PreviewOpenUrl = null;
                        d.RemoveLink.PerformClick();
                        d.Close();
                    }

                    // Three lines: no "more" line.
                    using (var d = new MicrosoftLinesDialog(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.MicrosoftLinesOutside)))
                    {
                        Assert.Equal(string.Join("\r\n", UiScenarios.FakeMicrosoftLines), d.Lines.TextBox.Text);
                        Assert.False(ControlTree.IsMeantToShow(d.More, d));
                    }
                });
            }
            Assert.Equal("And 1 more line.", Strings.MicrosoftLinesMore(1));
            Assert.Equal("And 1,200 more lines.", Strings.MicrosoftLinesMore(1200));
        }
    }
}
