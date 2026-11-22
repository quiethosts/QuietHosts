using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>Message pumping, fake launches and dialog answers for the window tests.</summary>
    internal static class Flow
    {
        public static readonly string Hash = new string('a', 64);
        public static readonly string OtherHash = new string('b', 64);
        public static readonly MethodInfo ProcessDialogKey = typeof(Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        public static readonly MethodInfo ProcessCmdKey = typeof(Control).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

        /// <summary>Handles the window's messages until <paramref name="done"/> or the time is up.</summary>
        public static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        public static void PumpFor(int milliseconds) => PumpUntil(() => false, milliseconds);

        /// <summary>
        /// Runs <paramref name="action"/> from the window's message loop, as a click does: there WinForms' synchronization
        /// context brings every await back to the window's thread.
        /// </summary>
        public static Task Start(Form f, Func<Task> action)
        {
            Task? run = null;
            f.BeginInvoke(new Action(() => run = action()));
            PumpUntil(() => run != null);
            return run ?? throw new InvalidOperationException("The window didn't run it.");
        }

        /// <summary>Clicks a button from the window's message loop, as a user does.</summary>
        public static void Click(Form f, Button b) => f.BeginInvoke(new Action(b.PerformClick));

        /// <summary>A fake launch that ends in <paramref name="kind"/> on a worker thread and counts its calls.</summary>
        public static LaunchChange Returns(ResultKind kind, List<ElevatedCommand>? commands = null) => (command, progress, owner) =>
        {
            commands?.Add(command);
            return Task.Run(() => new LaunchResult(kind, kind == ResultKind.Declined ? (int?)null : ExitCodes.FromResult(kind), null, Hash, kind == ResultKind.Success ? OtherHash : Hash));
        };

        /// <summary>A key as the window gets it: ProcessCmdKey first, then ProcessDialogKey (what a form does with a key).</summary>
        public static void SendKey(Form f, Keys keys)
        {
            var msg = new Message { HWnd = f.Handle, Msg = 0x0100, WParam = (IntPtr)(int)(keys & Keys.KeyCode) };
            object[] args = { msg, keys };
            bool handled = (bool)ProcessCmdKey.Invoke(f, args)!;
            if (!handled) ProcessDialogKey.Invoke(f, new object[] { keys });
        }

        public static bool Enter(Form f, Keys keys) => (bool)ProcessDialogKey.Invoke(f, new object[] { keys })!;

        public static bool Shows(Form f, Control c) => !c.IsDisposed && c.FindForm() == f && ControlTree.IsMeantToShow(c, f);

        public static IEnumerable<string> ShownTexts(Form f) => ControlTree.All(f).Where(c => ControlTree.IsMeantToShow(c, f)).Select(c => c.Text);
    }

    /// <summary>
    /// Answers modal dialogs while it lives: a timer that ticks inside the dialog's own message loop finds each new modal
    /// form of this thread, records it and closes it with the answer (null leaves it open).
    /// </summary>
    internal sealed class DialogWatch : IDisposable
    {
        private readonly System.Windows.Forms.Timer _timer;

        public DialogWatch(Func<Form, DialogResult?> answer)
        {
            _timer = new System.Windows.Forms.Timer { Interval = 20 };
            _timer.Tick += (s, e) =>
            {
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList())
                {
                    if (!f.Modal || Seen.Contains(f)) continue;
                    Seen.Add(f);
                    Answered.Add(Describe?.Invoke(f) ?? f.GetType().Name);
                    DialogResult? r = answer(f);
                    if (r.HasValue) f.DialogResult = r.Value;
                }
            };
            _timer.Start();
        }

        /// <summary>The dialogs seen, in order.</summary>
        public List<Form> Seen { get; } = new List<Form>();

        /// <summary>What was noted about each dialog when it was seen (its type by default).</summary>
        public List<string> Answered { get; } = new List<string>();

        public Func<Form, string>? Describe { get; set; }

        public void Dispose() => _timer.Dispose();
    }

    /// <summary>
    /// The redesigned main window (spec 3): every state of the status card, the Enter and arrow-key rules, what a running
    /// change disables, where the focus goes after a change, what a declined prompt says, what screen readers hear, and the
    /// window's height on the common laptop screen (D15, D19).
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class MainWindowTests
    {
        // ---------------------------------------------------------------- T-B-01: the state table (3.2)

        public static IEnumerable<object[]> States() => new[]
        {
            new object[] { "S1", ProtectionState.Off, RefusalReason.None, false },
            new object[] { "S2", ProtectionState.Off, RefusalReason.None, true },
            new object[] { "S3", ProtectionState.On, RefusalReason.None, false },
            new object[] { "S4", ProtectionState.On, RefusalReason.None, false },
            new object[] { "S5", ProtectionState.OlderList, RefusalReason.None, false },
            new object[] { "S6", ProtectionState.Removed, RefusalReason.None, false },
            new object[] { "S7", ProtectionState.CantRead, RefusalReason.None, false },
            new object[] { "S8", ProtectionState.Refused, RefusalReason.TooLarge, false },
            new object[] { "S9", ProtectionState.Refused, RefusalReason.HostsPathMoved, false },
            new object[] { "S10", ProtectionState.Refused, RefusalReason.UnsupportedEncoding, false },
            new object[] { "S11", ProtectionState.Refused, RefusalReason.None, false },
        };

        private static HostsStatus StatusFor(BlockLists l, string row, ProtectionState state, RefusalReason refusal, bool runInProgress = false) =>
            row == "S4"
                ? UiScenarios.Status(l, state, Level.Strict, allowed: 3, runInProgress: runInProgress)
                : UiScenarios.Status(l, state, refusal: refusal, runInProgress: runInProgress);

        [Theory]
        [MemberData(nameof(States))]
        public void The_status_card_follows_the_state_table(string row, ProtectionState state, RefusalReason refusal, bool offForTest)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (bool otherRun in new[] { false, true })
                    {
                        HostsStatus s = StatusFor(p.Lists, row, state, refusal, otherRun);
                        var c = new AppController(p.Context, s);
                        if (offForTest) c.PreviewOffForTest();
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            StatusCard card = f.PreviewStatusCard;
                            string headline = Strings.Headline(state, refusal);
                            string detail = Strings.Detail(s, offForTest);
                            Assert.Equal(ShieldIcon.KindFor(state), card.Shield.Kind);
                            Assert.Equal(headline, card.Shield.AccessibleName);
                            Assert.Equal(headline, card.Headline.Text);
                            Assert.Equal(detail, card.Detail.Text);
                            Assert.Equal(Strings.MainButton(state), card.MainButton.Text);

                            bool changes = row != "S7" && row != "S8" && row != "S9" && row != "S10" && row != "S11";
                            bool accent = row == "S1" || row == "S2" || row == "S5" || row == "S6" || row == "S7";
                            Assert.Equal(accent ? ButtonStyle.Accent : ButtonStyle.Standard, card.MainButton.Style);
                            Assert.Equal(changes, card.MainButton.Shield);
                            Assert.True(card.MainButton.SwallowsEnter, row + ": Enter isn't swallowed");
                            Assert.Equal(row == "S10", ControlTree.IsMeantToShow(card.HowToLink, f));
                            // S1-S6 wait while another run holds the mutex; the re-read buttons don't.
                            Assert.Equal(!otherRun || !changes, card.MainButton.Enabled);

                            // Every button waits while this window's own change runs.
                            c.PreviewBusy(Strings.ChangingHostsFile);
                            Assert.False(card.MainButton.Enabled, row + ": the main button works while a change runs");
                            c.PreviewClearBusy();
                            Assert.Equal(!otherRun || !changes, card.MainButton.Enabled);
                        }
                    }
                });
            }
            Assert.Equal(new[] { "Protection is off", "Protection is on", "Protection was removed", "Older block list found", "Can't read your hosts file" },
                new[] { ProtectionState.Off, ProtectionState.On, ProtectionState.Removed, ProtectionState.OlderList, ProtectionState.CantRead }.Select(st => Strings.Headline(st, RefusalReason.None)));
        }

        [Fact]
        public void The_main_button_does_what_its_state_says()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach ((HostsStatus s, Verb? verb, Level? level) in new (HostsStatus, Verb?, Level?)[]
                    {
                        (UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict), Verb.Off, null),
                        (UiScenarios.Status(p.Lists, ProtectionState.Off, pendingLevel: Level.Strict), Verb.On, Level.Strict),
                        (UiScenarios.Status(p.Lists, ProtectionState.OlderList), Verb.On, Level.Standard),
                        (UiScenarios.Status(p.Lists, ProtectionState.Removed), Verb.On, Level.Standard),
                        (UiScenarios.Status(p.Lists, ProtectionState.CantRead), null, null),
                        (UiScenarios.Status(p.Lists, ProtectionState.Refused, refusal: RefusalReason.TooLarge), null, null),
                    })
                    {
                        var commands = new List<ElevatedCommand>();
                        int reads = 0;
                        var c = new AppController(p.Context, s, Flow.Returns(ResultKind.Declined, commands), () => { reads++; return s; });
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            Flow.Click(f, f.PreviewStatusCard.MainButton);
                            Flow.PumpUntil(() => reads > 0 && !c.IsBusy);
                            if (verb == null)
                            {
                                // Try again and Check again only read the file again: no prompt.
                                Assert.Empty(commands);
                                Assert.Equal(1, reads);
                            }
                            else
                            {
                                Assert.Equal(verb, Assert.Single(commands).Verb);
                                Assert.Equal(level, commands[0].Level);
                            }
                            f.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-10 (G2): Enter never switches protection or the level

        [Fact]
        public void Enter_never_switches_protection_or_the_level()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var cases = new List<(Func<MainForm> Create, Func<MainForm, Button> Button, string What)>();
                    foreach (object[] row in States())
                    {
                        string name = (string)row[0];
                        HostsStatus s = StatusFor(p.Lists, name, (ProtectionState)row[1], (RefusalReason)row[2]);
                        cases.Add((() => new MainForm(new AppController(p.Context, s), false), f => f.PreviewStatusCard.MainButton, name + " main button"));
                    }
                    HostsStatus onStandard = UiScenarios.Status(p.Lists, ProtectionState.On);
                    HostsStatus onStrict = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict);
                    cases.Add((() => new MainForm(new AppController(p.Context, onStandard), false), f => f.PreviewStrictCard.Button, "Use Strict…"));
                    cases.Add((() => new MainForm(new AppController(p.Context, onStrict), false), f => f.PreviewStandardCard.Button, "Use Standard"));
                    cases.Add((() => new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.ChangeNotFinished)), false),
                        f => (Button)f.PreviewNoticeArea.BarFor(NoticeId.ChangeNotFinished)!.Action!, "Repair"));
                    cases.Add((() => new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: UiScenarios.OlderListVersion)), false),
                        f => (Button)f.PreviewNoticeArea.BarFor(NoticeId.NewerListInApp)!.Action!, "Use newer list"));
                    cases.Add((() => new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.LegacyBlockToo, legacy: true)), false),
                        f => (Button)f.PreviewNoticeArea.BarFor(NoticeId.LegacyBlock)!.Action!, "Remove it now"));
                    cases.Add((() => new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false)), true),
                        f => f.PreviewFirstRun!.TurnOnButton, "first run's Turn on protection"));

                    foreach ((Func<MainForm> create, Func<MainForm, Button> find, string what) in cases)
                    {
                        // Space on "Use Strict…" opens the confirmation: answer it with Cancel.
                        using (var watch = new DialogWatch(d => DialogResult.Cancel))
                        using (MainForm f = create())
                        {
                            // A click (and so Enter on the focused button) needs a visible window.
                            Sta.ShowOffScreen(f);
                            Button button = find(f);
                            Assert.True(button.Enabled && ControlTree.IsMeantToShow(button, f), what + " isn't shown");
                            int clicks = 0;
                            button.Click += (s, e) => clicks++;
                            f.ActiveControl = button;
                            foreach (Keys keys in new[] { Keys.Enter, Keys.Enter | Keys.Shift })
                            {
                                Assert.True(Flow.Enter(f, keys), keys + " wasn't swallowed on " + what);
                                Assert.True(clicks == 0, keys + " clicked " + what);
                            }
                            Assert.Null(f.AcceptButton);
                            // Space still clicks it.
                            button.PerformClick();
                            Assert.Equal(1, clicks);
                            f.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-11 (G1): arrow keys never cause a prompt or a dialog

        [Fact]
        public void Arrow_keys_never_cause_a_prompt_or_a_dialog()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    foreach (HostsStatus s in new[]
                    {
                        UiScenarios.Status(p.Lists, ProtectionState.On),
                        UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict),
                        UiScenarios.Status(p.Lists, ProtectionState.Off),
                    })
                    {
                        int launches = 0;
                        var c = new AppController(p.Context, s, (command, progress, owner) =>
                        {
                            launches++;
                            return Task.FromResult(new LaunchResult(ResultKind.Declined, null, null, Flow.Hash, Flow.Hash));
                        });
                        using (var f = new MainForm(c, showFirstRun: false))
                        using (var watch = new DialogWatch(d => DialogResult.Cancel))
                        {
                            Sta.ShowOffScreen(f);
                            foreach (Func<Control> start in new Func<Control>[] { () => f.PreviewStandardCard.Button, () => f.PreviewStrictCard.Button, () => f.PreviewStatusCard.MainButton })
                            {
                                Control first = start();
                                if (!first.CanFocus) continue;
                                foreach (Keys key in new[] { Keys.Up, Keys.Down, Keys.Left, Keys.Right })
                                {
                                    first.Focus();
                                    Flow.SendKey(f, key);
                                    Flow.PumpFor(75);
                                }
                            }
                            Flow.PumpFor(300);
                            Assert.Equal(0, launches);
                            Assert.Empty(watch.Seen);
                            Assert.All(Application.OpenForms.Cast<Form>(), open => Assert.Same(f, open));
                            Assert.Same(s, c.Status);
                            f.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-12 (G3): a running change disables exactly the change actions

        [Fact]
        public void A_running_change_disables_exactly_the_change_actions()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads))
            {
                Sta.Run(() =>
                {
                    // Repair (N1), "Get the newest QuietHosts" (N3), "Show lines" (N5) and "Why?" (N10): four bars, so one is
                    // behind "Show 1 more notice".
                    HostsStatus many = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.ChangeNotFinished | HostsWarnings.MicrosoftLinesOutside,
                        blockListVersion: UiScenarios.NewerListVersion(p.Lists));
                    var c = new AppController(p.Context, many);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        NoticeArea area = f.PreviewNoticeArea;
                        Assert.True(Flow.Shows(f, area.ShowMoreLink));
                        Control repair = area.BarFor(NoticeId.ChangeNotFinished)!.Action!;
                        Control newest = area.BarFor(NoticeId.NewerListInFile)!.Action!;
                        Control lines = area.BarFor(NoticeId.MicrosoftLines)!.Action!;
                        Control why = area.BarFor(NoticeId.DownloadsTip)!.Action!;
                        var waits = new List<Control> { f.PreviewStatusCard.MainButton, f.PreviewStrictCard.Button, f.PreviewUndoButton, repair };
                        var works = new List<Control>
                        {
                            f.PreviewFixButton, f.PreviewAboutButton, f.PreviewStrictCard.Link!, f.PreviewNewerVersionLink, area.ShowMoreLink, newest, lines, why,
                            area.BarFor(NoticeId.MicrosoftLines)!.CloseButton!, area.BarFor(NoticeId.DownloadsTip)!.CloseButton!,
                        };
                        AssertBusyRule(c, waits, works);
                        f.Close();
                    }

                    foreach ((HostsStatus s, NoticeId id) in new[]
                    {
                        (UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: UiScenarios.OlderListVersion), NoticeId.NewerListInApp),
                        (UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.LegacyBlockToo, legacy: true), NoticeId.LegacyBlock),
                    })
                    {
                        var other = new AppController(p.Context, s);
                        using (var f = new MainForm(other, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            NoticeBar bar = f.PreviewNoticeArea.BarFor(id)!;
                            AssertBusyRule(other, new List<Control> { bar.Action!, f.PreviewStandardCard.Button.Visible ? f.PreviewStandardCard.Button : f.PreviewStrictCard.Button },
                                new List<Control> { bar.CloseButton! });
                            f.Close();
                        }
                    }

                    // S10: Check again waits for this window's change; "How to do this" never waits.
                    var encoding = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Refused, refusal: RefusalReason.UnsupportedEncoding));
                    using (var f = new MainForm(encoding, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        AssertBusyRule(encoding, new List<Control> { f.PreviewStatusCard.MainButton }, new List<Control> { f.PreviewStatusCard.HowToLink });
                        f.Close();
                    }

                    // The first run: Turn on protection waits, Not now doesn't.
                    var first = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false));
                    using (var f = new MainForm(first, showFirstRun: true))
                    {
                        Sta.ShowOffScreen(f);
                        AssertBusyRule(first, new List<Control> { f.PreviewFirstRun!.TurnOnButton }, new List<Control> { f.PreviewFirstRun.NotNowButton, f.PreviewFirstRun.WhyLink });
                        f.Close();
                    }
                });
            }
        }

        private static void AssertBusyRule(AppController c, List<Control> waits, List<Control> works)
        {
            foreach (Control x in waits.Concat(works)) Assert.True(x.Enabled, x.Text + " is disabled before the change");
            c.PreviewBusy(Strings.WaitingForPermission);
            foreach (Control x in waits) Assert.False(x.Enabled, x.Text + " works while a change runs");
            foreach (Control x in works) Assert.True(x.Enabled, x.Text + " is disabled while a change runs");
            c.PreviewClearBusy();
            foreach (Control x in waits.Concat(works)) Assert.True(x.Enabled, x.Text + " didn't come back after the change");
        }

        // ---------------------------------------------------------------- T-B-13 (G5): the focus after a change

        [Fact]
        public void The_focus_comes_back_after_a_change()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus strict = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict);
                    HostsStatus standard = UiScenarios.Status(p.Lists, ProtectionState.On);

                    // Declined: "Use Standard" is still there, and has the focus again.
                    var declined = new AppController(p.Context, strict, Flow.Returns(ResultKind.Declined), () => strict);
                    using (var f = new MainForm(declined, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        ThemedButton use = f.PreviewStandardCard.Button;
                        use.Focus();
                        Flow.Click(f, use);
                        Flow.PumpUntil(() => declined.LastResult != null && !declined.IsBusy);
                        Assert.True(use.Focused, "the focus didn't come back to Use Standard");
                        f.Close();
                    }

                    // It worked: Use Standard became "In use", so the main button gets the focus.
                    var worked = new AppController(p.Context, strict, Flow.Returns(ResultKind.Success), () => standard);
                    using (var f = new MainForm(worked, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        ThemedButton use = f.PreviewStandardCard.Button;
                        use.Focus();
                        Flow.Click(f, use);
                        Flow.PumpUntil(() => worked.LastResult != null && !worked.IsBusy);
                        Assert.Equal(Strings.ResultLevelChanged(Level.Standard), worked.ResultLine);
                        Assert.Equal(LevelCardState.InUse, f.PreviewStandardCard.View!.State);
                        Assert.False(Flow.Shows(f, use));
                        Assert.True(f.PreviewStatusCard.MainButton.Focused, "the focus didn't go to the main button");
                        f.Close();
                    }

                    // The Fix dialog's last step button is the same control after "Turn off for now" (a test turn-off) worked:
                    // it becomes "Turn on again" and keeps the focus.
                    HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                    var fix = new AppController(p.Context, standard, Flow.Returns(ResultKind.Success), () => off);
                    using (var main = new MainForm(fix, showFirstRun: false))
                    using (var d = new FixDialog(fix) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(main);
                        Sta.ShowOffScreen(d);
                        Button step = ControlTree.All(d).OfType<Button>().Single(b => b.Text == Strings.FixTurnOffButton);
                        step.Focus();
                        Flow.Click(d, step);
                        Flow.PumpUntil(() => fix.LastResult != null && !fix.IsBusy);
                        Assert.True(fix.OffForTest);
                        Assert.Same(step, ControlTree.All(d).OfType<Button>().Single(b => b.Text == Strings.FixTurnOnButton));
                        Assert.False(step.IsDisposed);
                        Assert.True(step.Focused, "the focus didn't stay on the step button");
                        d.Close();
                        main.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-14 (G6): a declined prompt changes nothing and says so

        [Fact]
        public void A_declined_prompt_changes_nothing_and_says_so()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                    var commands = new List<ElevatedCommand>();
                    var c = new AppController(p.Context, off, Flow.Returns(ResultKind.Declined, commands), () => off);
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewForeground = true })
                    using (var watch = new DialogWatch(d => DialogResult.Cancel))
                    {
                        Sta.ShowOffScreen(f);
                        string headline = f.PreviewStatusCard.Headline.Text;
                        Flow.Click(f, f.PreviewStatusCard.MainButton);
                        Flow.PumpUntil(() => c.LastResult != null && !c.IsBusy);
                        Flow.PumpFor(200);
                        Assert.Single(commands);
                        Assert.Equal(Strings.ResultDeclined, c.LastResult!.Text);
                        Assert.Equal(NoticeSeverity.Info, c.LastResult.Severity);
                        Assert.Equal(headline, f.PreviewStatusCard.Headline.Text);
                        Assert.Empty(watch.Seen);
                        NoticeBar bar = f.PreviewNoticeArea.ResultBar!;
                        Assert.True(Flow.Shows(f, bar));
                        Assert.Equal(Strings.PrefixNote + Strings.ResultDeclined, bar.AccessibleName);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-15 (G7): every result is announced once

        [Fact]
        public void Every_result_and_progress_step_is_announced_once()
        {
            using (var p = new PreviewContext())
            using (var finish = new ManualResetEventSlim(false))
            {
                Sta.Run(() =>
                {
                    var heard = new List<(Control Control, string Text, Announcement Kind)>();
                    global::QuietHosts.Ui.AnnouncementSink = (control, text, kind) => heard.Add((control, text, kind));
                    try
                    {
                        HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                        HostsStatus off = UiScenarios.Status(p.Lists, ProtectionState.Off);
                        LaunchChange slow = (command, progress, owner) => Task.Run(() =>
                        {
                            progress.Report(LaunchStage.WaitingForPermission);
                            progress.Report(LaunchStage.Changing);
                            progress.Report(LaunchStage.Changing);
                            progress.Report(LaunchStage.TakingLong);
                            progress.Report(LaunchStage.TakingLong);
                            finish.Wait(10000);
                            return new LaunchResult(ResultKind.Success, 0, null, Flow.Hash, Flow.OtherHash);
                        });
                        var c = new AppController(p.Context, on, slow, () => off);
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            Task run = Flow.Start(f, () => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, f));
                            Flow.PumpUntil(() => c.TakingLong);
                            Flow.PumpFor(100);
                            finish.Set();
                            Flow.PumpUntil(() => run.IsCompleted);
                            Assert.DoesNotContain(heard, h => h.Text.StartsWith("Waiting for your permission", StringComparison.Ordinal));
                            Assert.Single(heard, h => h.Kind == Announcement.Progress && h.Text == Strings.ChangingHostsFile);
                            Assert.Single(heard, h => h.Kind == Announcement.Progress && h.Text == Strings.ChangingSlow);
                            (Control from, string text, Announcement _) = Assert.Single(heard, h => h.Kind == Announcement.Result);
                            Assert.Same(f, from);
                            Assert.Equal(Strings.ResultTurnOffOk, text);
                            Assert.Equal(c.LastResult!.Text, text);
                            f.Close();
                        }

                        // R12: refused, and the file itself is refused: no bar, and screen readers hear why.
                        heard.Clear();
                        HostsStatus tooLarge = UiScenarios.Status(p.Lists, ProtectionState.Refused, refusal: RefusalReason.TooLarge);
                        var refused = new AppController(p.Context, on, Flow.Returns(ResultKind.Refused), () => tooLarge);
                        using (var f = new MainForm(refused, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            Task run = Flow.Start(f, () => refused.RunAsync(ElevatedCommand.On(Level.Strict), ChangeKind.ChangeLevel, f));
                            Flow.PumpUntil(() => run.IsCompleted);
                            Assert.Null(refused.LastResult);
                            Assert.Null(f.PreviewNoticeArea.ResultBar);
                            (Control _, string text, Announcement _) = Assert.Single(heard, h => h.Kind == Announcement.Result);
                            Assert.Equal(Strings.ResultRefusedAnnouncement(Strings.Headline(ProtectionState.Refused, RefusalReason.TooLarge), Strings.DetailTooLarge), text);
                            f.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-17: what Narrator reads

        [Fact]
        public void Narrator_reads_the_state_and_notices_with_the_main_button()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.LargeFile);
                    var c = new AppController(p.Context, s);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        ThemedButton main = f.PreviewStatusCard.MainButton;
                        string expected = Strings.Headline(s.State, s.Refusal) + ". " + Strings.Detail(s) + " " +
                            Strings.PrefixWarning + Strings.WarningEditedBlock + " " + Strings.PrefixNote + Strings.WarningLargeFile(s.BytesOutsideBlocks) + " " +
                            Strings.ShieldDescription;
                        Assert.Equal(expected, main.AccessibleDescription);
                        Assert.Equal(AccessibleRole.PushButton, main.AccessibilityObject.Role);

                        // The result bar isn't part of it.
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        Assert.Equal(expected, main.AccessibleDescription);

                        Assert.Equal("Standard, recommended, in use", f.PreviewStandardCard.AccessibleName);
                        Assert.Equal(AccessibleRole.Grouping, f.PreviewStandardCard.AccessibilityObject.Role);
                        Assert.Equal("Strict", f.PreviewStrictCard.AccessibleName);
                        Assert.Equal(Strings.StrictDescription(p.Lists.StrictExtraCount), f.PreviewStrictCard.Button.AccessibleDescription);

                        NoticeBar edited = f.PreviewNoticeArea.BarFor(NoticeId.EditedBlock)!;
                        Assert.Equal(Strings.PrefixWarning + Strings.WarningEditedBlock, edited.AccessibleName);
                        Assert.Equal(AccessibleRole.Grouping, edited.AccessibilityObject.Role);
                        Assert.Equal(Strings.CloseNoticeName, edited.CloseButton!.AccessibleName);
                        Assert.Equal(edited.AccessibleName, edited.CloseButton.AccessibleDescription);
                        Assert.Equal(Strings.RepairButton, edited.Action!.Text);
                        Assert.Equal(Strings.ShieldDescription, edited.Action.AccessibleDescription);
                        Assert.Equal(AccessibleRole.Graphic, f.PreviewStatusCard.Shield.AccessibleRole);
                    }

                    // Strict while On: Use Standard carries the shield sentence after the card's description.
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict)), showFirstRun: false))
                    {
                        Assert.Equal("Standard, recommended", f.PreviewStandardCard.AccessibleName);
                        Assert.Equal("Strict, in use", f.PreviewStrictCard.AccessibleName);
                        Assert.Equal(Strings.StandardDescription + " " + Strings.ShieldDescription, f.PreviewStandardCard.Button.AccessibleDescription);
                    }

                    // The progress ring is named after the first line of its text.
                    var busy = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var f = new MainForm(busy, showFirstRun: false))
                    {
                        busy.PreviewBusy(Strings.ChangingHostsFile, takingLong: true);
                        Assert.Equal(AccessibleRole.ProgressBar, f.PreviewStatusCard.Ring.AccessibleRole);
                        Assert.Equal(Strings.ChangingHostsFile, f.PreviewStatusCard.Ring.AccessibleName);
                        Assert.Equal(Strings.ChangingHostsFile + "\r\n" + Strings.ChangingSlow, f.PreviewStatusCard.ProgressLabel.Text);
                    }
                });
            }
        }

        [Fact]
        public void New_notices_after_a_reread_are_announced_once()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var heard = new List<(string Text, Announcement Kind)>();
                    global::QuietHosts.Ui.AnnouncementSink = (control, text, kind) => heard.Add((text, kind));
                    try
                    {
                        HostsStatus plain = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.LargeFile);
                        HostsStatus edited = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.LargeFile | HostsWarnings.EditedBlock);
                        HostsStatus next = plain;
                        var c = new AppController(p.Context, plain, Flow.Returns(ResultKind.Success), () => next);
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            // Nothing at window open.
                            Assert.Empty(heard);
                            next = edited;
                            Task run = Flow.Start(f, () => c.RefreshAsync());
                            Flow.PumpUntil(() => run.IsCompleted);
                            Assert.Equal(new[] { (Strings.PrefixWarning + Strings.WarningEditedBlock, Announcement.Notice) }, heard);
                            // The same notices again: nothing new to say.
                            next = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.LargeFile | HostsWarnings.EditedBlock);
                            run = Flow.Start(f, () => c.RefreshAsync());
                            Flow.PumpUntil(() => run.IsCompleted);
                            Assert.Single(heard);
                            f.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        [Fact]
        public void A_changes_result_is_heard_before_the_notices_it_brought()
        {
            // The window announced a new notice before AppController announced the change's result, so the important result
            // could cut the notice off (behavior-3): the result comes first, then the notice.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var heard = new List<(string Text, Announcement Kind)>();
                    global::QuietHosts.Ui.AnnouncementSink = (control, text, kind) => heard.Add((text, kind));
                    try
                    {
                        HostsStatus on = UiScenarios.Status(p.Lists, ProtectionState.On);
                        HostsStatus offLarge = UiScenarios.Status(p.Lists, ProtectionState.Off, warnings: HostsWarnings.LargeFile);
                        var c = new AppController(p.Context, on, Flow.Returns(ResultKind.Success), () => offLarge);
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            heard.Clear();
                            Task run = Flow.Start(f, () => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, f));
                            Flow.PumpUntil(() => run.IsCompleted);
                            Flow.PumpFor(100);
                            string notice = NoticeBar.NameFor(NoticeSeverity.Info, Strings.WarningLargeFile(offLarge.BytesOutsideBlocks));
                            Assert.Equal(new[] { (Strings.ResultTurnOffOk, Announcement.Result), (notice, Announcement.Notice) },
                                heard.Where(h => h.Kind != Announcement.Progress).ToArray());
                            f.Close();
                        }
                    }
                    finally
                    {
                        global::QuietHosts.Ui.AnnouncementSink = null;
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-B-21 (D15, D19): the footer row and the laptop screen

        public static IEnumerable<object[]> LaptopTargets() => new[]
        {
            new object[] { "S1", null! },
            new object[] { "S1", "R3" },
            new object[] { "S1", "R10" },
            new object[] { "S3", "R1" },
            new object[] { "S3", "R4" },
            new object[] { "S3", "R10" },
            new object[] { "S4", "R1" },
            new object[] { "S4", "R4" },
            new object[] { "S4", "R10" },
        };

        /// <summary>
        /// D19: on a 1920 x 1080 screen at 150% (a 1280 x 672 epx work area) at 100% text, the main window shows without a
        /// scroll bar with no notice, and with the result bar of the common changes.
        /// </summary>
        [Theory]
        [MemberData(nameof(LaptopTargets))]
        public void On_the_common_laptop_screen_the_window_needs_no_scroll_bar(string row, string? result)
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
#if DEBUG
                        UiFonts.TextScale = 1f;
#endif
                        BlockLists l = p.Lists;
                        HostsStatus s = row == "S1" ? UiScenarios.Status(l, ProtectionState.Off)
                            : row == "S3" ? UiScenarios.Status(l, ProtectionState.On)
                            : UiScenarios.Status(l, ProtectionState.On, Level.Strict, allowed: 3);
                        Level level = s.BlockLevel ?? s.SelectedLevel;
                        ChangeResult? bar = result switch
                        {
                            "R1" => UiScenarios.Result(l, ChangeKind.TurnOn, ElevatedCommand.On(level), ResultKind.Success, s),
                            "R3" => UiScenarios.Result(l, ChangeKind.TurnOff, ElevatedCommand.Off(), ResultKind.Success, s),
                            "R4" => UiScenarios.Result(l, ChangeKind.ChangeLevel, ElevatedCommand.On(level), ResultKind.Success, s),
                            "R10" => UiScenarios.Result(l, ChangeKind.TurnOn, ElevatedCommand.On(level), ResultKind.Declined, s),
                            _ => null,
                        };
                        Rectangle laptop = UiScenarios.Scaled(UiScenarios.LaptopScreen150);
                        var c = new AppController(p.Context, s);
                        using (var f = new MainForm(c, showFirstRun: false) { PreviewWorkingArea = laptop })
                        {
                            Sta.ShowOffScreen(f);
                            if (bar != null) c.PreviewResult(bar, f);
                            Flow.PumpFor(50);
                            Assert.Empty(f.PreviewNotices);
                            Assert.Equal(bar != null, f.PreviewNoticeArea.HasBars);
                            Assert.False(f.ContentScrolls, row + " with " + (result ?? "no bar") + " scrolls: the window is " + f.Height + " px tall, the limit " +
                                (laptop.Height - UiScale.Current.Px(Metrics.MainHeightMargin)) + " px");
                            Assert.True(f.Height <= laptop.Height - UiScale.Current.Px(Metrics.MainHeightMargin));
                            Assert.False(f.PreviewFooter.ShowDivider);
                            f.Close();
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        [Fact]
        public void The_footer_row_stays_visible_below_a_page_that_scrolls()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    UiScale sc = UiScale.Current;
                    Rectangle laptop = UiScenarios.Scaled(UiScenarios.LaptopScreen150);
                    // Several notices and the result bar: this still scrolls on the laptop screen (D15 stays for it).
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.LargeFile);
                    var c = new AppController(p.Context, s);
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewWorkingArea = laptop })
                    {
                        Sta.ShowOffScreen(f);
                        c.PreviewResult(UiScenarios.Result(p.Lists, ChangeKind.TurnOn, ElevatedCommand.On(Level.Standard), ResultKind.Success, s), f);
                        Flow.PumpFor(50);
                        Assert.True(f.ContentScrolls);
                        Assert.True(f.Height <= laptop.Height - sc.Px(Metrics.MainHeightMargin));
                        Assert.True(f.PreviewFooter.ShowDivider, "no divider line above the footer row while the page scrolls");
                        foreach (Button b in new Button[] { f.PreviewFixButton, f.PreviewUndoButton, f.PreviewAboutButton })
                        {
                            Rectangle r = f.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
                            Assert.True(r.Top >= 0 && r.Bottom <= f.ClientSize.Height, b.Text + " is outside the window: " + r + " in " + f.ClientSize);
                        }
                        // The tab order: the block list's link, then Fix a site or app…, Undo all changes…, About.
                        f.PreviewNewerVersionLink.Focus();
                        var order = new List<Control>();
                        Control? next = f.PreviewNewerVersionLink;
                        for (int i = 0; i < 3; i++)
                        {
                            next = f.GetNextControl(next!, true);
                            while (next != null && !(next.CanSelect && next.TabStop)) next = f.GetNextControl(next, true);
                            order.Add(next!);
                        }
                        Assert.Equal(new Control[] { f.PreviewFixButton, f.PreviewUndoButton, f.PreviewAboutButton }, order);
                        f.Close();
                    }

                    // With room: the footer row sits 24 epx below the block list line, with no divider.
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Assert.False(f.ContentScrolls);
                        Assert.False(f.PreviewFooter.ShowDivider);
                        Control line = f.PreviewNewerVersionLink.Parent!;
                        int lineBottom = f.RectangleToClient(line.RectangleToScreen(line.ClientRectangle)).Bottom;
                        int fixTop = f.RectangleToClient(f.PreviewFixButton.RectangleToScreen(f.PreviewFixButton.ClientRectangle)).Top;
                        Assert.Equal(sc.Px(Metrics.SectionGap), fixTop - lineBottom);
                        Assert.Equal(sc.Px(Metrics.PagePadding), f.ClientSize.Height - f.PreviewFixButton.Bounds.Bottom - f.PreviewFooter.Top);
                        Assert.Equal(sc.Px(Metrics.MainClientWidth), f.ClientSize.Width);
                        f.Close();
                    }

                    // The first run's buttons are in the footer slot; Not now swaps in the footer row.
                    var first = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false));
                    using (var f = new MainForm(first, showFirstRun: true))
                    {
                        Sta.ShowOffScreen(f);
                        FirstRunPanel run = f.PreviewFirstRun!;
                        Assert.Equal(new Control[] { run.TurnOnButton, run.NotNowButton }, f.PreviewFooter.LeftControls);
                        Assert.Empty(f.PreviewFooter.RightControls);
                        Assert.Same(run.NotNowButton, f.CancelButton);
                        run.NotNowButton.PerformClick();
                        Assert.False(f.IsShowingFirstRun);
                        Assert.Equal(new Control[] { f.PreviewFixButton }, f.PreviewFooter.LeftControls);
                        Assert.Equal(new Control[] { f.PreviewUndoButton, f.PreviewAboutButton }, f.PreviewFooter.RightControls);
                        Assert.Null(f.CancelButton);
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void The_page_follows_the_layout_metrics()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiScale sc = UiScale.Current;
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Rectangle Of(Control c) => f.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                        StatusCard card = f.PreviewStatusCard;
                        Rectangle cardBox = Of(card);
                        Assert.Equal(new Rectangle(sc.Px(24), sc.Px(24), sc.Px(528), cardBox.Height), cardBox);
                        Assert.Equal(new Rectangle(cardBox.X + sc.Px(20), cardBox.Y + sc.Px(20), sc.Px(64), sc.Px(64)), Of(card.Shield));
                        Assert.Equal(cardBox.X + sc.Px(100), Of(card.Headline).X);
                        Assert.Equal(cardBox.Y + sc.Px(20), Of(card.Headline).Y);
                        Assert.Equal(Of(card.Headline).Bottom + sc.Px(4), Of(card.Detail).Y);
                        Assert.Equal(Of(card.Detail).Bottom + sc.Px(16), Of(card.MainButton).Y);
                        Assert.Equal(cardBox.X + sc.Px(100), Of(card.MainButton).X);
                        Assert.Equal(Of(card.MainButton).Bottom + sc.Px(20), cardBox.Bottom);
                        // No notice: "Blocking level" 24 below the card; the cards 8 and 4 apart; the block list line 12 below.
                        Rectangle standard = Of(f.PreviewStandardCard), strict = Of(f.PreviewStrictCard);
                        Assert.Equal(sc.Px(528), standard.Width);
                        Assert.Equal(standard.Bottom + sc.Px(4), strict.Top);
                        Assert.True(standard.Height >= sc.Px(68) && strict.Height >= sc.Px(68));
                        Assert.Equal(strict.Bottom + sc.Px(12), Of(f.PreviewNewerVersionLink.Parent!).Top);
                        // Both cards' left columns are equally wide, and the right side is vertically centered.
                        Rectangle use = Of(f.PreviewStrictCard.Button), inUse = Of(f.PreviewStandardCard.Indicator);
                        Assert.Equal(strict.Right - sc.Px(16), use.Right);
                        Assert.Equal(standard.Right - sc.Px(16), inUse.Right);
                        Assert.InRange(use.Top - strict.Top - (strict.Bottom - use.Bottom), -1, 1);
                        f.Close();
                    }
                });
            }
        }

        /// <summary>The first column with ink (unlike the background) in an area of a picture.</summary>
        internal static int FirstInk(Bitmap bmp, Rectangle area, Color background)
        {
            for (int x = Math.Max(0, area.Left); x < Math.Min(bmp.Width, area.Right); x++)
                for (int y = Math.Max(0, area.Top); y < Math.Min(bmp.Height, area.Bottom); y++)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (Math.Abs(c.R - background.R) + Math.Abs(c.G - background.G) + Math.Abs(c.B - background.B) > 90) return x;
                }
            return -1;
        }

        [Fact]
        public void The_status_text_starts_on_the_main_buttons_edge()
        {
            // The headline and the detail started GDI's blank margin (4 to 7 px) right of the main button's left edge, which
            // 3.1 puts on the text column (visual-5); "How to do this" lines its letters up with them.
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        ThemeManager.Override(Theme.Light);
                        foreach (HostsStatus s in new[] { UiScenarios.Status(p.Lists, ProtectionState.On), UiScenarios.Status(p.Lists, ProtectionState.Refused, refusal: RefusalReason.UnsupportedEncoding) })
                        {
                            using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false))
                            {
                                Sta.ShowOffScreen(f);
                                StatusCard card = f.PreviewStatusCard;
                                Assert.True(card.Headline.Tight && card.Detail.Tight);
                                Assert.Equal(card.MainButton.Left, card.Headline.Left);
                                Assert.Equal(card.MainButton.Left, card.Detail.Left);
                                using (Bitmap bmp = Pixels.Of(card))
                                {
                                    foreach (ThemedLabel label in new[] { card.Headline, card.Detail })
                                    {
                                        int first = FirstInk(bmp, label.Bounds, Theme.Light.CardBackground);
                                        Assert.InRange(first - card.MainButton.Left, 0, UiScale.Current.Px(3));
                                    }
                                }
                                if (Flow.Shows(f, card.HowToLink))
                                    Assert.Equal(card.Detail.Left, card.HowToLink.Left + TextFlow.Lead(card.HowToLink));
                                // At the normal text size the block list's date and its link share one line.
                                Assert.True(f.PreviewListDate.Height < 2 * f.PreviewListDate.Font.Height);
                                Assert.True(f.PreviewNewerVersionLink.Top < f.PreviewListDate.Bottom, "Check for a newer version went to a line of its own at 100% text");
                                f.Close();
                            }
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

#if DEBUG
        [Fact]
        public void At_large_text_check_for_a_newer_version_lines_up_with_the_date_when_it_wraps()
        {
            // At 150% text the link wrapped onto its own line 4 px right of the date's text (visual-9).
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (float text in new[] { 1.5f, 2.25f })
                        {
                            UiFonts.TextScale = text;
                            using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                            {
                                Sta.ShowOffScreen(f);
                                ThemedLink link = f.PreviewNewerVersionLink;
                                ThemedLabel date = f.PreviewListDate;
                                if (link.Top >= date.Bottom)
                                    Assert.Equal(date.Left + TextFlow.Lead(date), link.Left + TextFlow.Lead(link));
                                else
                                    Assert.True(link.Left > date.Left, "the link overlaps the date");
                                // The date's letters stay on the page's column, as every other row's.
                                Rectangle Of(Control c) => f.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
                                Assert.Equal(Of(f.PreviewStandardCard).X, Of(date).X);
                                f.Close();
                            }
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }
#endif

        // ---------------------------------------------------------------- the result bar's Copy details and close

        [Fact]
        public void Closing_the_result_bar_clears_the_result_and_the_focus_moves_on()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock);
                    var c = new AppController(p.Context, s);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        c.PreviewResult(Strings.ResultTurnOnOk, f);
                        NoticeBar result = f.PreviewNoticeArea.ResultBar!;
                        Assert.Same(result, f.PreviewNoticeArea.Bars[0]);
                        result.CloseButton!.Focus();
                        result.CloseButton.PerformClick();
                        Assert.Null(c.LastResult);
                        Assert.Null(f.PreviewNoticeArea.ResultBar);
                        // The next bar's first control: Repair on the edited-block notice.
                        Assert.True(f.PreviewNoticeArea.BarFor(NoticeId.EditedBlock)!.Action!.Focused);
                        f.Close();
                    }
                });
            }
        }
    }
}
