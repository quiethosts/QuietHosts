using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>The main window's notices (spec 3.5.1, N1-N10), in their table order.</summary>
    internal enum NoticeId
    {
        /// <summary>N1: the last change didn't finish (not closable).</summary>
        ChangeNotFinished,
        /// <summary>N2: running from a temporary folder (not closable).</summary>
        RunningFromTemp,
        /// <summary>N3: the hosts file has a newer list than this copy (not closable).</summary>
        NewerListInFile,
        /// <summary>N4: QuietHosts lines changed outside QuietHosts ("edited-block").</summary>
        EditedBlock,
        /// <summary>N5: other programs' lines block Microsoft addresses ("microsoft-lines").</summary>
        MicrosoftLines,
        /// <summary>N6: this copy has a newer list ("newer-list-in-app").</summary>
        NewerListInApp,
        /// <summary>N7: the older block-ads.ps1 list is still there ("legacy-block").</summary>
        LegacyBlock,
        /// <summary>N8: damaged marker lines ("malformed-markers").</summary>
        MalformedMarkers,
        /// <summary>N9: a large hosts file ("large-file").</summary>
        LargeFile,
        /// <summary>N10: the Downloads tip ("downloads-tip").</summary>
        DownloadsTip,
    }

    /// <summary>A notice's one action (at most one per bar).</summary>
    internal enum NoticeAction
    {
        None,
        /// <summary>Re-runs <see cref="AppController.RepairCommand"/> (shield).</summary>
        Repair,
        /// <summary>N6: "on" at the block's level from this copy's lists (shield).</summary>
        UseNewerList,
        /// <summary>N7: the same "on", which removes the older list (shield).</summary>
        RemoveOlderList,
        /// <summary>N5: the Microsoft-lines dialog.</summary>
        ShowLines,
        /// <summary>N3: the releases page (external).</summary>
        GetNewest,
        /// <summary>N10: How it works at "Keep QuietHosts.exe in a folder of its own".</summary>
        Why,
    }

    /// <summary>One notice as the window shows it: the bar's look and text, its action, and how a close is remembered.</summary>
    internal sealed class Notice
    {
        public Notice(NoticeId id, NoticeSeverity severity, string message, bool closable, string? fingerprint, NoticeAction action = NoticeAction.None)
        {
            Id = id;
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Closable = closable;
            Fingerprint = fingerprint;
            Action = action;
        }

        public NoticeId Id { get; }
        public NoticeSeverity Severity { get; }
        public string Message { get; }
        public NoticeAction Action { get; }
        /// <summary>The action's label (null without an action).</summary>
        public string? ActionLabel => LabelFor(Action);
        /// <summary>The action starts an elevated change: a shield button that waits while a change can't start.</summary>
        public bool ActionElevates => Elevates(Action);
        /// <summary>The action opens the browser (a link with the external-link glyph).</summary>
        public bool ActionExternal => Action == NoticeAction.GetNewest;
        public bool Closable { get; }
        /// <summary>The condition a close remembers (a hash, a size, versions or a hashed path); null if not closable.</summary>
        public string? Fingerprint { get; }

        /// <summary>The bar's accessible name: the severity prefix and the message ("Warning: …").</summary>
        public string AccessibleName => NoticeBar.NameFor(Severity, Message);

        internal Notice WithAction(NoticeAction action) => new Notice(Id, Severity, Message, Closable, Fingerprint, action);

        public static bool Elevates(NoticeAction action) =>
            action == NoticeAction.Repair || action == NoticeAction.UseNewerList || action == NoticeAction.RemoveOlderList;

        public static string? LabelFor(NoticeAction action)
        {
            switch (action)
            {
                case NoticeAction.Repair: return Strings.RepairButton;
                case NoticeAction.UseNewerList: return Strings.UseNewerListButton;
                case NoticeAction.RemoveOlderList: return Strings.RemoveOlderListButton;
                case NoticeAction.ShowLines: return Strings.ShowLinesLink;
                case NoticeAction.GetNewest: return Strings.GetNewestLink;
                case NoticeAction.Why: return Strings.WhyLink;
                default: return null;
            }
        }
    }

    /// <summary>
    /// Works out the notices for a status (spec 3.5.1, 3.5.2): which show, in tier and table order, without the ones the
    /// user closed, and which one gets each action. A pure function, so the tests pin the whole table.
    /// </summary>
    internal static class NoticeBuilder
    {
        public static IReadOnlyList<Notice> Build(HostsStatus s, UiContext ctx, NoticeDismissals dismissals)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            dismissals ??= NoticeDismissals.Empty;
            bool on = s.State == ProtectionState.On;
            int listOrder = CompareLists(s, ctx.Lists);
            string? blockVersion = BlockListVersion(s);

            // Warning (N1-N5), then Info (N6-N9), then Tip (N10): the table order is the tier order.
            var all = new List<Notice>();
            if (s.Has(HostsWarnings.ChangeNotFinished))
                all.Add(new Notice(NoticeId.ChangeNotFinished, NoticeSeverity.Warning, Strings.WarningChangeNotFinished, false, null));
            if ((ctx.Tips & RunLocationTips.RunningFromTemp) != 0)
                all.Add(new Notice(NoticeId.RunningFromTemp, NoticeSeverity.Warning, Strings.TipRunningFromTemp, false, null));
            if (listOrder < 0)
                all.Add(new Notice(NoticeId.NewerListInFile, NoticeSeverity.Warning, Strings.NoteNewerListInFile(blockVersion), false, null, NoticeAction.GetNewest));
            if (s.Has(HostsWarnings.EditedBlock))
                all.Add(new Notice(NoticeId.EditedBlock, NoticeSeverity.Warning, Strings.WarningEditedBlock, true, Fingerprint(s.BlockLinesSha256)));
            if (s.Has(HostsWarnings.MicrosoftLinesOutside))
                all.Add(new Notice(NoticeId.MicrosoftLines, NoticeSeverity.Warning, Strings.WarningMicrosoftLinesOutside, true, Fingerprint(s.MicrosoftLinesSha256), NoticeAction.ShowLines));
            if (listOrder > 0)
                all.Add(new Notice(NoticeId.NewerListInApp, NoticeSeverity.Info, Strings.TipNewerListInApp(ctx.Lists.Version), true, AppFingerprint(ctx.Lists)));
            if (s.Has(HostsWarnings.LegacyBlockToo))
                all.Add(new Notice(NoticeId.LegacyBlock, NoticeSeverity.Info, Strings.WarningLegacyBlockToo, true, Fingerprint(s.FileSha256)));
            if (s.Has(HostsWarnings.MalformedMarkers))
                all.Add(new Notice(NoticeId.MalformedMarkers, NoticeSeverity.Info, Strings.WarningMalformedMarkers, true, Fingerprint(s.FileSha256)));
            if (s.Has(HostsWarnings.LargeFile))
                all.Add(new Notice(NoticeId.LargeFile, NoticeSeverity.Info, Strings.WarningLargeFile(s.BytesOutsideBlocks), true, SizeFingerprint(s.BytesOutsideBlocks)));
            if ((ctx.Tips & RunLocationTips.RunningFromDownloads) != 0)
                all.Add(new Notice(NoticeId.DownloadsTip, NoticeSeverity.Tip, Strings.TipRunningFromDownloads, true, FolderFingerprint(ctx.ExePath), NoticeAction.Why));

            // Closed ones go first, so an action moves on to the next notice that shows.
            List<Notice> shown = all.Where(n => !dismissals.Hides(n, s)).ToList();

            // One Repair at a time (N1, then N4, then N8), and while On only one of Repair, Use newer list and Remove it now,
            // in that order (D16): all three run the same "on", which clears every one of these notices.
            ElevatedCommand? repair = AppController.RepairCommand(s);
            NoticeId? repairOn = null;
            if (repair != null)
            {
                foreach (NoticeId id in new[] { NoticeId.ChangeNotFinished, NoticeId.EditedBlock, NoticeId.MalformedMarkers })
                {
                    if (shown.Any(n => n.Id == id))
                    {
                        repairOn = id;
                        break;
                    }
                }
            }
            bool useNewer = on && repairOn == null && shown.Any(n => n.Id == NoticeId.NewerListInApp);
            bool removeOlder = on && repairOn == null && !useNewer && shown.Any(n => n.Id == NoticeId.LegacyBlock);
            for (int i = 0; i < shown.Count; i++)
            {
                Notice n = shown[i];
                if (repairOn == n.Id) shown[i] = n.WithAction(NoticeAction.Repair);
                else if (useNewer && n.Id == NoticeId.NewerListInApp) shown[i] = n.WithAction(NoticeAction.UseNewerList);
                else if (removeOlder && n.Id == NoticeId.LegacyBlock) shown[i] = n.WithAction(NoticeAction.RemoveOlderList);
            }
            return shown;
        }

        /// <summary>
        /// On: the list version in the block's header, if it is a real date ("YYYY-MM-DD"); otherwise null. The header is
        /// plain text in the hosts file, so anything else there is ignored.
        /// </summary>
        public static string? BlockListVersion(HostsStatus s) =>
            s.State == ProtectionState.On && Strings.IsListVersion(s.BlockListVersion) ? s.BlockListVersion : null;

        /// <summary>
        /// On: above 0 when this exe ships a newer list than the block was written from ("Use newer list"), below 0 when the
        /// block is newer than this exe's list (a change made with this copy would put the older list back), otherwise 0.
        /// </summary>
        public static int CompareLists(HostsStatus s, BlockLists lists)
        {
            string? block = BlockListVersion(s);
            if (block == null || !Strings.IsListVersion(lists.Version)) return 0;
            return Math.Sign(string.CompareOrdinal(lists.Version, block));
        }

        /// <summary>A hash fingerprint, or "none" when the status has none (a fake status), so a close still has something to match.</summary>
        private static string Fingerprint(string? hash) => string.IsNullOrEmpty(hash) ? "none" : hash!;

        /// <summary>N6: this app version and its list version (for example app:1.0.0|lists:2026-11-20).</summary>
        public static string AppFingerprint(BlockLists lists) => "app:" + AppInfo.Version + "|lists:" + lists.Version;

        /// <summary>N9: the bytes outside the blocks ("size:1480000").</summary>
        public static string SizeFingerprint(long bytes) => "size:" + bytes.ToString(CultureInfo.InvariantCulture);

        /// <summary>N10: the first 16 hex characters of the SHA-256 of the exe's folder (full path, lowercase, UTF-8); never the path itself.</summary>
        public static string FolderFingerprint(string exePath)
        {
            string folder;
            try
            {
                folder = Path.GetFullPath(Path.GetDirectoryName(exePath) ?? exePath ?? string.Empty);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                folder = exePath ?? string.Empty;
            }
            return "path:" + Hashing.Sha256Hex(Encoding.UTF8.GetBytes(folder.ToLowerInvariant())).Substring(0, 16);
        }
    }

    /// <summary>
    /// The notices the user closed (spec 3.5.3), as "id=fingerprint" entries with the lowercase ids of the catalog. A notice
    /// stays hidden while its fingerprint is the one stored; the large-file notice while the file is at most 1.5 times the
    /// stored size. Unknown ids and malformed entries are dropped. Immutable.
    /// </summary>
    internal sealed class NoticeDismissals
    {
        private static readonly Dictionary<NoticeId, string> Ids = new Dictionary<NoticeId, string>
        {
            { NoticeId.EditedBlock, "edited-block" },
            { NoticeId.MicrosoftLines, "microsoft-lines" },
            { NoticeId.NewerListInApp, "newer-list-in-app" },
            { NoticeId.LegacyBlock, "legacy-block" },
            { NoticeId.MalformedMarkers, "malformed-markers" },
            { NoticeId.LargeFile, "large-file" },
            { NoticeId.DownloadsTip, "downloads-tip" },
        };

        private readonly List<KeyValuePair<NoticeId, string>> _entries;

        private NoticeDismissals(List<KeyValuePair<NoticeId, string>> entries) => _entries = entries;

        public static NoticeDismissals Empty { get; } = new NoticeDismissals(new List<KeyValuePair<NoticeId, string>>());

        /// <summary>The stored id of a closable notice ("edited-block"), or null for the ones that can't be closed.</summary>
        public static string? StoredId(NoticeId id) => Ids.TryGetValue(id, out string? s) ? s : null;

        /// <summary>From the per-user store's entries: cleaned (8.1), unknown ids dropped.</summary>
        public static NoticeDismissals From(IEnumerable<string>? raw)
        {
            var entries = new List<KeyValuePair<NoticeId, string>>();
            foreach (string entry in NoticeDismissalText.Clean(raw))
            {
                int eq = entry.IndexOf('=');
                string id = entry.Substring(0, eq);
                foreach (KeyValuePair<NoticeId, string> known in Ids)
                {
                    if (!string.Equals(known.Value, id, StringComparison.Ordinal)) continue;
                    entries.Add(new KeyValuePair<NoticeId, string>(known.Key, entry.Substring(eq + 1)));
                    break;
                }
            }
            return new NoticeDismissals(entries);
        }

        public bool IsEmpty => _entries.Count == 0;

        /// <summary>The entries to store, "id=fingerprint", one per id.</summary>
        public IReadOnlyList<string> Entries => _entries.Select(e => Ids[e.Key] + "=" + e.Value).ToList();

        /// <summary>The fingerprint stored for <paramref name="id"/>, or null.</summary>
        public string? Get(NoticeId id)
        {
            foreach (KeyValuePair<NoticeId, string> e in _entries)
            {
                if (e.Key == id) return e.Value;
            }
            return null;
        }

        /// <summary>A copy with <paramref name="id"/> closed at <paramref name="fingerprint"/> (it replaces an older close of the same notice).</summary>
        public NoticeDismissals With(NoticeId id, string fingerprint)
        {
            if (StoredId(id) == null || string.IsNullOrEmpty(fingerprint)) return this;
            var entries = new List<KeyValuePair<NoticeId, string>>(_entries);
            var entry = new KeyValuePair<NoticeId, string>(id, fingerprint);
            int at = entries.FindIndex(e => e.Key == id);
            if (at >= 0) entries[at] = entry;
            else entries.Add(entry);
            return new NoticeDismissals(entries);
        }

        /// <summary>The notice was closed and its condition hasn't changed since.</summary>
        public bool Hides(Notice notice, HostsStatus s)
        {
            if (!notice.Closable) return false;
            string? stored = Get(notice.Id);
            if (stored == null) return false;
            if (notice.Id == NoticeId.LargeFile)
            {
                // Hidden until the part from other programs has grown by half again.
                const string prefix = "size:";
                return stored.StartsWith(prefix, StringComparison.Ordinal) &&
                    long.TryParse(stored.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long size) &&
                    size <= Limits.MaxHostsFileBytes && s.BytesOutsideBlocks * 2 <= size * 3;
            }
            return string.Equals(stored, notice.Fingerprint, StringComparison.Ordinal);
        }
    }

    /// <summary>Which notice a bar belongs to (null: the result bar).</summary>
    internal sealed class NoticeEventArgs : EventArgs
    {
        public NoticeEventArgs(Notice? notice) => Notice = notice;

        /// <summary>The notice, or null for the result bar.</summary>
        public Notice? Notice { get; }
    }

    /// <summary>
    /// The notices area under the status card (spec 3.5.2, 3.5.5): the result bar first, then the notices; at most three
    /// bars show, then "Show N more notices", which shows them all until the window closes. One bar per notice is kept and
    /// reused while its notice stays (same control, updated text), so the focus can come back to its action; a bar whose
    /// notice is gone is disposed. 12 epx above the first bar, 8 between bars, the link 8 below the last one, its text lined
    /// up with the bars' edge (the area reaches the link's 4 epx padding further left than the bars).
    /// </summary>
    internal sealed class NoticeArea : Panel, IThemed
    {
        public const int MaxShown = 3;
        private readonly TypeRamp _ramp;
        private readonly int _width;
        private readonly int _inset;
        private readonly Dictionary<NoticeId, NoticeBar> _bars = new Dictionary<NoticeId, NoticeBar>();
        private readonly Dictionary<NoticeId, Notice> _notices = new Dictionary<NoticeId, Notice>();
        private readonly ThemedLink _showMore;
        private NoticeBar? _resultBar;
        private ChangeResult? _result;
        private ThemedLink? _copyDetails;
        // The bars in order (the result bar first), and how many of them show.
        private readonly List<NoticeBar> _order = new List<NoticeBar>();
        private int _visible;

        /// <param name="widthPx">The bars' width.</param>
        /// <param name="insetPx">Room left of the bars (the link's padding before its text), part of the area's width.</param>
        public NoticeArea(TypeRamp ramp, int widthPx, int insetPx = 0)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            _width = widthPx;
            _inset = Math.Max(0, insetPx);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            // A layout panel has no name (Windows would otherwise name it after a label before it).
            AccessibleName = string.Empty;
            _showMore = new ThemedLink(ramp, Strings.ShowMoreNotices(1)) { Visible = false };
            _showMore.Invoked += (s, e) => Expand();
            Controls.Add(_showMore);
        }

        /// <summary>A notice's action was clicked (or a result bar's Copy details: Notice is null).</summary>
        public event EventHandler<NoticeEventArgs>? ActionInvoked;
        /// <summary>A bar's close button was clicked (the result bar: Notice is null). The owner works the notices out again.</summary>
        public event EventHandler<NoticeEventArgs>? CloseRequested;
        /// <summary>"Show N more notices" expanded the area, but the newly shown bars have nothing to focus.</summary>
        public event EventHandler? FocusPastArea;
        /// <summary>"Show N more notices" showed every bar: the window fits its screen again before the focus moves (3.12).</summary>
        public event EventHandler? AllShown;

        /// <summary>Every bar shows ("Show N more notices" was clicked); it stays so until the window closes.</summary>
        public bool Expanded { get; private set; }

        /// <summary>At least one bar shows.</summary>
        public bool HasBars => _visible > 0;

        /// <summary>The result bar (null until a result showed, and while none shows).</summary>
        public NoticeBar? ResultBar => _result != null ? _resultBar : null;

        /// <summary>The bars in order, the result bar first, shown or collapsed.</summary>
        public IReadOnlyList<NoticeBar> Bars => _order;

        /// <summary>How many bars show (the rest are behind "Show N more notices").</summary>
        public int VisibleCount => _visible;

        public ThemedLink ShowMoreLink => _showMore;

        /// <summary>The bar of a notice, or null.</summary>
        public NoticeBar? BarFor(NoticeId id) => _bars.TryGetValue(id, out NoticeBar? bar) ? bar : null;

        /// <summary>The notice a bar shows, or null for the result bar.</summary>
        public Notice? NoticeOf(NoticeBar bar)
        {
            foreach (KeyValuePair<NoticeId, NoticeBar> b in _bars)
            {
                if (ReferenceEquals(b.Value, bar)) return _notices[b.Key];
            }
            return null;
        }

        /// <summary>The result bar's Copy details link (R2), or null.</summary>
        public ThemedLink? CopyDetailsLink => _result != null && _result.OffersSelfCheckDetails ? _copyDetails : null;

        /// <summary>
        /// Shows <paramref name="result"/> (null: no result bar) and <paramref name="notices"/> in this order. Elevating
        /// actions are enabled only while <paramref name="canStartChange"/>.
        /// </summary>
        public void Show(ChangeResult? result, IReadOnlyList<Notice> notices, bool canStartChange)
        {
            SuspendLayout();
            try
            {
                ShowResult(result);
                var keep = new HashSet<NoticeId>(notices.Select(n => n.Id));
                foreach (NoticeId gone in _bars.Keys.Where(id => !keep.Contains(id)).ToList())
                {
                    NoticeBar bar = _bars[gone];
                    _bars.Remove(gone);
                    _notices.Remove(gone);
                    Controls.Remove(bar);
                    bar.Dispose();
                }
                _order.Clear();
                if (_result != null && _resultBar != null) _order.Add(_resultBar);
                foreach (Notice n in notices)
                {
                    NoticeBar bar = BarOf(n);
                    SetAction(bar, n, canStartChange);
                    _notices[n.Id] = n;
                    _order.Add(bar);
                }
                _visible = Expanded ? _order.Count : Math.Min(MaxShown, _order.Count);
                for (int i = 0; i < _order.Count; i++)
                {
                    _order[i].Visible = i < _visible;
                    _order[i].TabIndex = i;
                    // The child order is the order screen readers read in: the bars as they show (a result bar made after
                    // the others first), then "Show N more notices".
                    Controls.SetChildIndex(_order[i], i);
                }
                int more = _order.Count - _visible;
                _showMore.Visible = more > 0;
                if (more > 0) _showMore.Text = Strings.ShowMoreNotices(more);
                _showMore.TabIndex = _order.Count;
                Controls.SetChildIndex(_showMore, _order.Count);
            }
            finally
            {
                ResumeLayout(false);
            }
            Relayout();
        }

        /// <summary>Enables or disables the elevating actions only (a change started or ended); nothing else changes.</summary>
        public void SetCanStartChange(bool canStartChange)
        {
            foreach (NoticeBar bar in _order)
            {
                Notice? n = NoticeOf(bar);
                if (n != null && n.ActionElevates && bar.Action != null) bar.Action.Enabled = canStartChange;
            }
        }

        /// <summary>The feedback line under the result bar's Copy details ("Details copied. …"); null hides it.</summary>
        public void SetResultFeedback(string? feedback)
        {
            if (_resultBar == null || _result == null) return;
            _resultBar.Feedback = feedback;
            Relayout();
        }

        private void ShowResult(ChangeResult? result)
        {
            if (result == null)
            {
                _result = null;
                if (_resultBar != null) _resultBar.Visible = false;
                return;
            }
            if (_resultBar == null)
            {
                _resultBar = new NoticeBar(_ramp, result.Severity, result.Text, closable: true) { FixedWidth = _width };
                _resultBar.CloseRequested += (s, e) => CloseRequested?.Invoke(this, new NoticeEventArgs(null));
                Controls.Add(_resultBar);
            }
            // A new result: its own look, and no feedback from an earlier Copy details.
            if (!ReferenceEquals(_result, result))
            {
                _resultBar.Severity = result.Severity;
                _resultBar.Message = result.Text;
                _resultBar.Feedback = null;
            }
            _result = result;
            if (result.OffersSelfCheckDetails)
            {
                if (_copyDetails == null)
                {
                    _copyDetails = new ThemedLink(_ramp, Strings.CopySelfCheckDetailsLink);
                    _copyDetails.Invoked += (s, e) => ActionInvoked?.Invoke(this, new NoticeEventArgs(null));
                }
                // Kept while no bar held it: it may have missed a theme change.
                if (_copyDetails.Parent == null) _copyDetails.ApplyTheme(ThemeManager.Current);
                _resultBar.Action = _copyDetails;
            }
            else
            {
                _resultBar.Action = null;
            }
        }

        private NoticeBar BarOf(Notice n)
        {
            if (_bars.TryGetValue(n.Id, out NoticeBar? bar))
            {
                // The same bar, updated: the focus can come back to it.
                bar.Severity = n.Severity;
                if (!string.Equals(bar.Message, n.Message, StringComparison.Ordinal)) bar.Message = n.Message;
                return bar;
            }
            bar = new NoticeBar(_ramp, n.Severity, n.Message, n.Closable) { FixedWidth = _width };
            NoticeId id = n.Id;
            bar.CloseRequested += (s, e) =>
            {
                if (_notices.TryGetValue(id, out Notice? shown)) CloseRequested?.Invoke(this, new NoticeEventArgs(shown));
            };
            _bars[n.Id] = bar;
            Controls.Add(bar);
            return bar;
        }

        /// <summary>The bar's action control for the notice's action: the same control while the action stays the same.</summary>
        private void SetAction(NoticeBar bar, Notice n, bool canStartChange)
        {
            Control? current = bar.Action;
            if (n.Action == NoticeAction.None)
            {
                if (current != null)
                {
                    bar.Action = null;
                    current.Dispose();
                }
                return;
            }
            if (current == null || !(current.Tag is NoticeAction kind) || kind != n.Action)
            {
                Control action = CreateAction(n);
                bar.Action = action;
                current?.Dispose();
                current = action;
            }
            current.Enabled = !n.ActionElevates || canStartChange;
        }

        private Control CreateAction(Notice n)
        {
            NoticeId id = n.Id;
            EventHandler invoked = (s, e) =>
            {
                if (_notices.TryGetValue(id, out Notice? shown)) ActionInvoked?.Invoke(this, new NoticeEventArgs(shown));
            };
            string label = n.ActionLabel ?? string.Empty;
            if (n.ActionElevates)
            {
                // A file-changing action: the shield, and Enter never runs it (K2).
                var button = new ThemedButton(_ramp, label) { Shield = true, SwallowsEnter = true, Tag = n.Action };
                button.Click += invoked;
                return button;
            }
            var link = new ThemedLink(_ramp, label) { External = n.ActionExternal, Tag = n.Action };
            link.Invoked += invoked;
            return link;
        }

        /// <summary>"Show N more notices": every bar shows, and the focus moves to the first newly shown bar.</summary>
        private void Expand()
        {
            if (Expanded) return;
            int firstNew = _visible;
            Expanded = true;
            SuspendLayout();
            try
            {
                _visible = _order.Count;
                foreach (NoticeBar bar in _order) bar.Visible = true;
                _showMore.Visible = false;
            }
            finally
            {
                ResumeLayout(false);
            }
            Relayout();
            // The page grew: the window refits (the page may scroll now, the footer row stays), then the focus moves, which
            // scrolls the bar into view.
            AllShown?.Invoke(this, EventArgs.Empty);
            Control? target = firstNew < _order.Count ? FirstFocusable(_order[firstNew]) : null;
            if (target != null) target.Focus();
            else FocusPastArea?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A bar's first control that can take the focus: its action, else its close button.</summary>
        public static Control? FirstFocusable(NoticeBar bar)
        {
            if (bar == null || bar.IsDisposed) return null;
            Control? action = bar.Action;
            if (action != null && action.Enabled && LayoutUtil.IsShown(action)) return action;
            ThemedButton? close = bar.CloseButton;
            return close != null && close.Enabled ? close : null;
        }

        private void Relayout()
        {
            Parent?.PerformLayout(this, nameof(Bounds));
            PerformLayout();
            Invalidate();
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        // ---------------------------------------------------------------- layout

        public override Size GetPreferredSize(Size proposedSize) => new Size(_inset + _width, Arrange(apply: false));

        protected override void OnLayout(LayoutEventArgs levent)
        {
            if (_showMore == null) return;
            Arrange(apply: true);
        }

        private int Arrange(bool apply)
        {
            if (_showMore == null || _visible == 0) return 0;
            UiScale s = _ramp.Scale;
            int y = s.Px(Metrics.NoticeTop);
            for (int i = 0; i < _visible; i++)
            {
                NoticeBar bar = _order[i];
                if (i > 0) y += s.Px(Metrics.NoticeGap);
                int h = bar.GetPreferredSize(new Size(_width, 0)).Height;
                if (apply) bar.SetBounds(_inset, y, _width, h);
                y += h;
            }
            if (LayoutUtil.IsShown(_showMore))
            {
                y += s.Px(Metrics.NoticeGap);
                Size link = _showMore.GetPreferredSize(Size.Empty);
                // The link's text lines up with the bars' edge (its box has a 4 epx padding before the text).
                if (apply) _showMore.SetBounds(Math.Max(0, _inset - _showMore.Padding.Left), y, link.Width, link.Height);
                y += link.Height;
            }
            return y;
        }

        protected override void Dispose(bool disposing)
        {
            // The Copy details link is kept between results, also while no bar holds it.
            if (disposing && _copyDetails != null && _copyDetails.Parent == null) _copyDetails.Dispose();
            base.Dispose(disposing);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: as if "Show N more notices" was clicked.</summary>
        internal void PreviewExpand() => Expand();
#endif
    }
}
