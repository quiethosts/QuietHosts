using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace QuietHosts
{
    /// <summary>Helpers the windows share: table rows, the Enter rule and announcements for screen readers.</summary>
    internal static class Ui
    {
        /// <summary>Adds an AutoSize row with one control per column; null leaves a cell empty.</summary>
        public static int AddRow(TableLayoutPanel t, params Control?[] cells)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int col = 0; col < cells.Length; col++)
            {
                if (cells[col] != null) t.Controls.Add(cells[col], col, row);
            }
            return row;
        }

        /// <summary>
        /// The keys with which a form clicks its focused button: Enter, also with Shift (not with Alt or Ctrl). The windows
        /// swallow them on buttons that switch protection, so Enter never does that by accident.
        /// </summary>
        public static bool IsEnterClick(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Enter && (keyData & (Keys.Alt | Keys.Control)) == Keys.None;

        /// <summary>
        /// Says something to screen readers (spec 2.8.19): a result, progress, or a notice that newly appeared. Needs .NET
        /// 4.8 and Windows 10 1709 or later; elsewhere it does nothing.
        /// </summary>
        public static void Announce(Control control, string? text, Announcement kind = Announcement.Result)
        {
            if (string.IsNullOrEmpty(text) || control.IsDisposed) return;
#if DEBUG || QUIETHOSTS_UI_TESTS
            AnnouncementSink?.Invoke(control, text!, kind);
#endif
            if (!control.IsHandleCreated) return;
            AutomationNotificationKind notification;
            AutomationNotificationProcessing processing;
            switch (kind)
            {
                case Announcement.Progress:
                    // UI Automation has no "in progress" notification kind (ItemAdded, ItemRemoved, ActionCompleted,
                    // ActionAborted, Other): Other, and only the most recent one is read.
                    notification = AutomationNotificationKind.Other;
                    processing = AutomationNotificationProcessing.MostRecent;
                    break;
                case Announcement.Notice:
                    notification = AutomationNotificationKind.Other;
                    processing = AutomationNotificationProcessing.All;
                    break;
                default:
                    notification = AutomationNotificationKind.ActionCompleted;
                    processing = AutomationNotificationProcessing.ImportantMostRecent;
                    break;
            }
            try
            {
                control.AccessibilityObject.RaiseAutomationNotification(notification, processing, text);
            }
            catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException || ex is COMException || ex is InvalidOperationException)
            {
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: sees every announcement (control, text, kind), also from windows whose handle doesn't exist yet.</summary>
        internal static Action<Control, string, Announcement>? AnnouncementSink { get; set; }
#endif
    }

    /// <summary>What an announcement is (spec 2.8.19); it picks the notification kind and processing.</summary>
    internal enum Announcement
    {
        /// <summary>A change's result, copy feedback, a level saved while off: ActionCompleted, ImportantMostRecent.</summary>
        Result,
        /// <summary>"Changing the hosts file…", taking longer, already changing, a search count: Other, MostRecent.</summary>
        Progress,
        /// <summary>A notice that newly appears after a re-read: Other, All.</summary>
        Notice,
    }

    /// <summary>
    /// Holds a window's content. In the small windows it shows one control at a time, at the top left, sizing itself: the
    /// host is exactly as large as that control, unless the window would be taller than its screen's working area (a large
    /// text size on a small screen); then the host gets a fixed height and scrolls, so the window fits on the screen and the
    /// footer below the host (the first-run buttons, a dialog's buttons) stays visible. In the Fix dialog it fills the room
    /// above the footer and the dialog sizes its content (<see cref="ThemedWindow.AddFillingContent"/>). Tab scrolls the
    /// focused control into view; its scroll bar follows the theme.
    /// </summary>
    internal sealed class ScrollHost : Panel, IThemed
    {
        private Control? _content;

        public ScrollHost()
        {
            // Double-buffered, like every panel of the redesign (2.8).
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            // A layout panel has no name (Windows would otherwise name it after a label before it).
            AccessibleName = string.Empty;
        }

        /// <summary>The surface it shows behind its content: the page, or a dialog body's card color.</summary>
        public ThemeToken Background { get; set; } = ThemeToken.WindowBackground;

        /// <summary>The surface color and the native theme name of its scroll bar (dark scroll bars in dark mode).</summary>
        public void ApplyTheme(Theme theme)
        {
            BackColor = theme[Background];
            if (IsHandleCreated) NativeTheme.SetTheme(Handle, theme.NativeThemeName);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A recreated handle loses its theme name.
            NativeTheme.SetTheme(Handle, ThemeManager.Current.NativeThemeName);
        }

        /// <summary>The scroll position from the top, in pixels.</summary>
        public int ScrollTop => AutoScroll ? -AutoScrollPosition.Y : 0;

        /// <summary>How far the content can scroll, in pixels (0 when it fits).</summary>
        public int ScrollRange => AutoScroll ? Math.Max(0, DisplayRectangle.Height - ClientSize.Height) : 0;

        /// <summary>Scrolls to <paramref name="top"/> pixels from the top (clamped); the focus stays where it is.</summary>
        public void ScrollTo(int top)
        {
            if (!AutoScroll) return;
            AutoScrollPosition = new Point(-AutoScrollPosition.X, Math.Max(0, Math.Min(ScrollRange, top)));
        }

        /// <summary>The control shown; set it when switching to another added control.</summary>
        public Control? Content
        {
            get => _content;
            set
            {
                _content = value;
                ScrollToTop();
            }
        }

        /// <summary>True while the content is taller than the room it has, so it scrolls.</summary>
        public bool Scrolls => AutoScroll;

        /// <summary>Adds a control that sizes itself (AutoSize); the first one added is the content.</summary>
        public void Add(Control content)
        {
            content.Dock = DockStyle.None;
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            content.Location = Point.Empty;
            Controls.Add(content);
            if (_content == null) _content = content;
        }

        /// <summary>
        /// Gives the content at most <paramref name="maxHeight"/> pixels: taller content scrolls (the host is then wider
        /// by the scroll bar, so nothing is cut at the side), and the host grows back to the content once it fits.
        /// </summary>
        public void LimitHeight(int maxHeight)
        {
            if (_content == null) return;
            Size need = _content.GetPreferredSize(Size.Empty) + _content.Margin.Size;
            if (maxHeight <= 0 || need.Height <= maxHeight)
            {
                if (!AutoScroll) return;
                ScrollToTop();
                AutoScroll = false;
                AutoSize = true;
                return;
            }
            var size = new Size(need.Width + SystemInformation.VerticalScrollBarWidth, maxHeight);
            if (AutoScroll && Size == size) return;
            AutoSize = false;
            Size = size;
            AutoScroll = true;
        }

        /// <summary>Shows the top of the content (where a window's headline is) while it scrolls; the focus stays where it is.</summary>
        public void ScrollToTop()
        {
            if (AutoScroll) AutoScrollPosition = Point.Empty;
        }
    }

    /// <summary>The few user32 calls the windows make outside <see cref="NativeTheme"/>.</summary>
    internal static class NativeUi
    {
        public const uint GW_OWNER = 4;

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(int processId);
    }
}
