using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// A change as the windows live through it, with a fake launcher (nothing is started): where the focus goes back, the
    /// line for a run that takes long, another QuietHosts run that is still going, the prompt's owner window, a window
    /// that was minimized meanwhile or whose screen changed, and how the window opens on a short screen.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class ChangeFlowTests
    {
        private static readonly string Hash = new string('a', 64);
        private static readonly string OtherHash = new string('b', 64);
        private static readonly MethodInfo ProcessDialogKey = typeof(Control).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_SHOWMINNOACTIVE = 7;

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        /// <summary>Handles the window's messages until <paramref name="done"/> or the time is up.</summary>
        private static void PumpUntil(Func<bool> done, int timeoutMs = 5000)
        {
            var clock = Stopwatch.StartNew();
            while (!done() && clock.ElapsedMilliseconds < timeoutMs)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        private static void PumpFor(int milliseconds) => PumpUntil(() => false, milliseconds);

        /// <summary>
        /// Runs <paramref name="action"/> from the window's message loop, as a click does. There WinForms' synchronization
        /// context brings every await back to the window's thread; a test's own code runs outside it (DoEvents puts the
        /// plain context back when it returns), where an await would go on on a worker thread.
        /// </summary>
        private static Task Start(Form f, Func<Task> action)
        {
            Task? run = null;
            f.BeginInvoke(new Action(() => run = action()));
            PumpUntil(() => run != null);
            return run ?? throw new InvalidOperationException("The window didn't run it.");
        }

        private static LaunchChange Returns(ResultKind kind, Action? onStart = null) => (command, progress, owner) =>
        {
            onStart?.Invoke();
            return Task.Run(() => new LaunchResult(kind, kind == ResultKind.Declined ? (int?)null : ExitCodes.FromResult(kind), null, Hash, kind == ResultKind.Success ? OtherHash : Hash));
        };

        private static T Find<T>(Form f, string text) where T : Control => ControlTree.All(f).OfType<T>().Single(c => c.Text == text);

        private static bool Shows(Form f, string text) => ControlTree.All(f).Any(c => c is Label && c.Text == text && ControlTree.IsMeantToShow(c, f));

        // ---------------------------------------------------------------- focus after a change

        [Fact]
        public void A_level_change_that_didnt_take_is_not_asked_for_again_when_the_focus_comes_back()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    int runs = 0;
                    // Windows asks for permission and the user clicks No. A second request is the bug: it never finishes
                    // here, so the test fails instead of going round the loop of Windows prompts forever.
                    LaunchChange declined = (command, progress, owner) => ++runs == 1
                        ? Task.Run(() => new LaunchResult(ResultKind.Declined, null, null, Hash, Hash))
                        : new TaskCompletionSource<LaunchResult>().Task;
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict), declined);
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        // The Standard card's "Use Standard": one click from the message loop asks for Standard once.
                        ThemedButton useStandard = f.PreviewStandardCard.Button;
                        Assert.Equal(Strings.UseStandardCardButton, useStandard.Text);
                        useStandard.Focus();
                        f.BeginInvoke(new Action(useStandard.PerformClick));
                        PumpUntil(() => runs > 0 && !c.IsBusy && c.ResultLine != null);
                        // A second request would come right after the focus is given back.
                        PumpFor(400);
                        Assert.Equal(1, runs);
                        Assert.Equal(Strings.ResultDeclined, c.StatusLineText);
                        Assert.Equal(NoticeSeverity.Info, c.LastResult!.Severity);
                        // The Strict card is still in use, and the focus is back on "Use Standard".
                        Assert.Equal(LevelCardState.InUse, f.PreviewStrictCard.View!.State);
                        Assert.True(useStandard.Focused);
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void When_the_control_that_started_a_change_is_gone_the_focus_goes_to_the_main_button()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    // "Use newer list" worked: the block is now from this exe's list, and the notice with its button is gone.
                    HostsStatus older = UiScenarios.Status(p.Lists, ProtectionState.On, blockListVersion: UiScenarios.OlderListVersion);
                    HostsStatus current = UiScenarios.Status(p.Lists, ProtectionState.On);
                    var c = new AppController(p.Context, older, Returns(ResultKind.Success), () => current);
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        ThemedButton use = Find<ThemedButton>(f, Strings.UseNewerListButton);
                        Assert.Same(use, f.PreviewNoticeArea.BarFor(NoticeId.NewerListInApp)!.Action);
                        use.Focus();
                        Assert.True(use.Focused);
                        f.BeginInvoke(new Action(use.PerformClick));
                        PumpUntil(() => !c.IsBusy && c.ResultLine != null);
                        Assert.Equal(Strings.ResultListUpdated(p.Lists.Version), c.ResultLine);
                        Assert.Null(f.PreviewNoticeArea.BarFor(NoticeId.NewerListInApp));
                        Assert.True(use.IsDisposed);
                        Assert.True(Find<Button>(f, Strings.MainButton(ProtectionState.On)).Focused, "the focus didn't come back to the main button");
                        f.Close();
                    }

                    // Undo all changes worked: there is nothing left to undo, so its button is disabled.
                    var undoing = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On), Returns(ResultKind.Success),
                        () => UiScenarios.Status(p.Lists, ProtectionState.Off));
                    using (var f = new MainForm(undoing, showFirstRun: false) { PreviewForeground = true })
                    {
                        Sta.ShowOffScreen(f);
                        Button undo = Find<Button>(f, Strings.UndoButton);
                        undo.Focus();
                        Assert.True(undo.Focused);
                        Task run = Start(f, () => undoing.RunAsync(ElevatedCommand.Undo(), ChangeKind.Undo, f));
                        PumpUntil(() => run.IsCompleted);
                        Assert.Equal(Strings.ResultUndoOk, undoing.ResultLine);
                        Assert.False(undo.Enabled);
                        Assert.True(Find<Button>(f, Strings.MainButton(ProtectionState.Off)).Focused, "the focus didn't come back to the main button");
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void Enter_and_shift_enter_never_switch_protection_from_the_main_window()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var forms = new (MainForm Form, string Button)[]
                    {
                        (new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false), Strings.MainButton(ProtectionState.On)),
                        (new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off)), showFirstRun: false), Strings.MainButton(ProtectionState.Off)),
                        (new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false)), showFirstRun: true), Strings.FirstRunTurnOnButton),
                    };
                    foreach ((MainForm f, string text) in forms)
                    {
                        using (f)
                        {
                            // A click (and so Enter on the focused button) needs a visible window.
                            Sta.ShowOffScreen(f);
                            // The first run's Turn on protection has the main button's Off text: it is the one in the footer slot.
                            Button button = f.IsShowingFirstRun ? f.PreviewFirstRun!.TurnOnButton : Find<Button>(f, text);
                            Assert.Equal(text, button.Text);
                            int clicks = 0;
                            button.Click += (s, e) => clicks++;
                            f.ActiveControl = button;
                            foreach (Keys keys in new[] { Keys.Enter, Keys.Enter | Keys.Shift })
                            {
                                Assert.True((bool)ProcessDialogKey.Invoke(f, new object[] { keys })!);
                                Assert.True(clicks == 0, keys + " clicked " + text);
                            }
                            f.Close();
                        }
                    }
                });
            }
        }

        // ---------------------------------------------------------------- a run that takes long

        [Fact]
        public void A_change_that_takes_long_says_so_until_its_result()
        {
            using (var p = new PreviewContext())
            using (var finish = new ManualResetEventSlim(false))
            {
                Sta.Run(() =>
                {
                    // The elevated run started, and 30 s later it is still going (an antivirus holding it, say).
                    LaunchChange slow = (command, progress, owner) => Task.Run(() =>
                    {
                        progress.Report(LaunchStage.WaitingForPermission);
                        progress.Report(LaunchStage.Changing);
                        progress.Report(LaunchStage.TakingLong);
                        finish.Wait(10000);
                        return new LaunchResult(ResultKind.Success, 0, null, Hash, OtherHash);
                    });
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On), slow,
                        () => UiScenarios.Status(p.Lists, ProtectionState.Off));
                    using (var f = new MainForm(c, showFirstRun: false))
                    using (var d = new FixDialog(c))
                    {
                        Sta.ShowOffScreen(f);
                        ControlTree.CreateHandles(d);
                        Task? run = null;
                        try
                        {
                            run = Start(f, () => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, f));
                            PumpUntil(() => c.TakingLong);
                            Assert.True(c.IsBusy);
                            Assert.Equal(Strings.ChangingHostsFile + "\r\n" + Strings.ChangingSlow, c.StatusLineText);
                            Assert.True(Shows(f, Strings.ChangingHostsFile + "\r\n" + Strings.ChangingSlow));
                            // The Fix dialog has less room above its buttons: one line.
                            Assert.True(Shows(d, Strings.ChangingHostsFile + " " + Strings.ChangingSlow));
                            // The window keeps waiting: nothing stops the run.
                            PumpFor(100);
                            Assert.False(run.IsCompleted);
                        }
                        finally
                        {
                            finish.Set();
                            if (run != null) PumpUntil(() => run.IsCompleted);
                        }
                        // The line goes once the result is there.
                        Assert.False(c.TakingLong);
                        Assert.Equal(Strings.ResultTurnOffOk, c.StatusLineText);
                        Assert.DoesNotContain(ControlTree.All(f).Concat(ControlTree.All(d)), x => x.Text.Contains(Strings.ChangingSlow));
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void A_quick_change_never_says_it_takes_long()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    LaunchChange quick = (command, progress, owner) => Task.Run(() =>
                    {
                        progress.Report(LaunchStage.WaitingForPermission);
                        progress.Report(LaunchStage.Changing);
                        return new LaunchResult(ResultKind.Success, 0, null, Hash, OtherHash);
                    });
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On), quick);
                    var lines = new List<string>();
                    c.BusyChanged += (s, e) => lines.Add(c.StatusLineText);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Task run = Start(f, () => c.RunAsync(ElevatedCommand.On(Level.Strict), ChangeKind.ChangeLevel, f));
                        PumpUntil(() => run.IsCompleted);
                        Assert.Contains(Strings.ChangingHostsFile, lines);
                        Assert.DoesNotContain(lines, l => l.Contains(Strings.ChangingSlow));
                        Assert.Equal(Strings.ResultLevelChanged(Level.Strict), c.StatusLineText);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- the window that owns the Windows prompt

        [Fact]
        public void The_window_whose_button_started_the_change_owns_the_windows_prompt()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var owners = new List<IntPtr>();
                    LaunchChange record = (command, progress, owner) =>
                    {
                        owners.Add(owner);
                        return Task.Run(() => new LaunchResult(ResultKind.Success, 0, null, Hash, OtherHash));
                    };
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 1), record);
                    using (var f = new MainForm(c, showFirstRun: false))
                    using (var d = new FixDialog(c))
                    {
                        Sta.ShowOffScreen(f);
                        Sta.ShowOffScreen(d);
                        Task fromMain = Start(f, () => c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, f));
                        PumpUntil(() => fromMain.IsCompleted);
                        Task fromFix = Start(d, () => c.RunAsync(ElevatedCommand.Block(new[] { c.Status.Allowlist[0] }), ChangeKind.Block, d));
                        PumpUntil(() => fromFix.IsCompleted);
                        Assert.Equal(new[] { f.Handle, d.Handle }, owners);
                        d.Close();
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- another QuietHosts run is still going

        [Fact]
        public void While_another_run_is_going_change_actions_wait_and_everything_else_works()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, Level.Strict, allowed: 3,
                        blockListVersion: UiScenarios.OlderListVersion, runInProgress: true);
                    var c = new AppController(p.Context, s);
                    Assert.False(c.CanStartChange);
                    Assert.Equal(Strings.OtherChangeRunning, c.StatusLineText);
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        ControlTree.CreateHandles(f);
                        Assert.True(Shows(f, Strings.OtherChangeRunning));
                        foreach (string text in new[] { Strings.MainButton(ProtectionState.On), Strings.UndoButton })
                            Assert.False(Find<Button>(f, text).Enabled, text + " is enabled");
                        // Both level cards' buttons wait ("Use Standard" shows; Strict is in use).
                        Assert.True(ControlTree.IsMeantToShow(f.PreviewStandardCard.Button, f));
                        Assert.False(f.PreviewStandardCard.Button.Enabled);
                        Assert.False(f.PreviewStrictCard.Button.Enabled);
                        Assert.False(Find<ThemedButton>(f, Strings.UseNewerListButton).Enabled);
                        foreach (string text in new[] { Strings.FixButton, Strings.AboutButton })
                            Assert.True(Find<Button>(f, text).Enabled, text + " is disabled");
                        Assert.True(Find<ThemedLink>(f, Strings.WhatStrictChangesLink).Enabled);
                        Assert.True(Find<ThemedLink>(f, Strings.CheckForNewerVersionLink).Enabled);
                    }
                    using (var d = new FixDialog(c))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.True(Shows(d, Strings.OtherChangeRunning));
                        d.PreviewSelect(4);
                        Assert.False(d.CanAllow);
                        Assert.False(d.CanBlockAgain);
                        foreach (string text in new[] { Strings.FixTurnOffButton, Strings.FixSwitchToStandardButton })
                            Assert.False(Find<Button>(d, text).Enabled, text + " is enabled");
                        Assert.True(Find<Button>(d, Strings.CloseButton).Enabled);
                    }

                    // Can't read: the main button only reads the file again, so it doesn't wait.
                    var cantRead = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.CantRead, runInProgress: true));
                    using (var f = new MainForm(cantRead, showFirstRun: false))
                    {
                        Assert.True(Find<Button>(f, Strings.MainButton(ProtectionState.CantRead)).Enabled);
                    }

                    // The first run's Turn on protection waits too; Not now doesn't.
                    var first = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false, runInProgress: true));
                    using (var f = new MainForm(first, showFirstRun: true))
                    {
                        Assert.False(f.PreviewFirstRun!.TurnOnButton.Enabled);
                        Assert.True(Find<Button>(f, Strings.FirstRunNotNowButton).Enabled);
                    }
                });
            }
        }

        [Fact]
        public void The_window_reads_the_file_again_once_the_other_run_has_ended()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                BlockLists lists = BlockLists.LoadEmbedded();
                // Not a preview: the window reads this test environment's (missing, so empty) hosts file and looks for its
                // own test mutex. No change is started.
                var ctx = new UiContext(env, lists, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                Sta.Run(() =>
                {
                    // Stands in for an elevated run that holds the write mutex (started before this window opened).
                    var running = new Mutex(false, env.WriteMutexName);
                    try
                    {
                        HostsStatus s = StatusReader.Read(env, lists);
                        Assert.True(s.RunInProgress);
                        var c = new AppController(ctx, s);
                        // The window looks every 2 s whether the other run still holds the mutex (K10, a round-2 behavior).
                        Assert.Equal(2000, c.RunWatchInterval);
                        Assert.True(c.IsWatchingOtherRun);
                        c.PreviewRunWatchInterval(50);
                        using (var f = new MainForm(c, showFirstRun: false))
                        {
                            Sta.ShowOffScreen(f);
                            Button turnOn = Find<Button>(f, Strings.MainButton(ProtectionState.Off));
                            Assert.False(turnOn.Enabled);
                            Assert.Equal(Strings.OtherChangeRunning, c.StatusLineText);
                            Assert.True(c.IsWatchingOtherRun);
                            // Still going: nothing changes.
                            PumpFor(300);
                            Assert.True(c.Status.RunInProgress);
                            Assert.False(turnOn.Enabled);

                            Assert.False(f.PreviewStrictCard.Button.Enabled);

                            running.Dispose();
                            PumpUntil(() => !c.Status.RunInProgress);
                            Assert.False(c.Status.RunInProgress);
                            Assert.True(turnOn.Enabled);
                            // Off with Standard selected: "Use Strict…" works again.
                            Assert.True(f.PreviewStrictCard.Button.Enabled);
                            Assert.Equal(string.Empty, c.StatusLineText);
                            Assert.False(c.IsWatchingOtherRun);
                            f.Close();
                        }
                    }
                    finally
                    {
                        running.Dispose();
                    }
                });
            }
        }

        [Fact]
        public void A_change_asked_for_while_another_run_holds_the_mutex_shows_that_run_instead_of_a_windows_prompt()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                BlockLists lists = BlockLists.LoadEmbedded();
                var ctx = new UiContext(env, lists, @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                Sta.Run(() =>
                {
                    // The status was read before the other run started.
                    HostsStatus s = StatusReader.Read(env, lists);
                    Assert.False(s.RunInProgress);
                    int launches = 0;
                    var c = new AppController(ctx, s, Returns(ResultKind.Success, () => launches++));
                    using (var running = new Mutex(false, env.WriteMutexName))
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Task run = Start(f, () => c.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.TurnOn, f));
                        PumpUntil(() => run.IsCompleted);
                        Assert.True(run.IsCompleted);
                        Assert.Equal(0, launches);
                        Assert.True(c.Status.RunInProgress);
                        Assert.Equal(Strings.OtherChangeRunning, c.StatusLineText);
                        Assert.False(Find<Button>(f, Strings.MainButton(ProtectionState.Off)).Enabled);
                        f.Close();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- minimized, short screens

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_result_that_arrived_while_minimized_fits_after_the_restore(bool shortScreen)
        {
            Rectangle area = shortScreen ? new Rectangle(0, 0, 1366, 400) : new Rectangle(0, 0, 2560, 1400);
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var f = new MainForm(c, showFirstRun: false) { PreviewWorkingArea = area })
                    {
                        Sta.ShowOffScreen(f);
                        ShowWindow(f.Handle, SW_SHOWMINNOACTIVE);
                        PumpUntil(() => f.WindowState == FormWindowState.Minimized);
                        Assert.Equal(FormWindowState.Minimized, f.WindowState);
                        // Two more lines and Copy details while minimized.
                        c.PreviewSelfCheckFailed(new SelfCheckResult(false, "no A record in the DNS cache"), f);
                        ShowWindow(f.Handle, SW_SHOWNOACTIVATE);
                        PumpUntil(() => f.WindowState == FormWindowState.Normal);
                        PumpFor(150);

                        Assert.True(f.Height <= area.Height, "the window is " + f.Height + " px tall on a " + area.Height + " px screen");
                        if (shortScreen)
                        {
                            Assert.True(f.ContentScrolls);
                        }
                        else
                        {
                            Assert.False(f.ContentScrolls);
                            Button about = Find<Button>(f, Strings.AboutButton);
                            Rectangle r = f.RectangleToClient(about.RectangleToScreen(about.ClientRectangle));
                            Assert.True(r.Bottom <= f.ClientSize.Height, "About is below the window: " + r + " in " + f.ClientSize);
                        }
                        f.Close();
                    }
                });
            }
        }

        [Theory]
        [InlineData(0x007E, 0)]
        [InlineData(0x001A, 0x002F)]
        public void A_new_resolution_or_work_area_fits_the_window_again(int message, int wParam)
        {
            // WM_DISPLAYCHANGE (a new resolution or display scale), or WM_SETTINGCHANGE with SPI_SETWORKAREA (a moved or
            // resized taskbar): Windows sends them to every top-level window.
            Rectangle shorter = new Rectangle(0, 0, 1366, 400);
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    var c = new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On));
                    using (var f = new MainForm(c, showFirstRun: false))
                    {
                        Sta.ShowOffScreen(f);
                        Assert.False(f.ContentScrolls);
                        f.PreviewWorkingArea = shorter;
                        SendMessage(f.Handle, message, (IntPtr)wParam, IntPtr.Zero);
                        PumpFor(150);
                        Assert.True(f.ContentScrolls);
                        Assert.True(f.Height <= shorter.Height, "the window is " + f.Height + " px tall on a " + shorter.Height + " px screen");
                        f.Close();
                    }
                });
            }
        }

        [Fact]
        public void On_a_short_screen_the_window_opens_at_the_top_with_the_main_button_focused()
        {
            using (var p = new PreviewContext(RunLocationTips.RunningFromDownloads | RunLocationTips.RunningFromTemp))
            {
                Sta.Run(() =>
                {
                    HostsWarnings every = HostsWarnings.EditedBlock | HostsWarnings.MalformedMarkers | HostsWarnings.LargeFile |
                        HostsWarnings.MicrosoftLinesOutside | HostsWarnings.ChangeNotFinished | HostsWarnings.LegacyBlockToo;
                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: every, legacy: true, blockListVersion: UiScenarios.OlderListVersion);
                    // So short that the main button, near the top of the redesigned page, is below the first screenful.
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: false) { PreviewWorkingArea = UiScenarios.Scaled(UiScenarios.TinyScreen) })
                    {
                        Sta.ShowOffScreen(f);
                        PumpFor(100);
                        Assert.True(f.ContentScrolls);
                        ScrollHost host = ControlTree.All(f).OfType<ScrollHost>().Single();
                        Button main = Find<Button>(f, Strings.MainButton(ProtectionState.On));
                        Rectangle button = host.RectangleToClient(main.RectangleToScreen(main.ClientRectangle));
                        Assert.True(button.Bottom > host.ClientSize.Height, "the case this covers: the main button is below the first screenful");
                        Assert.True(main.Focused);
                        Assert.Equal(Point.Empty, host.AutoScrollPosition);
                        Label headline = Find<Label>(f, Strings.Headline(s.State, s.Refusal));
                        Rectangle top = host.RectangleToClient(headline.RectangleToScreen(headline.ClientRectangle));
                        Assert.True(top.Top >= 0 && top.Bottom <= host.ClientSize.Height, "the headline isn't visible: " + top);
                        f.Close();
                    }
                });
            }
        }
    }
}
