using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// The one window (spec 3): a small settings page. The status card (status shield, headline, detail, the main button and,
    /// while a change runs, the progress row), then at most three notices, the two blocking-level cards, the block list line,
    /// and the footer row (Fix a site or app…, Undo all changes…, About), which stays visible below the page when the page
    /// has to scroll (D15). On a user's first launch while protection is off the first-run text shows in place of the page,
    /// with its two buttons in the footer slot. The window is 576 epx wide and as tall as its content, up to the screen's
    /// work area minus 16 epx (D19); taller content scrolls. Enter never switches protection or the level (K2), and the
    /// window has no default button.
    /// </summary>
    internal sealed class MainForm : ThemedWindow
    {
        private readonly AppController _c;
        private readonly UiContext _ctx;
        private readonly Icon? _appIcon;

        private readonly ScrollHost _host;
        private readonly ThemedTable _page;
        private readonly StatusCard _status;
        private readonly NoticeArea _notices;
        private readonly ThemedLabel _levelNote;
        private readonly LevelCard _standardCard;
        private readonly LevelCard _strictCard;
        private readonly ThemedLabel _listDate;
        private readonly ThemedLink _newerVersion;
        private readonly CommandBar _footer;
        private readonly ThemedButton _fixButton;
        private readonly ThemedButton _undoButton;
        private readonly ThemedButton _aboutButton;
        private readonly FirstRunPanel? _firstRun;

        private bool _firstRunShown;
        private Control? _focusBeforeBusy;
        private FormWindowState _lastWindowState = FormWindowState.Normal;
        private string? _screenBeforeMove;
        // The notices as last worked out, the status they were worked out for, and their ids (to announce new ones).
        private IReadOnlyList<Notice> _shownNotices = Array.Empty<Notice>();
        private HostsStatus? _noticesStatus;
        private HashSet<NoticeId>? _noticeIds;

        public MainForm(AppController controller, bool showFirstRun)
        {
            _c = controller ?? throw new ArgumentNullException(nameof(controller));
            _ctx = controller.Context;
            UiScale s = Scale;
            int pagePad = s.Px(Metrics.PagePadding);
            int pageWidth = s.Px(Metrics.MainClientWidth);
            int content = pageWidth - 2 * pagePad;
            // The page's left padding leaves this much to the notices area, so "Show N more notices" can line its text up
            // with the bars' edge (a link's box has a 4 epx padding before its text); every other row is indented by it.
            int ringRoom = s.Px(4);

            Text = _ctx.MainTitle;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.Manual;
            _appIcon = AppIcon.Load();
            if (_appIcon != null) Icon = _appIcon;
            else ShowIcon = false;

            // ---- the page: status card, notices, level heading (and note), the level cards, the block list line
            _status = new StatusCard(Ramp, content) { Margin = new Padding(ringRoom, 0, 0, 0) };
            _notices = new NoticeArea(Ramp, content, ringRoom);
            var levelHeading = new ThemedLabel(Ramp, Strings.LevelHeading, TextStyle.BodyStrong) { Margin = new Padding(ringRoom, s.Px(Metrics.SectionGap), 0, 0) };
            _levelNote = new ThemedLabel(Ramp, Strings.LevelNote, TextStyle.Body, TextRole.Secondary, Metrics.MainClientWidth - 2 * Metrics.PagePadding)
            {
                Margin = new Padding(ringRoom, s.Px(4), 0, 0),
                Visible = false,
            };
            _standardCard = new LevelCard(Ramp, Level.Standard, Strings.StandardDescription, content) { Margin = new Padding(ringRoom, s.Px(Metrics.HeadingGap), 0, 0) };
            _strictCard = new LevelCard(Ramp, Level.Strict, Strings.StrictDescription(_ctx.Lists.StrictExtraCount), content)
            {
                Margin = new Padding(ringRoom, s.Px(Metrics.CardGap), 0, 0),
            };
            int right = LevelCard.RightColumnWidth(Ramp);
            _standardCard.RightWidth = right;
            _strictCard.RightWidth = right;

            // The date wraps instead of widening the window, whatever the hosts file's header says; the link follows it
            // 16 epx after its text (the gap between the letters: the link's padding and GDI's blank margins count), or on
            // the next line when both don't fit.
            _newerVersion = new ThemedLink(Ramp, Strings.CheckForNewerVersionLink) { External = true };
            _listDate = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary, Metrics.MainClientWidth - 2 * Metrics.PagePadding);
            int letterGap = s.Px(16) - _newerVersion.Padding.Left - GdiText.LeftMargin(_newerVersion.Font) - GdiText.RightMargin(_listDate.Font);
            _listDate.Margin = new Padding(0, _newerVersion.Padding.Top, Math.Max(0, letterGap), 0);
            // When the link goes on a line of its own (large text), its letters line up with the date's: the flow reaches the
            // link's padding further left than the date's box, which stays on the page's column.
            var listLine = new TextFlow(_listDate, _newerVersion);
            int reach = listLine.Inset - TextFlow.Lead(_listDate);
            listLine.MaximumSize = new Size(content + reach, 0);
            listLine.Margin = new Padding(Math.Max(0, ringRoom - reach), s.Px(12), 0, 0);

            _page = new ThemedTable(1)
            {
                Padding = new Padding(pagePad - ringRoom, pagePad, pagePad, 0),
                MinimumSize = new Size(pageWidth, 0),
                MaximumSize = new Size(pageWidth, 0),
            };
            Ui.AddRow(_page, _status);
            Ui.AddRow(_page, _notices);
            Ui.AddRow(_page, levelHeading);
            Ui.AddRow(_page, _levelNote);
            Ui.AddRow(_page, _standardCard);
            Ui.AddRow(_page, _strictCard);
            Ui.AddRow(_page, listLine);

            // ---- the footer slot below the page: the footer row (or the first run's buttons), 24 epx all round
            _fixButton = new ThemedButton(Ramp, Strings.FixButton);
            _undoButton = new ThemedButton(Ramp, Strings.UndoButton);
            _aboutButton = new ThemedButton(Ramp, Strings.AboutButton);
            _footer = new CommandBar(Ramp) { FixedWidth = pageWidth, ShowDivider = false };
            _footer.Padding = new Padding(pagePad);
            _footer.SetLeft(_fixButton);
            _footer.SetRight(_undoButton, _aboutButton);

            _host = AddScrollingContent(_footer);
            _host.Add(_page);
            if (showFirstRun)
            {
                _firstRun = new FirstRunPanel(Ramp, _ctx, controller.Status, pageWidth);
                _host.Add(_firstRun);
                _host.Content = _firstRun;
                _page.Visible = false;
                _footer.SetLeft(_firstRun.TurnOnButton, _firstRun.NotNowButton);
                _footer.SetRight();
                // Esc is Not now while the first run shows.
                CancelButton = _firstRun.NotNowButton;
                _firstRunShown = true;
                _firstRun.TurnOnButton.Click += OnFirstRunTurnOn;
                _firstRun.NotNowButton.Click += OnFirstRunNotNow;
                _firstRun.WhyLink.Invoked += (o, e) => ShowHowItWorks(HowItWorksSection.CantBlock);
            }

            _status.MainButton.Click += OnMainButton;
            _status.HowToLink.Invoked += (o, e) => OpenUrl(Strings.Utf8HelpUrl);
            _notices.ActionInvoked += OnNoticeAction;
            _notices.CloseRequested += OnNoticeClose;
            _notices.FocusPastArea += (o, e) => FirstEnabledAfterNotices()?.Focus();
            // "Show N more notices" makes the page taller: the window fits its screen again (3.12, D15).
            _notices.AllShown += (o, e) => Refit();
            _standardCard.Button.Click += OnUseStandard;
            _strictCard.Button.Click += OnUseStrict;
            if (_strictCard.Link != null) _strictCard.Link.Invoked += (o, e) => ShowStrictInfo();
            _newerVersion.Invoked += (o, e) => OpenUrl(Strings.ReleasesUrl);
            _fixButton.Click += OnFix;
            _undoButton.Click += OnUndo;
            _aboutButton.Click += OnAbout;

            _c.StatusChanged += OnStatusChanged;
            _c.BusyChanged += OnBusyChanged;
            _c.DismissalsChanged += OnDismissalsChanged;

            ShowStatus(atOpen: true);
            EndLayout();
        }

        /// <summary>The first-run text shows instead of the normal content.</summary>
        public bool IsShowingFirstRun => _firstRunShown;

        // ---------------------------------------------------------------- showing the state

        private void OnStatusChanged(object? sender, EventArgs e) => ShowStatus(atOpen: false);

        private void OnDismissalsChanged(object? sender, EventArgs e)
        {
            if (_c.IsBusy)
            {
                // While a change runs the notices aren't worked out again (3.5.4), but a closed bar still goes at once
                // (3.5.3): only the notices now closed leave the area; the others stay as they are, their actions disabled.
                List<Notice> remaining = _shownNotices.Where(n => !_c.Dismissals.Hides(n, _c.Status)).ToList();
                if (remaining.Count == _shownNotices.Count) return;
                _shownNotices = remaining;
                _notices.Show(null, remaining, canStartChange: false);
                UpdateMainDescription();
                Refit();
                return;
            }
            // A local recompute, without reading the file: the closed notice goes at once.
            RecomputeNotices(announceNew: false);
            Refit();
        }

        private void OnBusyChanged(object? sender, EventArgs e)
        {
            if (_c.IsBusy)
            {
                if (_focusBeforeBusy == null) _focusBeforeBusy = ActiveControl;
                // The result bar goes when a change starts; the other bars stay as they are, their actions disabled.
                _notices.Show(null, _shownNotices, canStartChange: false);
            }
            else
            {
                // The change has ended: its result and the notices of the status read after it. Notices that are new are
                // announced after the change's result, which AppController announces once this event is done (3.5.6).
                RecomputeNotices(announceNew: !ReferenceEquals(_noticesStatus, _c.Status), afterResult: true);
            }
            UpdateLevelCards();
            UpdateEnabled();
            UpdateProgress();
            Refit();
            if (_c.IsBusy) return;
            // Disabling the focused control lost the keyboard focus: give it back.
            Control? restore = FocusAfterChange(_focusBeforeBusy);
            _focusBeforeBusy = null;
            if (restore != null && IsForeground) restore.Focus();
        }

        /// <summary>
        /// Where the keyboard focus goes back after a change (spec 3.10): to the control that had it, if it is still visible
        /// and enabled (a declined "Use Standard" keeps it); otherwise to the main button (a "Use Standard" that worked became
        /// "In use", a "Use newer list" that worked went with its notice, Undo all changes is disabled after Undo); otherwise
        /// to "Fix a site or app…".
        /// </summary>
        private Control? FocusAfterChange(Control? saved)
        {
            if (saved != null && !saved.IsDisposed && saved.CanFocus && saved.FindForm() == this) return saved;
            if (_status.MainButton.CanFocus) return _status.MainButton;
            return _fixButton.CanFocus ? _fixButton : null;
        }

        /// <summary>The main window is the active window: no dialog over it, no other program in front.</summary>
        private bool IsForeground
        {
            get
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                if (PreviewForeground) return true;
#endif
                return Form.ActiveForm == this;
            }
        }

        private void ShowStatus(bool atOpen)
        {
            HostsStatus s = _c.Status;
            SuspendLayout();
            try
            {
                string headline = Strings.Headline(s.State, s.Refusal);
                _status.Show(s, headline, Strings.Detail(s, _c.OffForTest));
                // Never while this window's change runs: the bars stay as they are until it ends.
                if (!_c.IsBusy) RecomputeNotices(announceNew: !atOpen && !ReferenceEquals(_noticesStatus, s));
                UpdateLevelCards();
                // While On: the block's list date if it is a real date; otherwise this copy's.
                _listDate.Text = Strings.BlockListFrom(NoticeBuilder.BlockListVersion(s) ?? _ctx.Lists.Version);
                UpdateEnabled();
                UpdateProgress();
                UpdateMainDescription();
            }
            finally
            {
                ResumeLayout(true);
            }
            Refit();
        }

        /// <summary>
        /// Works the notices out again (spec 3.5.4) and announces the ones that are new after a re-read (3.5.6). At the end
        /// of a change (<paramref name="afterResult"/>) they are announced after the change's result, which comes right after
        /// this, so the result can't cut them off.
        /// </summary>
        private void RecomputeNotices(bool announceNew, bool afterResult = false)
        {
            IReadOnlyList<Notice> notices = NoticeBuilder.Build(_c.Status, _ctx, _c.Dismissals);
            _notices.Show(_c.LastResult, notices, _c.CanStartChange);
            var ids = new HashSet<NoticeId>(notices.Select(n => n.Id));
            if (announceNew && _noticeIds != null)
            {
                List<string> fresh = notices.Where(n => !_noticeIds.Contains(n.Id)).Select(n => n.AccessibleName).ToList();
                if (fresh.Count > 0)
                {
                    if (afterResult && IsHandleCreated) BeginInvoke(new Action(() => AnnounceNotices(fresh)));
                    else AnnounceNotices(fresh);
                }
            }
            _noticeIds = ids;
            _shownNotices = notices;
            _noticesStatus = _c.Status;
            UpdateMainDescription();
        }

        private void AnnounceNotices(IEnumerable<string> names)
        {
            if (IsDisposed) return;
            foreach (string name in names) Ui.Announce(this, name, Announcement.Notice);
        }

        /// <summary>
        /// The main button's description (spec 3.9): the headline, the detail and every notice that shows or is collapsed
        /// (not the result bar), which are plain text a keyboard user doesn't reach; the shield sentence follows.
        /// </summary>
        private void UpdateMainDescription()
        {
            HostsStatus s = _c.Status;
            var spoken = new StringBuilder(Strings.Headline(s.State, s.Refusal)).Append(". ").Append(Strings.Detail(s, _c.OffForTest));
            foreach (Notice n in _shownNotices) spoken.Append(' ').Append(n.AccessibleName);
            _status.MainButton.ContextDescription = spoken.ToString();
        }

        private void UpdateLevelCards()
        {
            HostsStatus s = _c.Status;
            (CardView standard, CardView strict, bool note) = LevelUi.For(s, _c.CanStartChange);
            _standardCard.Show(standard);
            _strictCard.Show(strict);
            _levelNote.Visible = note;
        }

        /// <summary>
        /// Change actions wait while a change runs, here or in another QuietHosts run. The main button of Can't read, Refused
        /// and the catch-all only reads the file again, so it waits only for this window's own change.
        /// </summary>
        private void UpdateEnabled()
        {
            bool canChange = _c.CanStartChange;
            HostsStatus s = _c.Status;
            _status.MainButton.Enabled = MainButtonChanges(s.State) ? canChange : !_c.IsBusy;
            _undoButton.Enabled = canChange && s.CanUndo;
            _notices.SetCanStartChange(canChange);
            if (_firstRun != null) _firstRun.TurnOnButton.Enabled = canChange;
        }

        /// <summary>The main button starts a change (Turn on, Turn off, Turn on again), not a new read of the file.</summary>
        private static bool MainButtonChanges(ProtectionState state) =>
            state == ProtectionState.On || state == ProtectionState.Off || state == ProtectionState.OlderList || state == ProtectionState.Removed;

        /// <summary>The progress row under the main button (spec 3.3): this window's change, or another QuietHosts run.</summary>
        private void UpdateProgress() => _status.SetProgress(_c.ActivityText("\r\n"));

        /// <summary>
        /// Keeps the window within its screen's working area minus 16 epx after its content changed: the page scrolls if it
        /// must (the footer row stays below it, with a divider line), and a window that now reaches below the screen moves up.
        /// </summary>
        private void Refit()
        {
            // A minimized window has no client area to measure; the next change refits it.
            if (!IsHandleCreated || WindowState == FormWindowState.Minimized) return;
            Rectangle area = WorkingArea(() => Screen.FromControl(this));
            FitToWorkingArea(area, Scale.Px(Metrics.MainHeightMargin));
            _footer.ShowDivider = ContentScrolls;
            if (Visible && !_ctx.Preview) KeepInside(area);
        }

        private void KeepInside(Rectangle area)
        {
            if (WindowState == FormWindowState.Normal && Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        /// <summary>Lays the window out again, then fits its screen: content that changed while it was minimized couldn't resize it.</summary>
        private void RelayoutAndRefit()
        {
            if (IsDisposed || !IsHandleCreated || WindowState != FormWindowState.Normal) return;
            PerformLayout();
            Refit();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FormWindowState state = WindowState;
            if (state == _lastWindowState) return;
            bool restored = _lastWindowState == FormWindowState.Minimized;
            _lastWindowState = state;
            // A result that arrived while minimized (or a second launch restoring the window): after the restore has finished.
            if (restored && IsHandleCreated) BeginInvoke(new Action(RelayoutAndRefit));
        }

        protected override void OnResizeBegin(EventArgs e)
        {
            base.OnResizeBegin(e);
            _screenBeforeMove = Screen.FromControl(this).DeviceName;
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            // Moved to another screen, perhaps a shorter one: fit that one. A move within one screen is left as it is.
            if (!string.Equals(Screen.FromControl(this).DeviceName, _screenBeforeMove, StringComparison.Ordinal)) Refit();
        }

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int SPI_SETWORKAREA = 0x002F;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // A new resolution or display scale, or a moved or resized taskbar: fit the screen again.
            if ((m.Msg == WM_DISPLAYCHANGE || (m.Msg == WM_SETTINGCHANGE && m.WParam == (IntPtr)SPI_SETWORKAREA)) && IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(RelayoutAndRefit));
        }

        // ---------------------------------------------------------------- actions

        private async void OnMainButton(object? sender, EventArgs e)
        {
            HostsStatus s = _c.Status;
            switch (s.State)
            {
                case ProtectionState.On:
                    await _c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOff, this);
                    break;
                case ProtectionState.Off:
                case ProtectionState.OlderList:
                case ProtectionState.Removed:
                    await _c.RunAsync(ElevatedCommand.On(s.SelectedLevel), ChangeKind.TurnOn, this);
                    break;
                default:
                    // Can't read, Refused, anything else: "Try again" and "Check again" read the file again.
                    await _c.RefreshAsync();
                    break;
            }
        }

        private async void OnNoticeAction(object? sender, NoticeEventArgs e)
        {
            Notice? n = e.Notice;
            if (n == null)
            {
                CopySelfCheckDetails();
                return;
            }
            HostsStatus s = _c.Status;
            switch (n.Action)
            {
                case NoticeAction.Repair:
                    ElevatedCommand? repair = AppController.RepairCommand(s);
                    if (repair != null && _c.CanStartChange) await _c.RunAsync(repair, ChangeKind.Repair, this);
                    break;
                case NoticeAction.UseNewerList:
                    // One "on" at the block's own level rewrites the block from this exe's lists and keeps the allowed
                    // addresses: one Windows prompt, and protection stays on even if the change fails.
                    if (s.State == ProtectionState.On && _c.CanStartChange) await _c.RunAsync(ElevatedCommand.On(s.BlockLevel ?? s.SelectedLevel), ChangeKind.UpdateList, this);
                    break;
                case NoticeAction.RemoveOlderList:
                    if (s.State == ProtectionState.On && _c.CanStartChange) await _c.RunAsync(ElevatedCommand.On(s.BlockLevel ?? s.SelectedLevel), ChangeKind.RemoveOlderList, this);
                    break;
                case NoticeAction.ShowLines:
                    using (var lines = new MicrosoftLinesDialog(_ctx, s)) lines.ShowDialog(this);
                    break;
                case NoticeAction.GetNewest:
                    OpenUrl(Strings.ReleasesUrl);
                    break;
                case NoticeAction.Why:
                    ShowHowItWorks(HowItWorksSection.OwnFolder);
                    break;
            }
        }

        /// <summary>
        /// A bar's close button (spec 3.5.3, 3.10): the result bar goes in every window; a notice is closed until its condition
        /// changes. The focus moves to the next bar, else the previous one, else the main button.
        /// </summary>
        private void OnNoticeClose(object? sender, NoticeEventArgs e)
        {
            IReadOnlyList<NoticeBar> bars = _notices.Bars;
            NoticeBar? closed = e.Notice == null ? _notices.ResultBar : _notices.BarFor(e.Notice.Id);
            int at = -1;
            for (int i = 0; i < bars.Count; i++)
            {
                if (ReferenceEquals(bars[i], closed)) at = i;
            }
            NoticeBar? next = at >= 0 && at + 1 < bars.Count ? bars[at + 1] : null;
            NoticeBar? previous = at > 0 ? bars[at - 1] : null;
            bool hadFocus = closed != null && closed.ContainsFocus;

            if (e.Notice == null) _c.CloseResult();
            else _c.DismissNotice(e.Notice.Id, e.Notice.Fingerprint ?? string.Empty);

            if (!hadFocus) return;
            Control? target = (next != null && !next.IsDisposed ? NoticeArea.FirstFocusable(next) : null)
                ?? (previous != null && !previous.IsDisposed ? NoticeArea.FirstFocusable(previous) : null)
                ?? (_status.MainButton.CanFocus ? _status.MainButton : null);
            target?.Focus();
        }

        /// <summary>After "Show N more notices" when the shown bars have nothing to focus: the first enabled control after them.</summary>
        private Control? FirstEnabledAfterNotices()
        {
            foreach (Control? c in new Control?[] { _standardCard.Button, _strictCard.Button, _strictCard.Link, _newerVersion, _fixButton, _undoButton, _aboutButton })
            {
                if (c != null && c.CanFocus) return c;
            }
            return null;
        }

        private void CopySelfCheckDetails()
        {
            if (!_c.CanCopySelfCheckDetails) return;
            bool copied = !_ctx.Preview && DetailsReport.TryCopy(_c.SelfCheckDetails());
            string feedback = copied || _ctx.Preview ? Strings.DetailsCopied : Strings.DetailsNotCopied;
            _notices.SetResultFeedback(feedback);
            Ui.Announce(this, feedback, Announcement.Result);
            Refit();
        }

        /// <summary>"Use Standard": while On one prompt at once (no confirmation); while off it is only saved.</summary>
        private async void OnUseStandard(object? sender, EventArgs e)
        {
            if (!_c.CanStartChange) return;
            if (_c.Status.State != ProtectionState.On) BeforeLevelSaved();
            await _c.ApplyLevelAsync(Level.Standard, this);
        }

        /// <summary>"Use Strict…": the confirmation first; its Use Strict carries the shield while protection is on.</summary>
        private async void OnUseStrict(object? sender, EventArgs e)
        {
            if (!_c.CanStartChange) return;
            bool on = _c.Status.State == ProtectionState.On;
            using (var confirm = new StrictDialog(_ctx, StrictDialogMode.Confirm, elevates: on))
            {
                // Cancel or Esc changes nothing: the card simply stays as it was.
                if (confirm.ShowDialog(this) != DialogResult.OK) return;
            }
            if (!on) BeforeLevelSaved();
            await _c.ApplyLevelAsync(Level.Strict, this);
        }

        /// <summary>
        /// A level chosen while off is only saved, and the pressed button becomes "Selected": the focus goes to the main button
        /// first (3.10), so the window never has to move it away from a button that disappears, and the announcement that the
        /// level is selected, which comes after the save, is the last thing a screen reader hears.
        /// </summary>
        private void BeforeLevelSaved()
        {
            if (IsDisposed || !IsForeground) return;
            if (_status.MainButton.CanFocus) _status.MainButton.Focus();
        }

        private void ShowStrictInfo()
        {
            using (var info = new StrictDialog(_ctx, StrictDialogMode.Info, elevates: false)) info.ShowDialog(this);
        }

        private void ShowHowItWorks(HowItWorksSection start)
        {
            using (var how = new HowItWorksDialog(_ctx, start)) how.ShowDialog(this);
        }

        private void OnFix(object? sender, EventArgs e)
        {
            using (var fix = new FixDialog(_c)) fix.ShowDialog(this);
        }

        private async void OnUndo(object? sender, EventArgs e)
        {
            bool confirmed;
            using (var undo = new UndoDialog(_c.Status)) confirmed = undo.ShowDialog(this) == DialogResult.OK;
            if (confirmed) await _c.RunAsync(ElevatedCommand.Undo(), ChangeKind.Undo, this);
        }

        private void OnAbout(object? sender, EventArgs e)
        {
            using (var about = new AboutDialog(_c)) about.ShowDialog(this);
        }

        private void OpenUrl(string url)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewOpenUrl != null)
            {
                PreviewOpenUrl(url);
                return;
            }
#endif
            Links.OpenUrl(_ctx, this, url);
        }

        // ---------------------------------------------------------------- first run

        private async void OnFirstRunTurnOn(object? sender, EventArgs e)
        {
            _c.MarkFirstRunDone();
            HideFirstRun();
            // The first-run button always uses Standard.
            await _c.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.TurnOn, this);
        }

        private void OnFirstRunNotNow(object? sender, EventArgs e)
        {
            _c.MarkFirstRunDone();
            HideFirstRun();
        }

        /// <summary>The page replaces the first-run text, and the footer row replaces its buttons in the footer slot.</summary>
        private void HideFirstRun()
        {
            if (_firstRun == null || !_firstRunShown) return;
            SuspendLayout();
            _firstRun.Visible = false;
            _page.Visible = true;
            _host.Content = _page;
            _footer.SetLeft(_fixButton);
            _footer.SetRight(_undoButton, _aboutButton);
            CancelButton = null;
            _firstRunShown = false;
            ResumeLayout(true);
            Refit();
            FocusFirstControl();
        }

        /// <summary>
        /// The main button gets the focus ("Fix a site or app…" while another QuietHosts run makes it wait), or on the first
        /// run Turn on protection (Not now while it waits). Content that scrolls starts at the top, with the headline.
        /// </summary>
        private void FocusFirstControl()
        {
            Control first = _firstRunShown && _firstRun != null
                ? (_firstRun.TurnOnButton.CanFocus ? _firstRun.TurnOnButton : (Control)_firstRun.NotNowButton)
                : (_status.MainButton.CanFocus ? (Control)_status.MainButton : _fixButton);
            first.Focus();
            _host.ScrollToTop();
        }

        // ---------------------------------------------------------------- keyboard

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                if (!_c.IsBusy) _ = _c.RefreshAsync();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Enter on the main button, the level-card buttons, the notice buttons with the shield and the first run's Turn on
        // protection: swallowed by ThemedWindow and by the buttons themselves (SwallowsEnter, K2). There is no AcceptButton.

        // ---------------------------------------------------------------- position (per user, centered if off-screen)

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_ctx.Preview)
            {
                Refit();
                return;
            }
            UserSettings u = _c.ReadUserSettings();
            var saved = new Rectangle(u.MainX ?? 0, u.MainY ?? 0, Width, Height);
            bool restore = u.MainX.HasValue && u.MainY.HasValue && IsOnAScreen(saved);
            // The screen it will open on: the remembered one, or the one CenterToScreen uses (under the mouse).
            Rectangle area = WorkingArea(() => restore ? Screen.FromRectangle(saved) : Screen.FromPoint(MousePosition));
            FitToWorkingArea(area, Scale.Px(Metrics.MainHeightMargin));
            _footer.ShowDivider = ContentScrolls;
            if (restore)
            {
                Location = saved.Location;
                KeepInside(area);
            }
            else
            {
                CenterToScreen();
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FocusFirstControl();
            // Why the change actions are disabled, for a screen reader (the focus is on another button then).
            if (!_c.IsBusy && _c.Status.RunInProgress) Ui.Announce(this, _c.StatusLineText, Announcement.Progress);
        }

        private bool IsOnAScreen(Rectangle bounds)
        {
            // The title bar must be on a screen, so the window can be moved back.
            var titleBar = new Rectangle(bounds.X, bounds.Y, bounds.Width, Scale.Px(30));
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle visible = Rectangle.Intersect(screen.WorkingArea, titleBar);
                if (visible.Width >= Math.Min(bounds.Width, Scale.Px(120)) && visible.Height >= Scale.Px(20)) return true;
            }
            return false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            Rectangle b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _c.SaveMainPosition(b.X, b.Y);
        }

        /// <summary>A second launch asked to bring this window forward.</summary>
        public void ShowFromSecondLaunch()
        {
            if (IsDisposed) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Form target = this;
            foreach (Form f in Application.OpenForms)
            {
                if (f != this && f.Visible && f.Modal) target = f;
            }
            target.Activate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _c.StatusChanged -= OnStatusChanged;
                _c.BusyChanged -= OnBusyChanged;
                _c.DismissalsChanged -= OnDismissalsChanged;
                // The first run's buttons left the footer slot when the page replaced it.
                if (_firstRun != null && !_firstRunShown)
                {
                    _firstRun.TurnOnButton.Dispose();
                    _firstRun.NotNowButton.Dispose();
                }
            }
            base.Dispose(disposing);
            if (disposing) _appIcon?.Dispose();
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For tests: fit to <see cref="ThemedWindow.PreviewWorkingArea"/> as showing the window would.</summary>
        internal void PreviewFit() => Refit();

        /// <summary>For tests: act as the active window (a test's window never is), so the focus comes back after a change.</summary>
        internal bool PreviewForeground { get; set; }

        /// <summary>For tests: links open here instead of in the browser (a preview opens nothing anyway).</summary>
        internal Action<string>? PreviewOpenUrl { get; set; }

        /// <summary>For tests: the notices' texts, shown and collapsed, in order (not the result bar).</summary>
        internal IReadOnlyList<string> PreviewNoticeTexts => _shownNotices.Select(n => n.Message).ToList();

        /// <summary>For tests: the notices as worked out last.</summary>
        internal IReadOnlyList<Notice> PreviewNotices => _shownNotices;

        /// <summary>For --render-ui and tests: as if "Show N more notices" was clicked.</summary>
        internal void PreviewExpandNotices() => _notices.PreviewExpand();

        /// <summary>For --render-ui: the progress ring as with Windows' animations off.</summary>
        internal void PreviewAnimationsOff()
        {
            _status.Ring.PreviewAnimations = false;
            _status.SetProgress(null);
            UpdateProgress();
        }

        /// <summary>For --render-ui and tests: the result bar's Copy details, as if clicked.</summary>
        internal void PreviewCopySelfCheckDetails() => CopySelfCheckDetails();

        internal StatusCard PreviewStatusCard => _status;
        internal NoticeArea PreviewNoticeArea => _notices;
        internal LevelCard PreviewStandardCard => _standardCard;
        internal LevelCard PreviewStrictCard => _strictCard;
        internal ThemedLabel PreviewLevelNote => _levelNote;
        internal CommandBar PreviewFooter => _footer;
        internal ScrollHost PreviewHost => _host;
        internal ThemedTable PreviewPage => _page;
        internal ThemedLink PreviewNewerVersionLink => _newerVersion;
        internal ThemedLabel PreviewListDate => _listDate;
        internal ThemedButton PreviewFixButton => _fixButton;
        internal ThemedButton PreviewUndoButton => _undoButton;
        internal ThemedButton PreviewAboutButton => _aboutButton;
        internal FirstRunPanel? PreviewFirstRun => _firstRun;
#endif
    }

    /// <summary>
    /// The main window's status card (spec 3.1): the 64 epx status shield at the top left; beside it, from 100 epx, a 408
    /// epx text column with the headline (Subtitle), 4 epx, the detail (Body, secondary), in S10 4 epx and "How to do this",
    /// 16 epx, the main button, and while a change runs 8 epx and the progress row (the ring centered on the first line of
    /// its text). Padding 20; as tall as the shield or the column. The card is never tinted; only the shield has color.
    /// </summary>
    internal sealed class StatusCard : CardPanel
    {
        private readonly TypeRamp _ramp;
        private readonly int _width;
        private bool _howTo, _progress;

        public StatusCard(TypeRamp ramp, int widthPx) : base(Metrics.StatusCardPadding, withContent: false)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            _width = widthPx;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            SetFixedWidth(widthPx);
            Shield = new ShieldIcon();
            // Tight: their letters start on the text column, where the main button's left edge is (3.1), not GDI's margin
            // further right.
            Headline = new ThemedLabel(ramp, string.Empty, TextStyle.Subtitle, TextRole.Primary, Metrics.StatusTextWidth) { Tight = true };
            Detail = new ThemedLabel(ramp, string.Empty, TextStyle.Body, TextRole.Secondary, Metrics.StatusTextWidth) { Tight = true };
            HowToLink = new ThemedLink(ramp, Strings.HowToSaveAsUtf8Link) { External = true, Visible = false };
            // Enter never switches protection (K2): Space, the access key and a click do.
            MainButton = new ThemedButton(ramp, Strings.MainButtonTurnOn, ButtonStyle.Accent) { SwallowsEnter = true };
            Ring = new ProgressRing { Visible = false };
            ProgressLabel = new ThemedLabel(ramp, string.Empty, TextStyle.Body, TextRole.Secondary, Metrics.StatusTextWidth - Metrics.ProgressRing - Metrics.ButtonGap)
            {
                Visible = false,
            };
            Controls.Add(Shield);
            Controls.Add(Headline);
            Controls.Add(Detail);
            Controls.Add(HowToLink);
            Controls.Add(MainButton);
            Controls.Add(Ring);
            Controls.Add(ProgressLabel);
            ApplyTheme(ThemeManager.Current);
        }

        public ShieldIcon Shield { get; }
        public ThemedLabel Headline { get; }
        public ThemedLabel Detail { get; }
        /// <summary>S10 only: the README's steps for saving the file as UTF-8 (above the main button).</summary>
        public ThemedLink HowToLink { get; }
        public ThemedButton MainButton { get; }
        public ProgressRing Ring { get; }
        public ThemedLabel ProgressLabel { get; }

        /// <summary>True while the progress row shows.</summary>
        public bool ShowsProgress => _progress;

        /// <summary>Shows a status (spec 3.2): the shield, the texts and the main button's text, style and shield.</summary>
        public void Show(HostsStatus s, string headline, string detail)
        {
            SuspendLayout();
            try
            {
                Shield.Kind = ShieldIcon.KindFor(s.State);
                Shield.AccessibleName = headline;
                Headline.Text = headline;
                Detail.Text = detail;
                MainButton.Text = Strings.MainButton(s.State);
                (ButtonStyle style, bool shield) = MainButtonLook(s);
                MainButton.Style = style;
                MainButton.Shield = shield;
                _howTo = s.State == ProtectionState.Refused && s.Refusal == RefusalReason.UnsupportedEncoding;
                HowToLink.Visible = _howTo;
            }
            finally
            {
                ResumeLayout(false);
            }
            Relayout();
        }

        /// <summary>
        /// The main button's look (spec 3.2): accent where turning on (or reading again after Can't read) is the next step,
        /// standard for Turn off protection and Check again; the shield on every button whose click shows the Windows prompt.
        /// </summary>
        public static (ButtonStyle Style, bool Shield) MainButtonLook(HostsStatus s)
        {
            switch (s.State)
            {
                case ProtectionState.Off:
                case ProtectionState.OlderList:
                case ProtectionState.Removed:
                    return (ButtonStyle.Accent, true);
                case ProtectionState.On:
                    return (ButtonStyle.Standard, true);
                case ProtectionState.CantRead:
                    return (ButtonStyle.Accent, false);
                default:
                    return (ButtonStyle.Standard, false);
            }
        }

        /// <summary>The progress row's text (lines separated by CRLF), or null or empty to hide the row.</summary>
        public void SetProgress(string? text)
        {
            bool show = !string.IsNullOrEmpty(text);
            SuspendLayout();
            try
            {
                ProgressLabel.Text = show ? text! : string.Empty;
                ProgressLabel.Visible = show;
                Ring.Visible = show;
                Ring.AccessibleName = show ? FirstLine(text!) : null;
                Ring.Active = show;
            }
            finally
            {
                ResumeLayout(false);
            }
            if (show == _progress && !show) return;
            _progress = show;
            Relayout();
        }

        private static string FirstLine(string text)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' });
            return end < 0 ? text : text.Substring(0, end);
        }

        private void Relayout()
        {
            Parent?.PerformLayout(this, nameof(Bounds));
            PerformLayout();
            Invalidate();
        }

        // ---------------------------------------------------------------- layout

        public override Size GetPreferredSize(Size proposedSize) => new Size(_width, Arrange(apply: false));

        protected override void OnLayout(LayoutEventArgs levent)
        {
            // The base constructor's padding lays out before this card's parts exist.
            if (ClientSize.Width <= 0 || ProgressLabel == null) return;
            SuspendLayout();
            try
            {
                Arrange(apply: true);
            }
            finally
            {
                ResumeLayout(false);
            }
        }

        private int Arrange(bool apply)
        {
            if (ProgressLabel == null) return 0;
            UiScale s = _ramp.Scale;
            int pad = s.Px(Metrics.StatusCardPadding);
            int shield = s.Px(Metrics.StatusShield);
            int x = pad + shield + s.Px(16);
            int column = s.Px(Metrics.StatusTextWidth);
            if (apply) Shield.SetBounds(pad, pad, shield, shield);

            int y = pad;
            Size headline = Headline.GetPreferredSize(new Size(column, 0));
            if (apply) Headline.SetBounds(x, y, Math.Min(column, headline.Width), headline.Height);
            y += headline.Height + s.Px(4);
            Size detail = Detail.GetPreferredSize(new Size(column, 0));
            if (apply) Detail.SetBounds(x, y, Math.Min(column, detail.Width), detail.Height);
            y += detail.Height;
            if (_howTo)
            {
                y += s.Px(4);
                Size link = HowToLink.GetPreferredSize(Size.Empty);
                // The link's text lines up with the detail's (its box has a 4 epx padding and GDI's margin before the text).
                if (apply) HowToLink.SetBounds(x - TextFlow.Lead(HowToLink), y, link.Width, link.Height);
                y += link.Height;
            }
            y += s.Px(Metrics.TextToButton);
            Size button = MainButton.GetPreferredSize(Size.Empty);
            if (apply) MainButton.SetBounds(x, y, button.Width, button.Height);
            y += button.Height;
            if (_progress)
            {
                y += s.Px(Metrics.ButtonGap);
                int ring = s.Px(Metrics.ProgressRing);
                int textX = x + ring + s.Px(Metrics.ButtonGap);
                int textWidth = Math.Max(1, column - ring - s.Px(Metrics.ButtonGap));
                Size label = ProgressLabel.GetPreferredSize(new Size(textWidth, 0));
                // The ring is centered on the first line of the text.
                int line = Math.Max(_ramp.LineHeight(TextStyle.Body), ProgressLabel.Font.Height);
                if (apply)
                {
                    Ring.SetBounds(x, y + Math.Max(0, (Math.Min(line, label.Height) - ring) / 2), ring, ring);
                    ProgressLabel.SetBounds(textX, y, Math.Min(textWidth, label.Width), label.Height);
                }
                y += label.Height;
            }
            return Math.Max(pad + shield + pad, y + pad);
        }
    }

    /// <summary>
    /// A transparent left-to-right flow that wraps (the block list line, the first run's limit line), double-buffered like
    /// every panel of the redesign (2.8).
    /// </summary>
    internal sealed class ThemedFlow : FlowLayoutPanel, IThemed
    {
        public ThemedFlow()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            BackColor = Color.Transparent;
            // A layout panel has no name (Windows would otherwise name it after a label before it).
            AccessibleName = string.Empty;
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = Color.Transparent;
            Invalidate();
        }
    }

    /// <summary>The app icon (QuietHosts.ico, embedded), for the title bar and taskbar.</summary>
    internal static class AppIcon
    {
        public const string ResourceName = "QuietHosts.Icon.ico";

        public static Icon? Load()
        {
            try
            {
                using (Stream? s = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName))
                {
                    return s == null ? null : new Icon(s);
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
