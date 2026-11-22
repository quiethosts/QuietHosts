using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// "Fix a site or app that isn't working" (spec 5): the steps to try as cards side by side (Restart your browser; Try
    /// Standard while on Strict; Test with protection off, or the card that fits the state), the report line, a labelled
    /// search box with the "Show only addresses you allowed" filter, the list of addresses (status pills, the known-effects
    /// glyph) with its details line, the result of a change, and a footer with the count or what a click would do, Allow,
    /// Block again and Close. At most 100 addresses per change. Modal, resizable and maximizable; the client size is
    /// remembered in epx. On a screen too short for four list rows the body scrolls above the footer (D18).
    /// </summary>
    internal sealed class FixDialog : ThemedWindow
    {
        /// <summary>The Status column's index (the old "Known effects" column is gone: its text is the details line).</summary>
        public const int StatusColumn = 4;
        private const int SearchDelayMs = 200;
        /// <summary>The narrowest room (epx) in which the footer's text stays beside its buttons.</summary>
        private const int MinFooterTextWidth = 200;
        private const int MaxRememberedWidth = 4000, MaxRememberedHeight = 3000;

        /// <summary>The last step card's kinds, from the state (5.3).</summary>
        private enum LastStep
        {
            /// <summary>On: Test with protection off, "Turn off for now".</summary>
            Test,
            /// <summary>Can't read or Refused: nothing can change from here; no button.</summary>
            CantChange,
            /// <summary>The older block-ads.ps1 list without QuietHosts' block: "Turn on" replaces it.</summary>
            OlderList,
            /// <summary>Off after "Turn off for now": "Turn on again" (accent).</summary>
            OffAfterTest,
            /// <summary>Removed: "Turn on again".</summary>
            Removed,
            /// <summary>Off (any other): "Turn on protection".</summary>
            Off,
        }

        private readonly AppController _c;
        private readonly UiContext _ctx;
        private readonly ScrollHost _host;
        private readonly FixBody _body;
        private readonly CommandBar _footer;

        private readonly ThemedLabel _intro;
        private readonly StepCard _step1, _step2, _last;
        private readonly StepRow _steps;
        private readonly ThemedButton _switchToStandard, _lastButton;
        private readonly TextFlow _reportLine;
        private readonly ThemedLabel _reportPrompt, _copyFeedback;
        private readonly ThemedLink _reportSite, _copyDetails;
        private readonly ThemedLabel _searchLabel;
        private readonly SearchBox _search;
        private readonly ThemedCheckBox _onlyAllowed;
        private readonly CardPanel _listCard;
        private readonly ThemedListView _list;
        private readonly EmptyStatePanel _empty;
        private readonly ThemedLabel _details;
        private readonly NoticeBar _resultBar;

        private readonly ProgressRing _ring;
        private readonly ThemedLabel _activity, _footerLine, _countLabel, _suffixLabel;
        private readonly LabelPair _countLine;
        private readonly ThemedButton _allow, _blockAgain, _close;
        private readonly Timer _searchTimer;
        private readonly Dictionary<string, ListViewItem> _items = new Dictionary<string, ListViewItem>(StringComparer.Ordinal);

        private List<FixRow> _rows = new List<FixRow>();
        private HashSet<string> _blocked = new HashSet<string>(StringComparer.Ordinal);
        private HostsStatus? _shownStatus;
        private LastStep _lastStep;
        private int _shown;
        private string _appliedQuery = string.Empty;
        private bool _selectionUpdatePending, _fitting, _resultShown;
        private ChangeResult? _shownResult;
        private Control? _focusBeforeBusy;
        private Control[]? _footerLeft;

        public FixDialog(AppController controller)
        {
            _c = controller ?? throw new ArgumentNullException(nameof(controller));
            _ctx = controller.Context;
            UiScale s = Scale;

            Text = Strings.FixTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            // Maximizing (also Alt+Space, X) widens the list, for keyboard users too.
            MaximizeBox = true;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AccessibleRole = AccessibleRole.Dialog;

            // ---- 1 intro and 2 the step cards
            _intro = new ThemedLabel(Ramp, Strings.FixIntro, TextStyle.BodyStrong);
            // Enter never switches protection or the level (K2): Space, the access key and a click do.
            _switchToStandard = new ThemedButton(Ramp, Strings.FixSwitchToStandardButton) { Shield = true, SwallowsEnter = true };
            _lastButton = new ThemedButton(Ramp, Strings.FixTurnOffButton) { Shield = true, SwallowsEnter = true };
            _step1 = new StepCard(Ramp, 1, Strings.FixStep1Title, Strings.FixStep1Body);
            _step2 = new StepCard(Ramp, 2, Strings.FixStep2Title, Strings.FixStep2Body) { Button = _switchToStandard };
            _last = new StepCard(Ramp, 2, Strings.FixStepTestTitle, Strings.FixStepTestBody) { Button = _lastButton };
            _steps = new StepRow(Ramp, _step1, _step2, _last);

            // ---- 3 the report line: the prompt, 8 epx, the report link, 16 epx, Copy details, and after a copy 16 epx and its feedback
            _reportPrompt = new ThemedLabel(Ramp, Strings.FixReportPrompt, TextStyle.Body, TextRole.Secondary);
            _reportSite = new ThemedLink(Ramp, Strings.FixReportSiteLink) { External = true };
            _copyDetails = new ThemedLink(Ramp, Strings.FixCopyDetailsLink);
            _copyFeedback = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary) { Visible = false };
            // A line the row wraps onto starts with its letters on the prompt's (a link goes its padding further left).
            _reportLine = new TextFlow(_reportPrompt, _reportSite, _copyDetails, _copyFeedback);
            // Each gap is the right margin of the part before it, so a line the row wraps onto starts at the column's edge.
            _reportPrompt.Margin = new Padding(0, 0, DialogParts.InkGap(Ramp, _reportPrompt, _reportSite, 8), 0);
            _reportSite.Margin = new Padding(0, 0, DialogParts.InkGap(Ramp, _reportSite, _copyDetails, 16), 0);
            _copyDetails.Margin = new Padding(0, 0, DialogParts.InkGap(Ramp, _copyDetails, _copyFeedback, 16), 0);
            _copyFeedback.Margin = Padding.Empty;
            DialogParts.AlignWithLinks(_reportPrompt, _reportSite);
            DialogParts.AlignWithLinks(_copyFeedback, _reportSite);

            // ---- 4 the field label and 5 the search row
            _searchLabel = new ThemedLabel(Ramp, Strings.FixSearchLabel, TextStyle.BodyStrong) { UseMnemonic = true };
            _search = new SearchBox(Ramp) { Hint = Strings.FixSearchHint };
            _search.TextBox.AccessibleName = Strings.FixSearchAccessibleName;
            _onlyAllowed = new ThemedCheckBox(Ramp, Strings.FixShowOnlyAllowed(0));

            // ---- 6 the list in a card (padding 4), with the no-match state over its rows
            _list = new ThemedListView(Ramp) { Dock = DockStyle.Fill, AccessibleName = Strings.FixListAccessibleName, CellPainter = PaintCell };
            _list.Columns.Add(Strings.ColumnAddress);
            _list.Columns.Add(Strings.ColumnCompany);
            _list.Columns.Add(Strings.ColumnWhatItIs);
            _list.Columns.Add(Strings.ColumnLevel);
            _list.Columns.Add(Strings.ColumnStatus);
            _empty = new EmptyStatePanel(Ramp) { Visible = false };
            _listCard = new CardPanel(4, withContent: false);
            _listCard.Controls.Add(_list);
            _listCard.Controls.Add(_empty);
            _empty.BringToFront();

            // ---- 7 the details line: the caution glyph's room always stays, so the text never jumps
            _details = new ThemedLabel(Ramp, string.Empty)
            {
                LeadingGlyph = Glyph.Warning,
                LeadingGlyphColor = ThemeToken.CautionGlyph,
                LeadingGlyphHidden = true,
                LiveSetting = AutomationLiveSetting.Polite,
            };
            // At least two Body lines tall (after LeadingGlyph, which sets the one-line minimum).
            _details.MinimumSize = new Size(0, 2 * Ramp.LineHeight(TextStyle.Body));

            // ---- 8 the result of a change (closable, no action)
            _resultBar = new NoticeBar(Ramp, NoticeSeverity.Success, string.Empty, closable: true) { Visible = false };
            _resultBar.CloseRequested += OnResultClosed;

            _body = new FixBody(this);
            // In tab order: the step buttons, the report links, the search box, the filter, the list (and the no-match link),
            // the result bar's close button.
            foreach (Control c in new Control[] { _intro, _steps, _reportLine, _searchLabel, _search, _onlyAllowed, _listCard, _details, _resultBar })
                _body.Controls.Add(c);

            // ---- F the footer: what a click would do (or the count), then Allow, Block again and Close
            _ring = new ProgressRing { Visible = false };
            _activity = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary);
            _footerLine = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary);
            _countLabel = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary) { LiveSetting = AutomationLiveSetting.Polite };
            _suffixLabel = new ThemedLabel(Ramp, string.Empty, TextStyle.Body, TextRole.Secondary) { Visible = false };
            // " · 2 selected" right after the count's last letter, in a label of its own that isn't a live region.
            _countLine = new LabelPair(_countLabel, _suffixLabel);
            _allow = new ThemedButton(Ramp, Strings.AllowButton, ButtonStyle.Accent) { Shield = true, ContextDescription = Strings.FixConsequence };
            _blockAgain = new ThemedButton(Ramp, Strings.BlockAgainButton) { Shield = true };
            _close = new ThemedButton(Ramp, Strings.CloseButton) { DialogResult = DialogResult.Cancel };
            CancelButton = _close;
            _footer = new CommandBar(Ramp)
            {
                // One footer height for the count and what a click would do (5.9): the text wraps beside the buttons in room
                // for at least two Body lines and for the longest of those texts, the consequence, so selecting a row never
                // moves the list; it goes above the buttons only where the room beside them is narrower than 200 epx or 12
                // times the Body size (large text).
                LeftWrapMinWidth = Math.Max(s.Px(MinFooterTextWidth), (int)Math.Round(12 * Ramp.SizePx(TextStyle.Body))),
                LeftReserve = FooterReserve,
            };
            _footer.SetRight(_allow, _blockAgain, _close);

            _host = AddFillingContent(_footer);
            _host.Controls.Add(_body);
            _host.Resize += (o, e) => FitBody();

            _searchTimer = new Timer { Interval = SearchDelayMs };
            _searchTimer.Tick += (o, e) => ApplyFilter();
            _search.TextBox.TextChanged += (o, e) => { _searchTimer.Stop(); _searchTimer.Start(); };
            _search.TextBox.KeyDown += OnSearchKeyDown;
            _search.TextBox.KeyPress += (o, e) => { if (e.KeyChar == '\r') e.Handled = true; };
            _search.FilterRequested += (o, e) => ApplyFilter();
            _onlyAllowed.CheckedChanged += (o, e) => ApplyFilter();
            _list.SelectedIndexChanged += OnSelectionChanged;
            _list.KeyDown += OnListKeyDown;
            _list.ClientSizeChanged += (o, e) => PostFitColumns();
            _allow.Click += OnAllow;
            _blockAgain.Click += OnBlockAgain;
            _switchToStandard.Click += OnSwitchToStandard;
            _lastButton.Click += OnLastStep;
            _reportSite.Invoked += (o, e) => OpenUrl(Strings.ReportSiteUrl);
            _copyDetails.Invoked += (o, e) => CopySiteReport();
            _empty.GoToStep.Invoked += (o, e) => { if (_lastButton.CanFocus) _lastButton.Focus(); };

            _c.StatusChanged += OnStatusChanged;
            _c.BusyChanged += OnBusyChanged;
            _c.ChangeFinished += OnChangeFinished;

            // The remembered client size in epx (D11), clamped to the smallest size and to 4000 x 3000; OnLoad raises the
            // smallest size to what four list rows need and fits the owner's screen.
            MinimumSize = SizeFromClientSize(new Size(s.Px(Metrics.FixMinClientWidth), s.Px(Metrics.FixMinClientHeight)));
            ClientSize = RememberedClientSize();

            ActiveControl = _search.TextBox;
            ShowStatus();
            EndLayout();
            FitBody();
        }

        private Size RememberedClientSize()
        {
            UserSettings u = _c.ReadUserSettings();
            Size epx = RememberedSizeEpx(u.FixW, u.FixH);
            return new Size(Scale.Px(epx.Width), Scale.Px(epx.Height));
        }

        /// <summary>
        /// The client size to open at, in epx: the remembered one (760 × 680 when there is none), at least 640 × 480 and at most
        /// 4000 × 3000. Opening then raises it to the smallest size four list rows need.
        /// </summary>
        internal static Size RememberedSizeEpx(int? width, int? height)
        {
            bool remembered = width.HasValue && height.HasValue;
            int w = remembered ? width!.Value : Metrics.FixDefaultClientWidth;
            int h = remembered ? height!.Value : Metrics.FixDefaultClientHeight;
            w = Math.Max(Metrics.FixMinClientWidth, Math.Min(w, MaxRememberedWidth));
            h = Math.Max(Metrics.FixMinClientHeight, Math.Min(h, MaxRememberedHeight));
            return new Size(w, h);
        }

        // ---------------------------------------------------------------- state

        private void OnStatusChanged(object? sender, EventArgs e)
        {
            // A new read of the file: the rows and the cards follow it. A result closed in either window, or the session's
            // "off after a test", changes only the result bar and the cards.
            if (!ReferenceEquals(_shownStatus, _c.Status)) ShowStatus();
            else
            {
                ShowSteps();
                UpdateButtons();
                UpdateFooter();
                UpdateResultBar();
            }
        }

        private void OnBusyChanged(object? sender, EventArgs e)
        {
            // Before the buttons are disabled: disabling the focused one moves the focus on.
            if (_c.IsBusy && _focusBeforeBusy == null) _focusBeforeBusy = ActiveControl;
            // The result bar goes when the next change starts (5.10).
            if (_c.IsBusy) _shownResult = null;
            UpdateButtons();
            UpdateFooter();
            UpdateResultBar();
            if (_c.IsBusy) return;
            Control? restore = _focusBeforeBusy;
            _focusBeforeBusy = null;
            // Nothing captured: the dialog opened during a change; leave the focus where the user put it.
            if (restore == null || !IsForeground) return;
            if (!restore.IsDisposed && restore.CanFocus && restore.FindForm() == this) restore.Focus();
            else if (_list.CanFocus) _list.Focus();
        }

        private void OnChangeFinished(object? sender, ChangeFinishedEventArgs e)
        {
            // A result that arrives while the dialog is open shows here too (the announcement comes from AppController).
            _shownResult = _c.LastResult;
            UpdateResultBar();
        }

        /// <summary>This dialog is the active window (a test's never is).</summary>
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

        private void ShowStatus()
        {
            HostsStatus s = _c.Status;
            _shownStatus = s;
            SuspendLayout();
            try
            {
                ShowSteps();
                _onlyAllowed.Text = Strings.FixShowOnlyAllowed(s.AllowedCount);
                if (s.AllowedCount == 0) _onlyAllowed.Checked = false;
                _onlyAllowed.Enabled = s.AllowedCount > 0;

                _rows = FixList.Build(_ctx.Lists, s.SelectedLevel, s.Allowlist);
                _blocked = new HashSet<string>(s.BlockedHosts, StringComparer.Ordinal);
                _items.Clear();
                ApplyFilter(announce: false);
                UpdateResultBar();
            }
            finally
            {
                ResumeLayout(true);
            }
            FitBody();
        }

        private LastStep LastStepFor(HostsStatus s)
        {
            switch (s.State)
            {
                case ProtectionState.On: return LastStep.Test;
                case ProtectionState.CantRead:
                case ProtectionState.Refused: return LastStep.CantChange;
            }
            if (s.State == ProtectionState.OlderList || ((s.State == ProtectionState.Off || s.State == ProtectionState.Removed) && s.HasLegacyBlock))
                return LastStep.OlderList;
            if (s.State == ProtectionState.Off && _c.OffForTest) return LastStep.OffAfterTest;
            if (s.State == ProtectionState.Removed) return LastStep.Removed;
            return LastStep.Off;
        }

        /// <summary>Protection on at Strict: the second card (Try Standard) shows.</summary>
        private bool StrictStepShown
        {
            get
            {
                HostsStatus s = _c.Status;
                return s.State == ProtectionState.On && (s.BlockLevel ?? s.SelectedLevel) == Level.Strict;
            }
        }

        /// <summary>
        /// The cards by state (5.3). The last card keeps its button object whatever it says, so the focus stays on it after
        /// "Turn off for now" becomes "Turn on again".
        /// </summary>
        private void ShowSteps()
        {
            _lastStep = LastStepFor(_c.Status);
            bool strict = StrictStepShown;
            _step2.Visible = strict;
            _last.Number = strict ? 3 : 2;
            string title, body, button = Strings.FixTurnOffButton;
            ButtonStyle style = ButtonStyle.Standard;
            switch (_lastStep)
            {
                case LastStep.Test:
                    title = Strings.FixStepTestTitle;
                    body = Strings.FixStepTestBody;
                    break;
                case LastStep.CantChange:
                    title = Strings.FixStepCantChangeTitle;
                    body = Strings.FixStepCantChangeBody;
                    break;
                case LastStep.OlderList:
                    title = Strings.FixStepOlderListTitle;
                    body = Strings.FixStepOlderListBody;
                    button = Strings.FixReplaceOlderListButton;
                    style = ButtonStyle.Accent;
                    break;
                case LastStep.OffAfterTest:
                    title = Strings.FixStepOffTitle;
                    body = Strings.FixStepOffAfterTestBody;
                    button = Strings.FixTurnOnButton;
                    style = ButtonStyle.Accent;
                    break;
                case LastStep.Removed:
                    title = Strings.FixStepOffTitle;
                    body = Strings.FixStepOffBody;
                    button = Strings.FixTurnOnButton;
                    break;
                default:
                    title = Strings.FixStepOffTitle;
                    body = Strings.FixStepOffBody;
                    button = Strings.FixTurnOnProtectionButton;
                    break;
            }
            _last.Title = title;
            _last.BodyText = body;
            _lastButton.Text = button;
            _lastButton.Style = style;
            _lastButton.Visible = _lastStep != LastStep.CantChange;
            _empty.GoToStep.Text = Strings.FixGoToStep(_last.Number);

            // The search box's description names the steps shown, then the hint (5.11).
            var steps = new List<string> { Strings.FixStepShortRestart };
            if (strict) steps.Add(Strings.FixStepShortStandard);
            switch (_lastStep)
            {
                case LastStep.Test: steps.Add(Strings.FixStepShortTest); break;
                case LastStep.OlderList: steps.Add(Strings.FixStepShortOlderList); break;
                case LastStep.CantChange: break;
                default: steps.Add(Strings.FixStepShortTurnOn); break;
            }
            _search.TextBox.AccessibleDescription = Strings.FixDescription(steps, _lastStep != LastStep.CantChange) + " " + Strings.FixSearchHint;
            _steps.PerformLayout();
        }

        /// <summary>
        /// The Status column, from the hosts file: "Blocked" only if the address is in QuietHosts' block there. The rows come
        /// from this exe's lists, so with protection off, or a block from another list, some aren't blocked.
        /// </summary>
        private string StatusText(FixRow r)
        {
            if (r.Allowed) return Strings.StatusAllowed;
            if (_lastStep == LastStep.CantChange) return Strings.StatusUnknown;
            if (_blocked.Contains(r.Host)) return Strings.StatusBlocked;
            // Not in QuietHosts' block, but the older block-ads.ps1 list may still have it.
            return _c.Status.HasLegacyBlock ? Strings.StatusUnknown : Strings.StatusNotBlocked;
        }

        private static bool CanTurnOn(ProtectionState state) =>
            state == ProtectionState.Off || state == ProtectionState.OlderList || state == ProtectionState.Removed;

        private void ApplyFilter() => ApplyFilter(announce: true);

        /// <summary>Shows the rows matching the search and the filter, from the top; the count changes (and is heard).</summary>
        private void ApplyFilter(bool announce)
        {
            _searchTimer.Stop();
            string query = _search.Text.Trim();
            IEnumerable<FixRow> shown = FixList.Search(_rows, query);
            if (_onlyAllowed.Checked) shown = shown.Where(r => r.Allowed);
            ListViewItem[] items = shown.Select(ItemFor).ToArray();
            _list.BeginUpdate();
            try
            {
                _list.SelectedIndices.Clear();
                _list.Items.Clear();
                _list.Items.AddRange(items);
            }
            finally
            {
                _list.EndUpdate();
            }
            if (items.Length > 0 && _list.IsHandleCreated) _list.EnsureVisible(0);
            _shown = items.Length;
            _appliedQuery = query;
            bool countShownBefore = CountShown;
            UpdateEmptyState();
            UpdateButtons();
            UpdateFooter();
            UpdateRowDetails();
            // The vertical scroll bar may have come or gone with the rows.
            FitColumns();
            // The count label is a live region, but it speaks only when its text changes while it shows. Otherwise (another
            // text holds the footer, or the count comes back in place of one: the filter cleared a selection) the dialog says
            // the count itself, so every search is heard once (5.9, C16).
            if (announce && !(countShownBefore && CountShown)) Ui.Announce(this, CountText, Announcement.Progress);
        }

        private ListViewItem ItemFor(FixRow r)
        {
            if (_items.TryGetValue(r.Host, out ListViewItem? item)) return item;
            item = new ListViewItem(new[]
            {
                r.Host,
                r.Company,
                Strings.CategoryWords(r.Entry.Category),
                Strings.TierName(r.Tier),
                StatusText(r),
            })
            {
                Tag = r,
                // The list shows no notes; hovering shows the whole one.
                ToolTipText = Strings.FixRowToolTip(r.Host, r.Note),
            };
            _items[r.Host] = item;
            return item;
        }

        /// <summary>No row matches a search: the no-match state over the list, with "Go to step N" while that step's button works.</summary>
        private void UpdateEmptyState()
        {
            bool empty = _shown == 0 && _appliedQuery.Length > 0;
            _empty.Message.Text = empty ? Strings.FixNoMatch(_appliedQuery) : string.Empty;
            _empty.GoToStep.Visible = LayoutUtil.IsShown(_lastButton) && _lastButton.Enabled;
            _empty.Visible = empty;
            // The empty list under the no-match state would take Tab with no visible focus: it leaves the tab order meanwhile
            // (it stays in the accessibility tree, 5.8); Tab goes on to "Go to step N".
            _list.TabStop = !empty;
            _empty.PerformLayout();
        }

        private int SelectedCount => _list.SelectedIndices.Count;

        /// <summary>Allow, Block again and the step buttons wait while a change runs, here or in another QuietHosts run.</summary>
        private void UpdateButtons()
        {
            bool canChange = _c.CanStartChange;
            HostsStatus s = _c.Status;
            bool on = s.State == ProtectionState.On;
            (int blocked, int allowed) = SelectedKinds();
            bool withinLimit = SelectedCount <= Limits.MaxHostsPerRun;
            _allow.Enabled = canChange && on && withinLimit && SelectedCount >= 1 && blocked > 0;
            _blockAgain.Enabled = canChange && on && withinLimit && allowed > 0;
            _switchToStandard.Enabled = canChange;
            _lastButton.Enabled = canChange && (_lastStep == LastStep.Test ? on : CanTurnOn(s.State));
            bool link = LayoutUtil.IsShown(_lastButton) && _lastButton.Enabled;
            if (LayoutUtil.IsShown(_empty.GoToStep) != link)
            {
                _empty.GoToStep.Visible = link;
                _empty.PerformLayout();
            }
        }

        /// <summary>How many selected rows are blocked and how many allowed (counted only within the 100-address limit).</summary>
        private (int Blocked, int Allowed) SelectedKinds()
        {
            int blocked = 0, allowed = 0;
            if (SelectedCount > Limits.MaxHostsPerRun) return (0, 0);
            foreach (FixRow r in SelectedRows())
            {
                if (r.Allowed) allowed++;
                else blocked++;
            }
            return (blocked, allowed);
        }

        private IEnumerable<FixRow> SelectedRows()
        {
            foreach (ListViewItem item in _list.SelectedItems)
            {
                if (item.Tag is FixRow r) yield return r;
            }
        }

        /// <summary>The count of shown rows, or that a search matched nothing.</summary>
        private string CountText => _shown == 0 && _appliedQuery.Length > 0 ? Strings.FixNoMatch(_appliedQuery) : Strings.FixShowing(_shown, _rows.Count);

        private bool CountShown => _footerLeft != null && _footerLeft.Length == 1 && ReferenceEquals(_footerLeft[0], _countLine);

        /// <summary>The footer's left side (5.9): the first of progress, can't change, not on, too many, the consequence, the count.</summary>
        private void UpdateFooter()
        {
            HostsStatus s = _c.Status;
            int selected = SelectedCount;
            Control[] left;
            if (_c.ShowsActivity)
            {
                // A change in progress, here or in another QuietHosts run, in one line.
                string activity = _c.ActivityText(" ");
                _activity.Text = activity;
                _ring.AccessibleName = activity;
                left = new Control[] { _ring, _activity };
            }
            else
            {
                string? text = null;
                if (_lastStep == LastStep.CantChange) text = Strings.FixCantChange;
                else if (s.State != ProtectionState.On) text = Strings.FixTurnOnToChange;
                else if (selected > Limits.MaxHostsPerRun) text = Strings.FixTooMany(Limits.MaxHostsPerRun);
                else if (selected >= 1 && SelectedKinds().Blocked > 0) text = Strings.FixConsequence;
                if (text != null)
                {
                    _footerLine.Text = text;
                    left = new Control[] { _footerLine };
                }
                else
                {
                    SetCountText(CountText);
                    _suffixLabel.Text = selected > 0 ? Strings.FixSelectedSuffix(selected) : string.Empty;
                    _suffixLabel.Visible = selected > 0;
                    left = new Control[] { _countLine };
                }
            }
            bool ring = left.Length == 2;
            _ring.Visible = ring;
            _ring.Active = ring;
            if (_footerLeft == null || !_footerLeft.SequenceEqual(left))
            {
                // Parts that were out of the footer missed any theme change meanwhile.
                foreach (Control c in left) ApplyCurrentTheme(c);
                _footerLeft = left;
                _footer.SetLeft(left);
            }
            else
            {
                // Only a text changed: the row lays out again, and asks the window for another height if the text now
                // takes more or fewer lines, so Allow, Block again and Close always show whole (5.9).
                _countLine.PerformLayout();
                _footer.RefreshLayout();
            }
        }

        private int _reserveRoom = -1, _reserveHeight;

        /// <summary>
        /// The footer's left height for <paramref name="room"/> pixels: two Body lines, or the consequence's lines if more (the
        /// longest text a selection brings; it is measured as its label measures it). The progress texts may still need more.
        /// </summary>
        private int FooterReserve(int room)
        {
            if (room != _reserveRoom)
            {
                Size consequence = TextRenderer.MeasureText(Strings.FixConsequence, Ramp.Body, new Size(room, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
                _reserveHeight = Math.Max(2 * Ramp.LineHeight(TextStyle.Body), consequence.Height);
                _reserveRoom = room;
            }
            return _reserveHeight;
        }

        /// <summary>
        /// The count label's text. While the label is out of the footer its live region stays quiet: a change there would
        /// come from a label nobody sees, and the dialog says the count itself when it comes back (<see cref="ApplyFilter(bool)"/>).
        /// </summary>
        private void SetCountText(string text)
        {
            if (string.Equals(_countLabel.Text, text, StringComparison.Ordinal)) return;
            if (CountShown)
            {
                _countLabel.Text = text;
                return;
            }
            AutomationLiveSetting live = _countLabel.LiveSetting;
            _countLabel.LiveSetting = AutomationLiveSetting.Off;
            try
            {
                _countLabel.Text = text;
            }
            finally
            {
                _countLabel.LiveSetting = live;
            }
        }

        private static void ApplyCurrentTheme(Control c)
        {
            if (c is IThemed themed) themed.ApplyTheme(ThemeManager.Current);
            foreach (Control child in c.Controls) ApplyCurrentTheme(child);
        }

        /// <summary>The focused row if it is selected, else the first selected row: its known effects, or the hint.</summary>
        private void UpdateRowDetails()
        {
            ListViewItem? item = _list.FocusedItem;
            if (item == null || !item.Selected) item = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0] : null;
            string text;
            if (item?.Tag is FixRow r)
            {
                text = Strings.FixRowDetails(r.Host, r.Company, Strings.TierName(r.Tier), r.Note);
                _details.LeadingGlyphHidden = r.Note.Length == 0;
                _details.Role = TextRole.Primary;
            }
            else
            {
                text = _shown > 0 ? Strings.FixNoSelection : string.Empty;
                _details.LeadingGlyphHidden = true;
                _details.Role = TextRole.Secondary;
            }
            if (!string.Equals(_details.Text, text, StringComparison.Ordinal))
            {
                _details.Text = text;
                FitBody();
            }
        }

        /// <summary>The result of a change that finished while the dialog is open (5.10), until the next change or its close.</summary>
        private void UpdateResultBar()
        {
            ChangeResult? r = _c.IsBusy ? null : _shownResult;
            // Closed in either window (or cleared by a new read of the file): it goes here too.
            if (r != null && !ReferenceEquals(r, _c.LastResult)) r = null;
            bool show = r != null;
            if (show)
            {
                _resultBar.Severity = r!.Severity;
                _resultBar.Message = r.Text;
            }
            if (show == _resultShown && LayoutUtil.IsShown(_resultBar) == show) return;
            _resultShown = show;
            _resultBar.Visible = show;
            if (show) GrowForResult();
            else ShrinkAfterResult();
            FitBody();
        }

        // The height the dialog grew by for the result bar, and its height then (a later resize by the user keeps theirs).
        private int _grownForResult, _heightWhenGrown;

        /// <summary>
        /// A result bar in a dialog whose body fitted: the dialog grows by what the bar needs, as far as its screen allows,
        /// so the body doesn't start to scroll (and every column to narrow) for a few pixels; the list keeps its rows (D18).
        /// It gives that height back when the bar goes (<see cref="ShrinkAfterResult"/>).
        /// </summary>
        private void GrowForResult()
        {
            if (!IsHandleCreated || WindowState != FormWindowState.Normal || _grownForResult > 0 || _host.Width <= 0 || _host.Height <= 0) return;
            int width = _host.Width;
            // Only a body that fits without the bar: one that scrolls anyway (a short screen) keeps scrolling.
            if (ArrangeBody(width, 0, apply: false, withResult: false) > _host.Height) return;
            int missing = ArrangeBody(width, 0, apply: false) - _host.Height;
            if (missing <= 0) return;
            Rectangle area = OwnerWorkingArea();
            int grow = area.Height > 0 ? Math.Min(missing, Math.Max(0, area.Height - Height)) : missing;
            if (grow <= 0) return;
            Height += grow;
            // Still on its screen: a dialog near the bottom moves up.
            if (area.Height > 0 && Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
            _grownForResult = grow;
            _heightWhenGrown = Height;
        }

        /// <summary>The result bar went: the height it added goes too, unless the user resized the dialog meanwhile.</summary>
        private void ShrinkAfterResult()
        {
            int grow = _grownForResult;
            _grownForResult = 0;
            if (grow <= 0 || WindowState != FormWindowState.Normal || Height != _heightWhenGrown) return;
            Height -= grow;
        }

        private void OnResultClosed(object? sender, EventArgs e)
        {
            bool hadFocus = _resultBar.ContainsFocus;
            _shownResult = null;
            // The bar goes in both windows.
            _c.CloseResult();
            UpdateResultBar();
            if (!hadFocus && ActiveControl != null && ActiveControl.CanFocus) return;
            if (_allow.CanFocus) _allow.Focus();
            else if (_list.CanFocus) _list.Focus();
        }

        // ---------------------------------------------------------------- columns and cells

        /// <summary>Base and minimum widths of the five columns, in pixels; Level and Status fit their pills.</summary>
        private (int[] Base, int[] Min) ColumnWidths()
        {
            UiScale s = Scale;
            if (_pillColumns == null)
            {
                // Level: max(64, the Strict pill + 24); Status: max(112, the widest pill or plain status + 24), measured on a
                // device context, so without GDI's blank margins (the measure without one keeps them, 2.8.8).
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    int Text(string text, Font font) => TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    int PillWidth(string text) => Math.Max(Pill.Height(Ramp), Text(text, Ramp.Caption) + 2 * s.Px(Metrics.PillPadX));
                    int level = Math.Max(s.Px(64), PillWidth(Strings.TierName(Tier.Strict)) + s.Px(24));
                    int widest = Math.Max(PillWidth(Strings.StatusBlocked), PillWidth(Strings.StatusAllowed));
                    foreach (string plain in new[] { Strings.StatusNotBlocked, Strings.StatusUnknown }) widest = Math.Max(widest, Text(plain, Ramp.Body));
                    _pillColumns = (level, Math.Max(s.Px(112), widest + s.Px(24)));
                    // No column is narrower than its header's text (it starts 16 epx in on the first column, 12 on the others,
                    // and keeps 8 epx on the right): at large text the epx minimums would cut "Company" to "Compa…".
                    string[] headers = { Strings.ColumnAddress, Strings.ColumnCompany, Strings.ColumnWhatItIs, Strings.ColumnLevel, Strings.ColumnStatus };
                    _headerFloors = headers.Select((h, i) => Text(h, Ramp.Caption) + 1 + s.Px(i == 0 ? 16 : 12) + s.Px(8)).ToArray();
                }
            }
            (int levelWidth, int statusWidth) = _pillColumns.Value;
            int[] widths = { s.Px(232), s.Px(128), s.Px(152), levelWidth, statusWidth };
            int[] min = { s.Px(160), s.Px(96), s.Px(120), levelWidth, statusWidth };
            for (int i = 0; i < widths.Length; i++)
            {
                min[i] = Math.Max(min[i], _headerFloors![i]);
                widths[i] = Math.Max(widths[i], min[i]);
            }
            return (widths, min);
        }

        private (int Level, int Status)? _pillColumns;
        private int[]? _headerFloors;

        /// <summary>
        /// The columns fill the list (5.6): the base widths with the rest to Address; when there isn't room, Company shrinks to
        /// its minimum, then What it is, then Address. Below the minimums a horizontal scroll bar appears.
        /// </summary>
        private void FitColumns()
        {
            if (_list.IsDisposed || _list.Columns.Count < 5) return;
            (int[] widths, int[] min) = ColumnWidths();
            int[] fitted = FitWidths(_list.ClientSize.Width, widths, min);
            for (int i = 0; i < fitted.Length; i++)
            {
                if (_list.Columns[i].Width != fitted[i]) _list.Columns[i].Width = fitted[i];
            }
        }

        private bool _fitPosted;

        /// <summary>
        /// The list raises ClientSizeChanged from inside its own size message, while it adds or removes a scroll bar. Widths
        /// set there are kept, but the list then still shows the horizontal bar it was adding although the columns now fit
        /// (fix-min-size showed one), so that fit waits until the message is done. Where the dialog itself sizes the list or
        /// changes its rows, it fits the columns at once (<see cref="ArrangeBody"/>, <see cref="ApplyFilter(bool)"/>).
        /// </summary>
        private void PostFitColumns()
        {
            if (!IsHandleCreated)
            {
                FitColumns();
                return;
            }
            if (_fitPosted) return;
            _fitPosted = true;
            BeginInvoke((Action)(() =>
            {
                _fitPosted = false;
                if (!IsDisposed) FitColumns();
            }));
        }

        /// <summary>
        /// The five widths for a list <paramref name="available"/> pixels wide: the base widths with the rest to Address; when
        /// there isn't room, Company shrinks to its minimum first, then What it is, then Address. Whenever the minimums fit,
        /// the widths add up to exactly <paramref name="available"/>; below that, the minimums.
        /// </summary>
        internal static int[] FitWidths(int available, int[] widths, int[] min)
        {
            int[] fitted = (int[])widths.Clone();
            int sum = fitted.Sum();
            if (available >= sum)
            {
                fitted[0] += available - sum;
                return fitted;
            }
            int over = sum - available;
            foreach (int i in new[] { 1, 2, 0 })
            {
                int cut = Math.Max(0, Math.Min(over, fitted[i] - min[i]));
                fitted[i] -= cut;
                over -= cut;
            }
            return fitted;
        }

        /// <summary>
        /// Address: the address and, when its note isn't empty, the info glyph 8 epx from the cell's right edge. Level: the
        /// "Strict" outline pill on Strict rows. Status: a pill for Blocked and Allowed by you, plain secondary text otherwise.
        /// </summary>
        private bool PaintCell(ListCell cell)
        {
            UiScale s = Scale;
            Graphics g = cell.Graphics;
            var r = cell.Item.Tag as FixRow;
            const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            switch (cell.Column)
            {
                case 0:
                    {
                        Rectangle text = cell.Content;
                        if (r != null && r.Note.Length > 0)
                        {
                            int px = s.Px(16);
                            var box = new Rectangle(cell.Bounds.Right - s.Px(8) - px, cell.Bounds.Y + (cell.Bounds.Height - px) / 2, px, px);
                            GlyphFont.Draw(g, Glyph.Info, box, cell.SecondaryTextColor);
                            text = new Rectangle(text.X, text.Y, Math.Max(0, box.X - s.Px(4) - text.X), text.Height);
                        }
                        TextRenderer.DrawText(g, cell.Item.Text, Ramp.Body, text, cell.TextColor, flags);
                        return true;
                    }
                case 3:
                    if (r != null && r.Tier == Tier.Strict) DrawPill(cell, PillKind.Outline, Strings.TierName(Tier.Strict));
                    return true;
                case StatusColumn:
                    {
                        string status = cell.Item.SubItems.Count > StatusColumn ? cell.Item.SubItems[StatusColumn].Text : string.Empty;
                        if (status == Strings.StatusBlocked) DrawPill(cell, PillKind.Neutral, status);
                        else if (status == Strings.StatusAllowed) DrawPill(cell, PillKind.Accent, status);
                        else TextRenderer.DrawText(g, status, Ramp.Body, cell.Content, cell.SecondaryTextColor, flags);
                        return true;
                    }
                default:
                    return false;
            }
        }

        /// <summary>A pill at the cell's text position: the text measured on the row's device context plus 8 epx on each side (2.8.8).</summary>
        private void DrawPill(ListCell cell, PillKind kind, string text)
        {
            int height = Pill.Height(Ramp);
            int textWidth = TextRenderer.MeasureText(cell.Graphics, text, Ramp.Caption, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
            int width = Math.Max(height, textWidth + 2 * Scale.Px(Metrics.PillPadX));
            var bounds = new Rectangle(cell.Content.X, cell.Bounds.Y + (cell.Bounds.Height - height) / 2, Math.Min(width, Math.Max(0, cell.Bounds.Right - cell.Content.X)), height);
            Pill.Draw(cell.Graphics, Ramp, kind, text, bounds, cell.Highlighted, cell.Theme);
        }

        // ---------------------------------------------------------------- input

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
            {
                // Down enters the list (the first row, if no row has the focus or is selected).
                _list.Focus();
                if (_list.FocusedItem == null || _list.SelectedIndices.Count == 0)
                {
                    _list.Items[0].Focused = true;
                    _list.Items[0].Selected = true;
                }
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter)
            {
                // Enter applies the search at once (the dialog has no default button).
                ApplyFilter();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void OnListKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                _list.BeginUpdate();
                try
                {
                    foreach (ListViewItem item in _list.Items) item.Selected = true;
                }
                finally
                {
                    _list.EndUpdate();
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                _search.TextBox.Focus();
                _search.TextBox.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Enter on the step buttons: swallowed by ThemedWindow and by the buttons themselves (SwallowsEnter, K2). Allow and
        // Block again take it as a focused button does; the list and the search box have no default button behind them.

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            // Ctrl+A raises this once per row: update once, after the last one.
            if (!IsHandleCreated)
            {
                AfterSelection();
                return;
            }
            if (_selectionUpdatePending) return;
            _selectionUpdatePending = true;
            BeginInvoke(new Action(() =>
            {
                _selectionUpdatePending = false;
                if (!IsDisposed) AfterSelection();
            }));
        }

        private void AfterSelection()
        {
            UpdateButtons();
            UpdateFooter();
            UpdateRowDetails();
        }

        private async void OnAllow(object? sender, EventArgs e)
        {
            List<string> hosts = SelectedRows().Where(r => !r.Allowed).Select(r => r.Host).ToList();
            if (hosts.Count == 0 || hosts.Count > Limits.MaxHostsPerRun || SelectedCount > Limits.MaxHostsPerRun) return;
            await _c.RunAsync(ElevatedCommand.Allow(hosts), ChangeKind.Allow, this);
        }

        private async void OnBlockAgain(object? sender, EventArgs e)
        {
            List<string> hosts = SelectedRows().Where(r => r.Allowed).Select(r => r.Host).ToList();
            if (hosts.Count == 0 || hosts.Count > Limits.MaxHostsPerRun || SelectedCount > Limits.MaxHostsPerRun) return;
            await _c.RunAsync(ElevatedCommand.Block(hosts), ChangeKind.Block, this);
        }

        private async void OnSwitchToStandard(object? sender, EventArgs e)
        {
            if (!_c.CanStartChange || !StrictStepShown) return;
            await _c.RunAsync(ElevatedCommand.On(Level.Standard), ChangeKind.ChangeLevel, this);
        }

        /// <summary>The last card's button: "Turn off for now" (a test: the main window then says so), or one of the turn-on buttons.</summary>
        private async void OnLastStep(object? sender, EventArgs e)
        {
            if (!_c.CanStartChange) return;
            HostsStatus s = _c.Status;
            if (_lastStep == LastStep.Test && s.State == ProtectionState.On) await _c.RunAsync(ElevatedCommand.Off(), ChangeKind.TurnOffForTest, this);
            else if (_lastStep != LastStep.Test && _lastStep != LastStep.CantChange && CanTurnOn(s.State))
                await _c.RunAsync(ElevatedCommand.On(s.SelectedLevel), ChangeKind.TurnOn, this);
        }

        /// <summary>Copy details (5.12): the site report, which never names an address; the line after it says whether it worked.</summary>
        private void CopySiteReport()
        {
            string report = DetailsReport.BuildSiteReport(_ctx, _c.Status);
            bool copied;
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewClipboard != null) copied = PreviewClipboard(report);
            else
#endif
                // A preview copies nothing and shows what a copy that worked would.
                copied = _ctx.Preview || DetailsReport.TryCopy(report);
            _copyFeedback.Text = copied ? Strings.DetailsCopied : Strings.DetailsNotCopied;
            _copyFeedback.Visible = true;
            Ui.Announce(this, _copyFeedback.Text, Announcement.Result);
            FitBody();
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

        // ---------------------------------------------------------------- layout and size

        /// <summary>
        /// The body's rows (5.2) at <paramref name="width"/> pixels; with <paramref name="apply"/> they are placed, the list card
        /// taking whatever of <paramref name="height"/> the other rows leave. Returns the body's smallest height at this width:
        /// the list card with its header and four rows (and the result bar while it shows, unless <paramref name="withResult"/>
        /// is false).
        /// </summary>
        private int ArrangeBody(int width, int height, bool apply, bool withResult = true)
        {
            UiScale s = Scale;
            int pad = s.Px(Metrics.PagePadding);
            int x = pad, w = Math.Max(1, width - 2 * pad);
            int y = pad;

            Size intro = Preferred(_intro, w);
            if (apply) _intro.SetBounds(x, y, Math.Min(w, intro.Width), intro.Height);
            y += intro.Height + s.Px(Metrics.ButtonGap);

            int steps = _steps.HeightFor(w);
            if (apply) _steps.SetBounds(x, y, w, steps);
            y += steps + s.Px(Metrics.ButtonGap);

            // The flow reaches a link's padding left of the column, so a link that starts a line lines up with the prompt.
            int reach = _reportLine.Inset - TextFlow.Lead(_reportPrompt);
            _reportLine.MaximumSize = new Size(w + reach, 0);
            Size report = _reportLine.GetPreferredSize(new Size(w + reach, 0));
            if (apply) _reportLine.SetBounds(x - reach, y, Math.Min(w + reach, report.Width), report.Height);
            y += report.Height + s.Px(Metrics.SectionGap);

            Size label = Preferred(_searchLabel, w);
            if (apply) _searchLabel.SetBounds(x, y, Math.Min(w, label.Width), label.Height);
            y += label.Height + s.Px(Metrics.HeadingGap);

            // The search box fills the row; the filter keeps its own width, 16 epx after it (its box starts 4 epx in). Where
            // that would leave the box narrower than 240 epx (large text, a narrow window), the filter goes 8 epx below the box,
            // its box in line with the search box, and its text ends in an ellipsis if even that row is too narrow.
            Size filter = _onlyAllowed.GetPreferredSize(Size.Empty);
            int searchHeight = _search.GetPreferredSize(new Size(w, 0)).Height;
            int filterGap = Math.Max(0, s.Px(16) - s.Px(ThemedCheckBox.RingRoom));
            int searchWidth = w - filterGap - filter.Width;
            if (searchWidth >= s.Px(MinSearchWidth))
            {
                int row = Math.Max(searchHeight, filter.Height);
                if (apply)
                {
                    _search.SetBounds(x, y + (row - searchHeight) / 2, searchWidth, searchHeight);
                    _onlyAllowed.SetBounds(x + searchWidth + filterGap, y + (row - filter.Height) / 2, filter.Width, filter.Height);
                }
                y += row + s.Px(12);
            }
            else
            {
                int ring = s.Px(ThemedCheckBox.RingRoom);
                int filterTop = y + searchHeight + s.Px(Metrics.ButtonGap);
                if (apply)
                {
                    _search.SetBounds(x, y, w, searchHeight);
                    _onlyAllowed.SetBounds(x - ring, filterTop, Math.Min(filter.Width, w + ring), filter.Height);
                }
                y = filterTop + filter.Height + s.Px(12);
            }

            int listTop = y;
            Size details = Preferred(_details, w);
            details.Height = Math.Max(details.Height, 2 * Ramp.LineHeight(TextStyle.Body));
            bool result = _resultShown && withResult;
            int resultHeight = result ? _resultBar.GetPreferredSize(new Size(w, 0)).Height : 0;
            int below = s.Px(Metrics.ButtonGap) + details.Height + (result ? s.Px(12) + resultHeight : 0) + pad;
            int minList = MinListCardHeight(w);
            int list = Math.Max(minList, height - listTop - below);
            if (apply)
            {
                _listCard.SetBounds(x, listTop, w, list);
                // The list has its new size now, outside its own size message (see PostFitColumns).
                FitColumns();
                PlaceEmptyState();
                int dy = listTop + list + s.Px(Metrics.ButtonGap);
                _details.SetBounds(x, dy, Math.Min(w, details.Width), details.Height);
                if (result) _resultBar.SetBounds(x, dy + details.Height + s.Px(12), w, resultHeight);
            }
            return listTop + minList + below;
        }

        private static Size Preferred(ThemedLabel label, int width)
        {
            label.MaximumSize = new Size(Math.Max(1, width), 0);
            return label.GetPreferredSize(new Size(Math.Max(1, width), 0));
        }

        /// <summary>The narrowest search box beside the filter (5.2 row 5); narrower, the filter goes below it.</summary>
        private const int MinSearchWidth = 240;

        /// <summary>
        /// The list card's smallest height at <paramref name="width"/> pixels wide: its 4 epx padding, the header and four rows
        /// (D18), and room for the horizontal scroll bar when even the minimum columns are wider than the list (large text at
        /// a narrow width), so four rows still show above it.
        /// </summary>
        private int MinListCardHeight(int width)
        {
            int pad = Scale.Px(4);
            int height = 2 * pad + _list.HeaderHeight + 4 * _list.RowHeight;
            int client = width - 2 * pad - SystemInformation.VerticalScrollBarWidth;
            if (ColumnWidths().Min.Sum() > client) height += SystemInformation.HorizontalScrollBarHeight;
            return height;
        }

        /// <summary>The no-match state covers the list below its header.</summary>
        private void PlaceEmptyState()
        {
            int pad = Scale.Px(4);
            int header = _list.HeaderHeight;
            var list = new Rectangle(pad, pad, Math.Max(0, _listCard.Width - 2 * pad), Math.Max(0, _listCard.Height - 2 * pad));
            _empty.SetBounds(list.X, list.Y + header, list.Width, Math.Max(0, list.Height - header));
        }

        /// <summary>
        /// The body fills the room above the footer while that is at least its smallest height; on a shorter client area it
        /// keeps its smallest height and scrolls (the list keeps four rows, the footer stays visible, D18).
        /// </summary>
        private void FitBody()
        {
            if (_fitting || _body == null || !IsHandleCreatedOrLaidOut) return;
            _fitting = true;
            try
            {
                int width = _host.Width, height = _host.Height;
                if (width <= 0 || height <= 0) return;
                int need = ArrangeBody(width, 0, apply: false);
                if (need <= height)
                {
                    if (_host.AutoScroll)
                    {
                        _host.AutoScrollPosition = Point.Empty;
                        _host.AutoScroll = false;
                    }
                    _body.SetBounds(0, 0, width, height);
                }
                else
                {
                    int inner = Math.Max(1, width - SystemInformation.VerticalScrollBarWidth);
                    need = ArrangeBody(inner, 0, apply: false);
                    if (!_host.AutoScroll) _host.AutoScroll = true;
                    _body.SetBounds(0, _host.AutoScrollPosition.Y, inner, need);
                }
                _body.PerformLayout();
            }
            finally
            {
                _fitting = false;
            }
        }

        private bool IsHandleCreatedOrLaidOut => _host != null;

        /// <summary>
        /// The smallest client size: 640 epx wide, and as tall as the footer and the body with four list rows (no result bar),
        /// measured at that width. When the step cards stack only there (Strict's three cards are 192 epx wide at 640), it is
        /// measured at the default width of 760 epx instead: the dialog then still opens at 760 × 680, and a window made
        /// narrower than the cards need scrolls its body, as a shorter one does (D18).
        /// </summary>
        private void ApplyMinimumSize()
        {
            UiScale s = Scale;
            int width = s.Px(Metrics.FixMinClientWidth);
            int padding = 2 * s.Px(Metrics.PagePadding);
            int measured = width;
            if (_steps.StacksAt(width - padding) && !_steps.StacksAt(s.Px(Metrics.FixDefaultClientWidth) - padding)) measured = s.Px(Metrics.FixDefaultClientWidth);
            int body = ArrangeBody(measured, 0, apply: false, withResult: false);
            int footer = _footer.GetPreferredSize(new Size(measured, 0)).Height;
            int height = Math.Max(s.Px(Metrics.FixMinClientHeight), footer + body);
            MinimumSize = SizeFromClientSize(new Size(width, height));
        }

        protected override void OnLoad(EventArgs e)
        {
            // Before base.OnLoad centers the dialog on its owner.
            ApplyMinimumSize();
            FitToArea(OwnerWorkingArea());
            // A result already showing (only a preview sets one before the dialog opens) gets its room as one arriving later.
            if (_resultShown) GrowForResult();
            base.OnLoad(e);
            FitBody();
            FitColumns();
        }

        /// <summary>
        /// Never larger than the screen it opens on: when the smallest size doesn't fit (large text, a short screen), it shrinks
        /// to the work area too, and the body scrolls above the footer.
        /// </summary>
        private void FitToArea(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            Size min = MinimumSize;
            if (min.Width > area.Width || min.Height > area.Height)
                MinimumSize = new Size(Math.Min(min.Width, area.Width), Math.Min(min.Height, area.Height));
            if (Width > area.Width || Height > area.Height)
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (WindowState != FormWindowState.Normal) return;
            // The client size in epx (D11): the same size comes back at any display scale or text size. The height a result
            // bar added isn't the user's choice, so it isn't remembered.
            double factor = Scale.Dpi / 96.0;
            int height = ClientSize.Height - (_grownForResult > 0 && Height == _heightWhenGrown ? _grownForResult : 0);
            _c.SaveFixSize((int)Math.Round(ClientSize.Width / factor), (int)Math.Round(height / factor));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _c.StatusChanged -= OnStatusChanged;
                _c.BusyChanged -= OnBusyChanged;
                _c.ChangeFinished -= OnChangeFinished;
                _searchTimer.Dispose();
                // The footer parts that aren't in the footer now.
                foreach (Control c in new Control[] { _ring, _activity, _footerLine, _countLine })
                {
                    if (c.Parent == null) c.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>The body: its rows laid out by the dialog (<see cref="ArrangeBody"/>), on the page color.</summary>
        private sealed class FixBody : Panel, IThemed
        {
            private readonly FixDialog _owner;

            public FixBody(FixDialog owner)
            {
                _owner = owner;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
                Margin = Padding.Empty;
                Padding = Padding.Empty;
                // A layout panel has no name (Windows would otherwise name it after a label before it).
                AccessibleName = string.Empty;
                BackColor = ThemeManager.Current.WindowBackground;
            }

            public void ApplyTheme(Theme theme)
            {
                BackColor = theme.WindowBackground;
                Invalidate();
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                if (ClientSize.Width <= 0) return;
                _owner.ArrangeBody(ClientSize.Width, ClientSize.Height, apply: true);
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: search now, without the 200 ms delay.</summary>
        internal void PreviewSearch(string text)
        {
            _search.Text = text;
            ApplyFilter();
        }

        /// <summary>For --render-ui and tests: select the first rows.</summary>
        internal void PreviewSelect(int count)
        {
            _list.BeginUpdate();
            try
            {
                for (int i = 0; i < count && i < _list.Items.Count; i++) _list.Items[i].Selected = true;
            }
            finally
            {
                _list.EndUpdate();
            }
            AfterSelection();
        }

        /// <summary>For --render-ui and tests: select the row of <paramref name="host"/> (and give it the list's focus).</summary>
        internal void PreviewSelectHost(string host)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (!string.Equals(item.Text, host, StringComparison.Ordinal)) continue;
                item.Selected = true;
                if (_list.IsHandleCreated) item.Focused = true;
                break;
            }
            AfterSelection();
        }

        /// <summary>For --render-ui and tests: select the first <paramref name="count"/> allowed rows.</summary>
        internal void PreviewSelectAllowed(int count)
        {
            _list.BeginUpdate();
            try
            {
                int n = 0;
                foreach (ListViewItem item in _list.Items)
                {
                    if (n >= count) break;
                    if (item.Tag is FixRow r && r.Allowed)
                    {
                        item.Selected = true;
                        n++;
                    }
                }
            }
            finally
            {
                _list.EndUpdate();
            }
            AfterSelection();
        }

        /// <summary>For --render-ui and tests: "Show only addresses you allowed", checked.</summary>
        internal void PreviewFilterAllowed() => _onlyAllowed.Checked = true;

        /// <summary>For --render-ui: the smallest client size the dialog allows on this screen.</summary>
        internal void PreviewMinimumSize()
        {
            Size = MinimumSize;
            FitBody();
        }

        internal int ShownRows => _shown;
        internal int TotalRows => _rows.Count;
        /// <summary>The footer's left side: progress, what a click would do, or the count (with the selected suffix).</summary>
        internal string FooterText
        {
            get
            {
                if (CountShown) return _countLabel.Text + (LayoutUtil.IsShown(_suffixLabel) ? _suffixLabel.Text : string.Empty);
                if (_footerLeft != null && _footerLeft.Length == 2) return _activity.Text;
                return _footerLine.Text;
            }
        }
        internal string RowDetails => _details.Text;
        internal bool CanAllow => _allow.Enabled;
        internal bool CanBlockAgain => _blockAgain.Enabled;

        /// <summary>For tests: fit to <see cref="ThemedWindow.PreviewWorkingArea"/> as showing the dialog would.</summary>
        internal void PreviewFit()
        {
            ApplyMinimumSize();
            FitToArea(OwnerWorkingArea());
            FitBody();
        }

        /// <summary>For tests: act as the active window (a test's window never is), so the focus comes back after a change.</summary>
        internal bool PreviewForeground { get; set; }

        /// <summary>For tests: links open here instead of in the browser.</summary>
        internal Action<string>? PreviewOpenUrl { get; set; }

        /// <summary>For tests: the clipboard (returns whether the copy worked).</summary>
        internal Func<string, bool>? PreviewClipboard { get; set; }

        internal IReadOnlyList<StepCard> PreviewStepCards => new[] { _step1, _step2, _last };
        internal StepCard PreviewLastCard => _last;
        internal StepRow PreviewStepRow => _steps;
        internal ThemedButton PreviewSwitchToStandard => _switchToStandard;
        internal ThemedButton PreviewLastButton => _lastButton;
        internal ThemedLink PreviewReportLink => _reportSite;
        internal ThemedLink PreviewCopyDetailsLink => _copyDetails;
        internal ThemedLabel PreviewCopyFeedback => _copyFeedback;
        internal ThemedLabel PreviewSearchLabel => _searchLabel;
        internal SearchBox PreviewSearchBox => _search;
        internal ThemedCheckBox PreviewFilter => _onlyAllowed;
        internal ThemedListView PreviewList => _list;
        internal CardPanel PreviewListCard => _listCard;
        internal EmptyStatePanel PreviewEmptyState => _empty;
        internal ThemedLabel PreviewDetails => _details;
        internal NoticeBar PreviewResultBar => _resultBar;
        internal ThemedLabel PreviewCountLabel => _countLabel;
        internal ThemedLabel PreviewSuffixLabel => _suffixLabel;
        internal ProgressRing PreviewRing => _ring;
        internal CommandBar PreviewFooter => _footer;
        internal ScrollHost PreviewHost => _host;
        internal ThemedButton PreviewAllow => _allow;
        internal ThemedButton PreviewBlockAgain => _blockAgain;
        internal ThemedButton PreviewClose => _close;
        internal int PreviewMinListCardHeight => MinListCardHeight(_listCard.Width);
        /// <summary>For tests: the columns' base and minimum widths, in pixels.</summary>
        internal (int[] Base, int[] Min) PreviewColumnWidths() => ColumnWidths();
        internal void PreviewSearchKey(Keys key) => OnSearchKeyDown(_search.TextBox, new KeyEventArgs(key));
        internal int PreviewSearchDelay => _searchTimer.Interval;
        internal bool PreviewSearchPending => _searchTimer.Enabled;
#endif
    }
}
