using System;
using System.Security;
using System.Threading.Tasks;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>Why a change runs; it picks the result (spec 3.4).</summary>
    internal enum ChangeKind
    {
        /// <summary>Turn on (also "Turn on again" and the first-run button): the self-check runs afterwards.</summary>
        TurnOn,
        TurnOff,
        /// <summary>The Fix dialog's "Turn off for now": like TurnOff, and the main window then says it was a test (S2).</summary>
        TurnOffForTest,
        /// <summary>A level change while protection is on.</summary>
        ChangeLevel,
        Allow,
        Block,
        Undo,
        /// <summary>"Use newer list": the block rewritten from this exe's lists at the same level.</summary>
        UpdateList,
        /// <summary>A notice's Repair: the block rewritten at its level (On), or the damaged lines cleaned (Off).</summary>
        Repair,
        /// <summary>"Remove it now": the block rewritten at its level, which drops the older block-ads.ps1 list.</summary>
        RemoveOlderList,
    }

    /// <summary>The result of one change as the windows show it: the result bar's text and look (spec 3.4).</summary>
    internal sealed class ChangeResult
    {
        public ChangeResult(ChangeKind kind, ResultKind result, string text, NoticeSeverity severity, bool offersSelfCheckDetails = false)
        {
            Kind = kind;
            Result = result;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Severity = severity;
            OffersSelfCheckDetails = offersSelfCheckDetails;
        }

        public ChangeKind Kind { get; }
        public ResultKind Result { get; }
        public string Text { get; }
        /// <summary>Success, Info or Warning.</summary>
        public NoticeSeverity Severity { get; }
        /// <summary>R2: the self-check failed; the bar offers "Copy details".</summary>
        public bool OffersSelfCheckDetails { get; }
    }

    internal sealed class ChangeFinishedEventArgs : EventArgs
    {
        public ChangeFinishedEventArgs(ChangeKind kind, ResultKind result, string? line, Form? origin, NoticeSeverity severity = NoticeSeverity.Success)
        {
            Kind = kind;
            Result = result;
            Line = line;
            Origin = origin;
            Severity = severity;
        }

        public ChangeKind Kind { get; }
        public ResultKind Result { get; }
        /// <summary>The result bar's text, or null when there is no bar (an error dialog, or a refusal the status explains).</summary>
        public string? Line { get; }
        /// <summary>The window whose button started the change.</summary>
        public Form? Origin { get; }
        /// <summary>The result bar's severity (Success when there is no bar).</summary>
        public NoticeSeverity Severity { get; }
    }

    /// <summary>
    /// Starts one elevated run and waits for it off the UI thread, as <see cref="ElevatedLauncher.RunAsync"/> does: the
    /// command, where its stages go, and the window that owns the Windows prompt (or zero).
    /// </summary>
    internal delegate Task<LaunchResult> LaunchChange(ElevatedCommand command, IProgress<LaunchStage> progress, IntPtr owner);

    /// <summary>
    /// The window side of every change: disables actions, shows "Waiting for your permission…" then
    /// "Changing the hosts file…" (and, after 30 s, that it takes longer than usual), runs the elevated run off the UI
    /// thread, re-reads the status, and shows the result as the result bar (<see cref="LastResult"/>) or an error dialog
    /// ("Try again" reruns the same change). While another QuietHosts run holds the write mutex, change actions wait and the
    /// status is read again when it ends. It also keeps the session's state: the closed notices and whether protection was
    /// turned off to test a site. The main window and the Fix dialog both listen to it, so they always show the same state.
    /// </summary>
    internal sealed class AppController
    {
        private static readonly TimeSpan SelfCheckRetryDelay = TimeSpan.FromMilliseconds(700);
        private const int SelfCheckAttempts = 3;
        /// <summary>How often the window looks whether another QuietHosts run still holds the write mutex.</summary>
        private const int RunWatchIntervalMs = 2000;
        private readonly LaunchChange? _launch;
        private readonly Timer? _runWatch;
#if DEBUG || QUIETHOSTS_UI_TESTS
        private readonly Func<HostsStatus>? _readStatus;
#endif

        public AppController(UiContext ctx, HostsStatus initial)
        {
            Context = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Status = initial ?? throw new ArgumentNullException(nameof(initial));
            Dismissals = LoadDismissals();
            if (ctx.Preview) return;
            _launch = new ElevatedLauncher(ctx.Env, ctx.ExePath).RunAsync;
            _runWatch = new Timer { Interval = RunWatchIntervalMs };
            _runWatch.Tick += OnRunWatchTick;
            UpdateRunWatch();
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>
        /// For tests: changes go to <paramref name="launch"/> instead of starting QuietHosts.exe, and each status read after a
        /// change (or a refresh) comes from <paramref name="readStatus"/> (by default the status stays as it is).
        /// </summary>
        internal AppController(UiContext ctx, HostsStatus initial, LaunchChange launch, Func<HostsStatus>? readStatus = null)
        {
            Context = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Status = initial ?? throw new ArgumentNullException(nameof(initial));
            _launch = launch ?? throw new ArgumentNullException(nameof(launch));
            _readStatus = readStatus;
            Dismissals = LoadDismissals();
        }
#endif

        public UiContext Context { get; }
        public HostsStatus Status { get; private set; }
        /// <summary>A change is running: action buttons are disabled.</summary>
        public bool IsBusy { get; private set; }
        /// <summary>"Waiting for your permission…" or "Changing the hosts file…" while busy.</summary>
        public string? ProgressText { get; private set; }
        /// <summary>While busy: the elevated run has gone on for 30 s (LaunchStage.TakingLong); <see cref="Strings.ChangingSlow"/> shows too.</summary>
        public bool TakingLong { get; private set; }

        /// <summary>
        /// A change can start: none is running here, and no other QuietHosts run holds the write mutex (a change started now
        /// could only end in Busy). Reading the file again and everything that changes nothing stay available.
        /// </summary>
        public bool CanStartChange => !IsBusy && !Status.RunInProgress;

        /// <summary>A change is running here, or another QuietHosts run is: the progress row shows <see cref="ActivityText"/>.</summary>
        public bool ShowsActivity => IsBusy || Status.RunInProgress;

        /// <summary>The last result (the result bar), until the next change, a manual re-read or its close button.</summary>
        public ChangeResult? LastResult { get; private set; }

        /// <summary>The last result's text (tests and announcements).</summary>
        public string? ResultLine => LastResult?.Text;

        /// <summary>
        /// Protection was turned off in this session to test a site (the Fix dialog's "Turn off for now"): the main window
        /// says so (S2). Cleared by any other successful change and whenever a read status isn't Off.
        /// </summary>
        public bool OffForTest { get; private set; }

        /// <summary>The notices the user closed (loaded from the per-user store; nothing is loaded in a preview).</summary>
        public NoticeDismissals Dismissals { get; private set; }

        /// <summary>After a successful Undo, the window stops saving per-user settings (so nothing is left behind).</summary>
        public bool UserSettingsDeleted { get; private set; }
        /// <summary>The self-check of the last Turn on (null after any other change).</summary>
        public SelfCheckResult? LastSelfCheck { get; private set; }
        private ElevatedCommand? _selfCheckCommand;
        private LaunchResult? _selfCheckLaunch;

        /// <summary>The result bar says the self-check failed: "Copy details" in it copies what it found.</summary>
        public bool CanCopySelfCheckDetails =>
            !IsBusy && LastSelfCheck != null && !LastSelfCheck.Passed && LastResult != null && LastResult.OffersSelfCheckDetails;

        /// <summary>The details of the Turn on whose self-check failed, with what the self-check found.</summary>
        public string SelfCheckDetails() => DetailsReport.Build(Context, _selfCheckCommand, _selfCheckLaunch, Status, null, LastSelfCheck);

        public event EventHandler? StatusChanged;
        public event EventHandler? BusyChanged;
        public event EventHandler<ChangeFinishedEventArgs>? ChangeFinished;
        /// <summary>A notice was closed, or the closed notices were forgotten (Undo).</summary>
        public event EventHandler? DismissalsChanged;

        /// <summary>The progress while a change runs (here or in another QuietHosts run), otherwise the last result's text.</summary>
        public string StatusLineText => ShowsActivity ? ActivityText("\r\n") : ResultLine ?? string.Empty;

        /// <summary>
        /// The line about a change in progress: this window's progress, with <see cref="Strings.ChangingSlow"/> after
        /// <paramref name="separator"/> once the run has gone on for 30 s; or, while another QuietHosts run is changing the
        /// file, <see cref="Strings.OtherChangeRunning"/>. Empty when neither (or while an error dialog shows).
        /// </summary>
        public string ActivityText(string separator)
        {
            if (IsBusy)
            {
                if (ProgressText == null) return string.Empty;
                return TakingLong ? ProgressText + separator + Strings.ChangingSlow : ProgressText;
            }
            return Status.RunInProgress ? Strings.OtherChangeRunning : string.Empty;
        }

        public void SetStatus(HostsStatus status)
        {
            Status = status ?? throw new ArgumentNullException(nameof(status));
            // "Off after a test" lasts only while the file says Off.
            if (status.State != ProtectionState.Off) OffForTest = false;
            UpdateRunWatch();
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Re-reads the hosts file and settings (F5, "Check again", "Try again"); the result bar goes.</summary>
        public async Task RefreshAsync()
        {
            if (IsBusy) return;
            LastResult = null;
            SetStatus(await ReadStatusAsync());
        }

        private Task<HostsStatus> ReadStatusAsync()
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (_readStatus != null) return Task.FromResult(_readStatus());
#endif
            return Context.Preview ? Task.FromResult(Status) : Task.Run(() => StatusReader.Read(Context.Env, Context.Lists));
        }

        /// <summary>
        /// Polls only while the status says another QuietHosts run holds the write mutex and this window runs no change of its
        /// own. Nothing is polled with fake statuses (--render-ui, most tests).
        /// </summary>
        private void UpdateRunWatch()
        {
            if (_runWatch != null) _runWatch.Enabled = !IsBusy && Status.RunInProgress;
        }

        private void OnRunWatchTick(object? sender, EventArgs e)
        {
            if (IsBusy || !Status.RunInProgress)
            {
                UpdateRunWatch();
                return;
            }
            // Only whether the mutex still exists; the file is read once the other run has ended.
            if (WriteMutex.IsRunInProgress(Context.Env.WriteMutexName)) return;
            if (_runWatch != null) _runWatch.Enabled = false;
            _ = RefreshAsync();
        }

        /// <summary>A level chosen while protection is off is saved per user, without a Windows prompt.</summary>
        public async Task SavePendingLevelAsync(Level level)
        {
            if (IsBusy) return;
            if (!Context.Preview)
            {
                TrySettings(() => Context.Env.User.SetPendingLevel(level));
                UserSettingsDeleted = false;
            }
            await RefreshAsync();
        }

        /// <summary>
        /// A level card's button (spec 3.6): while On, one elevated "on" at that level; while Off, Older list or Removed, the
        /// level is only saved for the next Turn on (no prompt) and screen readers hear that it is selected. Nothing happens
        /// in any other state, while a change can't start, or for the level already selected.
        /// </summary>
        public async Task ApplyLevelAsync(Level level, Form origin)
        {
            HostsStatus s = Status;
            if (!CanStartChange || level == s.SelectedLevel) return;
            switch (s.State)
            {
                case ProtectionState.On:
                    await RunAsync(ElevatedCommand.On(level), ChangeKind.ChangeLevel, origin);
                    break;
                case ProtectionState.Off:
                case ProtectionState.OlderList:
                case ProtectionState.Removed:
                    await SavePendingLevelAsync(level);
                    Form? window = ValidOwner(origin);
                    if (window != null) Ui.Announce(window, Strings.LevelSavedAnnouncement(level), Announcement.Result);
                    break;
            }
        }

        public void MarkFirstRunDone()
        {
            if (Context.Preview) return;
            TrySettings(() => Context.Env.User.SetFirstRunDone(true));
            UserSettingsDeleted = false;
        }

        public UserSettings ReadUserSettings()
        {
            try { return Context.Env.User.Read(); }
            catch (Exception ex) when (IsSettingsError(ex)) { return UserSettings.Empty; }
        }

        public void SaveMainPosition(int x, int y)
        {
            if (Context.Preview || UserSettingsDeleted) return;
            TrySettings(() => Context.Env.User.SetMainPosition(x, y));
        }

        public void SaveFixSize(int width, int height)
        {
            if (Context.Preview || UserSettingsDeleted) return;
            TrySettings(() => Context.Env.User.SetFixSize(width, height));
        }

        // ---------------------------------------------------------------- notices

        private NoticeDismissals LoadDismissals() =>
            Context.Preview ? NoticeDismissals.Empty : NoticeDismissals.From(ReadUserSettings().DismissedNotices);

        /// <summary>
        /// A closed notice (spec 3.5.3): hidden at once until its condition (<paramref name="fingerprint"/>) changes, and stored
        /// per user, except in a preview and after a successful Undo in this session (it deleted the per-user settings).
        /// </summary>
        public void DismissNotice(NoticeId id, string fingerprint)
        {
            Dismissals = Dismissals.With(id, fingerprint);
            if (!Context.Preview && !UserSettingsDeleted)
            {
                NoticeDismissals stored = Dismissals;
                TrySettings(() => Context.Env.User.SetDismissedNotices(stored.Entries));
            }
            DismissalsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The result bar's close button: the bar goes in every window (and with it the self-check's Copy details); nothing is stored.</summary>
        public void CloseResult()
        {
            if (LastResult == null) return;
            LastResult = null;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// The command a notice's Repair runs (spec 3.5.1): On rewrites the block at its level; Off cleans damaged QuietHosts
        /// lines and keeps protection off; in any other state there is none (the main button's next change repairs too).
        /// </summary>
        public static ElevatedCommand? RepairCommand(HostsStatus s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            switch (s.State)
            {
                case ProtectionState.On: return ElevatedCommand.On(s.BlockLevel ?? s.SelectedLevel);
                case ProtectionState.Off: return ElevatedCommand.Off();
                default: return null;
            }
        }

        // ---------------------------------------------------------------- changes

        /// <summary>Runs one change. Returns when the result is shown (in the result bar or an error dialog).</summary>
        public async Task RunAsync(ElevatedCommand command, ChangeKind kind, Form origin)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (IsBusy || _launch == null) return;
            if (!Context.Preview && WriteMutex.IsRunInProgress(Context.Env.WriteMutexName))
            {
                // Another QuietHosts run started after the status was read: its Windows prompt could only end in Busy. Show
                // that run instead; the window reads the file again when it ends.
                await RefreshAsync();
                Form? window = ValidOwner(origin);
                if (window != null) Ui.Announce(window, StatusLineText, Announcement.Progress);
                return;
            }
            Level? pendingBefore = Status.PendingLevel;
            ChangeFinishedEventArgs finished;
            string? announcement;
            LastResult = null;
            LastSelfCheck = null;
            SetBusy(Strings.WaitingForPermission);
            try
            {
                while (true)
                {
                    var progress = new Progress<LaunchStage>(stage => OnProgress(stage, origin));
                    // Read on the UI thread: Windows shows its prompt as that window's, in front of it (Try again may come
                    // after the window closed, so each attempt looks again).
                    Form? owner = ValidOwner(origin);
                    LaunchResult r = await _launch(command, progress, owner != null ? owner.Handle : IntPtr.Zero);
                    if (r.Kind == ResultKind.Success) AfterSuccess(kind, command, pendingBefore);
                    HostsStatus status = await ReadStatusAsync();

                    if (IsErrorDialogKind(r.Kind))
                    {
                        // The window snaps back to what the file says, then the dialog explains.
                        SetBusy(null);
                        SetStatus(status);
                        DialogResult choice = ErrorDialog.ShowForResult(ValidOwner(origin), Context, r,
                            () => DetailsReport.Build(Context, command, r, Status, null));
                        if (choice == DialogResult.Retry)
                        {
                            SetBusy(Strings.WaitingForPermission);
                            continue;
                        }
                        LastResult = null;
                        announcement = null;
                        SetStatus(status);
                        finished = new ChangeFinishedEventArgs(kind, r.Kind, null, origin);
                        break;
                    }

                    bool selfCheckPassed = true;
                    if (r.Kind == ResultKind.Success && kind == ChangeKind.TurnOn)
                    {
                        _selfCheckCommand = command;
                        _selfCheckLaunch = r;
                        selfCheckPassed = await SelfCheckPassesAsync(status);
                    }
                    ChangeResult? result = ResultFor(kind, command, r, status, selfCheckPassed, Context.Lists);
                    LastResult = result;
                    // R12: no bar, the status card explains the refusal; screen readers hear it with the reason.
                    announcement = result != null ? result.Text
                        : r.Kind == ResultKind.Refused ? Strings.ResultRefusedAnnouncement(Strings.Headline(status.State, status.Refusal), Strings.Detail(status)) : null;
                    SetStatus(status);
                    finished = new ChangeFinishedEventArgs(kind, r.Kind, result?.Text, origin, result?.Severity ?? NoticeSeverity.Success);
                    break;
                }
            }
            finally
            {
                ClearBusy();
            }
            ChangeFinished?.Invoke(this, finished);
            Form? announcer = ValidOwner(origin);
            if (announcer != null) Ui.Announce(announcer, announcement, Announcement.Result);
        }

        /// <summary>AccessDenied, FileLocked, Reverted and Unexpected open an error dialog (R14); the others give a result.</summary>
        private static bool IsErrorDialogKind(ResultKind kind) =>
            kind == ResultKind.AccessDenied || kind == ResultKind.FileLocked || kind == ResultKind.Reverted || kind == ResultKind.Unexpected;

        private void AfterSuccess(ChangeKind kind, ElevatedCommand command, Level? pendingBefore)
        {
            if (kind == ChangeKind.Undo)
            {
                // Undo also deletes HKCU\Software\QuietHosts: the closed notices go with it, in this session too.
                TrySettings(() => Context.Env.User.DeleteAll());
                UserSettingsDeleted = true;
                OffForTest = false;
                if (!Dismissals.IsEmpty)
                {
                    Dismissals = NoticeDismissals.Empty;
                    DismissalsChanged?.Invoke(this, EventArgs.Empty);
                }
                return;
            }
            UserSettingsDeleted = false;
            OffForTest = kind == ChangeKind.TurnOffForTest;
            // Each of these writes the block, so the level now lives there; a pending choice from "off" would otherwise come
            // back after Turn off. Turn off (also for a test) and a Repair that only cleans keep it.
            bool writesBlock = kind == ChangeKind.TurnOn || kind == ChangeKind.ChangeLevel || kind == ChangeKind.UpdateList ||
                kind == ChangeKind.RemoveOlderList || (kind == ChangeKind.Repair && command.Verb == Verb.On);
            if (writesBlock && pendingBefore.HasValue) TrySettings(() => Context.Env.User.SetPendingLevel(null));
        }

        /// <summary>
        /// The result bar for a finished change (spec 3.4), or null when there is none: an error dialog shows the result
        /// (AccessDenied, FileLocked, Reverted, Unexpected), or the change was refused and the status itself is Refused (the
        /// status card explains it; R12).
        /// </summary>
        /// <param name="after">The status read after the change.</param>
        /// <param name="selfCheckPassed">For a successful Turn on: whether Windows uses the new block yet.</param>
        internal static ChangeResult? ResultFor(ChangeKind kind, ElevatedCommand cmd, LaunchResult r, HostsStatus after, bool selfCheckPassed, BlockLists lists)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (after == null) throw new ArgumentNullException(nameof(after));
            switch (r.Kind)
            {
                case ResultKind.Success:
                    return SuccessResult(kind, cmd, selfCheckPassed, lists);
                case ResultKind.Declined:
                    return new ChangeResult(kind, r.Kind, Strings.ResultDeclined, NoticeSeverity.Info);
                case ResultKind.Busy:
                    return new ChangeResult(kind, r.Kind, Strings.ResultBusy, NoticeSeverity.Info);
                case ResultKind.Refused:
                    // The file can't be changed at all (the card says why), or the change would make it larger than 16 MB.
                    return after.State == ProtectionState.Refused ? null : new ChangeResult(kind, r.Kind, Strings.ResultRefusedChange, NoticeSeverity.Warning);
                default:
                    return null;
            }
        }

        private static ChangeResult SuccessResult(ChangeKind kind, ElevatedCommand cmd, bool selfCheckPassed, BlockLists lists)
        {
            const ResultKind ok = ResultKind.Success;
            switch (kind)
            {
                case ChangeKind.TurnOn:
                    return selfCheckPassed
                        ? new ChangeResult(kind, ok, Strings.ResultTurnOnOk, NoticeSeverity.Success)
                        : new ChangeResult(kind, ok, Strings.ResultSelfCheckFailed, NoticeSeverity.Warning, offersSelfCheckDetails: true);
                case ChangeKind.TurnOff:
                case ChangeKind.TurnOffForTest:
                    return new ChangeResult(kind, ok, Strings.ResultTurnOffOk, NoticeSeverity.Info);
                case ChangeKind.ChangeLevel:
                    return new ChangeResult(kind, ok, Strings.ResultLevelChanged(cmd.Level ?? Level.Standard), NoticeSeverity.Success);
                case ChangeKind.UpdateList:
                    return new ChangeResult(kind, ok, Strings.ResultListUpdated(lists?.Version), NoticeSeverity.Success);
                case ChangeKind.Allow:
                    return new ChangeResult(kind, ok, Strings.ResultAllowed(cmd.Hosts.Count), NoticeSeverity.Success);
                case ChangeKind.Block:
                    return new ChangeResult(kind, ok, Strings.ResultBlocked(cmd.Hosts.Count), NoticeSeverity.Success);
                case ChangeKind.Repair:
                    return new ChangeResult(kind, ok, Strings.ResultRepaired(cmd.Verb == Verb.On), NoticeSeverity.Success);
                case ChangeKind.RemoveOlderList:
                    return new ChangeResult(kind, ok, Strings.ResultOlderListRemoved, NoticeSeverity.Success);
                default:
                    return new ChangeResult(kind, ok, Strings.ResultUndoOk, NoticeSeverity.Info);
            }
        }

        /// <summary>The self-check after Turn on, on a worker thread, with two short retries for a slow DNS cache.</summary>
        private async Task<bool> SelfCheckPassesAsync(HostsStatus status)
        {
            string? host = status.FirstBlockedHost;
            if (status.State != ProtectionState.On || string.IsNullOrEmpty(host))
            {
                LastSelfCheck = new SelfCheckResult(false, status.State != ProtectionState.On ? Strings.DetailsSelfCheckNotOn : Strings.DetailsSelfCheckNoAddress);
                return false;
            }
            for (int attempt = 0; attempt < SelfCheckAttempts; attempt++)
            {
                if (attempt > 0) await Task.Delay(SelfCheckRetryDelay);
                SelfCheckResult check = await SelfCheck.CheckAsync(Context.Env, host!);
                LastSelfCheck = check;
                if (check.Passed) return true;
            }
            return false;
        }

        /// <summary>
        /// A stage of the running attempt. Nothing changes while the error dialog shows (no progress text), and the launcher
        /// reports nothing after its result, so a late stage can't replace a newer line. "Changing the hosts file…" and
        /// "taking longer" are announced once each; the wait for the Windows prompt isn't (Narrator reads the prompt).
        /// </summary>
        private void OnProgress(LaunchStage stage, Form origin)
        {
            if (!IsBusy || ProgressText == null) return;
            if (stage != LaunchStage.TakingLong)
            {
                string text = Strings.Progress(stage);
                if (string.Equals(text, ProgressText, StringComparison.Ordinal)) return;
                SetBusy(text);
                if (stage == LaunchStage.Changing)
                {
                    Form? window = ValidOwner(origin);
                    if (window != null) Ui.Announce(window, text, Announcement.Progress);
                }
                return;
            }
            if (TakingLong) return;
            // The run goes on (it is never stopped or abandoned): say why it may take so long.
            SetBusy(Strings.Progress(stage), takingLong: true);
            Form? announcer = ValidOwner(origin);
            if (announcer != null) Ui.Announce(announcer, Strings.ChangingSlow, Announcement.Progress);
        }

        private void SetBusy(string? progress, bool takingLong = false)
        {
            IsBusy = true;
            ProgressText = progress;
            TakingLong = takingLong;
            UpdateRunWatch();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearBusy()
        {
            IsBusy = false;
            ProgressText = null;
            TakingLong = false;
            UpdateRunWatch();
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Form? ValidOwner(Form? form)
        {
            if (form != null && !form.IsDisposed && form.Visible) return form;
            foreach (Form f in Application.OpenForms)
            {
                if (!f.IsDisposed && f.Visible) return f;
            }
            return null;
        }

        private static void TrySettings(Action action)
        {
            try { action(); }
            catch (Exception ex) when (IsSettingsError(ex)) { }
        }

        private static bool IsSettingsError(Exception ex) =>
            ex is UnauthorizedAccessException || ex is SecurityException || ex is System.IO.IOException || ex is ArgumentException;

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: show the progress row as if a change were running (and taking long).</summary>
        internal void PreviewBusy(string progress, bool takingLong = false) => SetBusy(progress, takingLong);

        /// <summary>For tests: the change has ended (as after <see cref="PreviewBusy"/>).</summary>
        internal void PreviewClearBusy() => ClearBusy();

        /// <summary>For tests: look for the end of another QuietHosts run this often instead of every 2 s.</summary>
        internal void PreviewRunWatchInterval(int milliseconds)
        {
            if (_runWatch != null) _runWatch.Interval = milliseconds;
        }

        /// <summary>For tests: the window is looking for the end of another QuietHosts run.</summary>
        internal bool IsWatchingOtherRun => _runWatch != null && _runWatch.Enabled;

        /// <summary>For tests: how often (ms) the window looks for the end of another QuietHosts run (2 s, K10); 0 without a watch (a preview).</summary>
        internal int RunWatchInterval => _runWatch?.Interval ?? 0;

        /// <summary>For --render-ui and tests: show a result as if a change had finished.</summary>
        internal void PreviewResult(string line, Form? origin, NoticeSeverity severity = NoticeSeverity.Success) =>
            PreviewResult(new ChangeResult(ChangeKind.ChangeLevel, ResultKind.Success, line, severity), origin);

        /// <summary>For --render-ui and tests: show <paramref name="result"/> as if its change had finished.</summary>
        internal void PreviewResult(ChangeResult result, Form? origin)
        {
            LastResult = result ?? throw new ArgumentNullException(nameof(result));
            StatusChanged?.Invoke(this, EventArgs.Empty);
            ChangeFinished?.Invoke(this, new ChangeFinishedEventArgs(result.Kind, result.Result, result.Text, origin, result.Severity));
        }

        /// <summary>For --render-ui and tests: a Turn on whose self-check failed (R2).</summary>
        internal void PreviewSelfCheckFailed(SelfCheckResult check, Form? origin)
        {
            _selfCheckCommand = ElevatedCommand.On(Level.Standard);
            _selfCheckLaunch = new LaunchResult(ResultKind.Success, 0, null, new string('a', 64), new string('b', 64));
            LastSelfCheck = check;
            PreviewResult(new ChangeResult(ChangeKind.TurnOn, ResultKind.Success, Strings.ResultSelfCheckFailed, NoticeSeverity.Warning, offersSelfCheckDetails: true), origin);
        }

        /// <summary>For --render-ui and tests: protection was turned off in this session to test a site (S2).</summary>
        internal void PreviewOffForTest()
        {
            OffForTest = Status.State == ProtectionState.Off;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
#endif
    }
}
