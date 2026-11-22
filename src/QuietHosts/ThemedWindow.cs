using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuietHosts
{
#if DEBUG || QUIETHOSTS_UI_TESTS
    /// <summary>What --render-ui and the tests set on any window: show without activating, and a pretend screen.</summary>
    internal interface IPreviewWindow
    {
        bool PreviewNoActivate { get; set; }
        Rectangle? PreviewWorkingArea { get; set; }
    }
#endif

    /// <summary>
    /// The base of every redesigned window (spec 2.8.1): no automatic scaling (sizes are set in code with
    /// <see cref="UiScale.Px(int)"/>), one <see cref="TypeRamp"/>, the theme's colors, the dark title bar, live theme
    /// switching, scrolling content above an always-visible footer, keyboard scrolling (PageUp, PageDown, Ctrl+Home,
    /// Ctrl+End) and the Enter rule (K2).
    /// </summary>
    internal abstract class ThemedWindow : Form
#if DEBUG || QUIETHOSTS_UI_TESTS
        , IPreviewWindow
#endif
    {
        private const string ImmersiveColorSet = "ImmersiveColorSet";
        private ScrollHost? _host;
        private Control? _footer;
        private bool _footerShown;
        private bool? _darkFrame;
        private bool _hasDialogFrame;

        protected ThemedWindow()
        {
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Ramp = new TypeRamp();
            Font = Ramp.Body;
            BackColor = ThemeManager.Current.WindowBackground;
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        /// <summary>This window's fonts (disposed with it).</summary>
        protected internal TypeRamp Ramp { get; }

        protected new UiScale Scale => UiScale.Current;

        /// <summary>The surface the scrolling content shows (a dialog's body is a card).</summary>
        protected virtual ThemeToken ScrollBackground => ThemeToken.WindowBackground;

        /// <summary>The scrolling host, once <see cref="AddScrollingContent"/> made it.</summary>
        protected ScrollHost? ContentHost => _host;

        /// <summary>
        /// Lays the window out as a <see cref="ScrollHost"/> (add the content to it) above an optional footer that always
        /// stays visible. Call <see cref="FitToWorkingArea"/> once the window knows its screen.
        /// </summary>
        protected ScrollHost AddScrollingContent(Control? footer)
        {
            var outer = new ThemedTable { Dock = DockStyle.Fill };
            _host = new ScrollHost { Background = ScrollBackground };
            Ui.AddRow(outer, _host);
            if (footer != null)
            {
                footer.Dock = DockStyle.Fill;
                Ui.AddRow(outer, footer);
            }
            _footer = footer;
            _footerShown = footer != null;
            Controls.Add(outer);
            _host.ApplyTheme(ThemeManager.Current);
            return _host;
        }

        /// <summary>
        /// A sizable window's layout (the Fix dialog, spec 5.1): <paramref name="footer"/> docked at the bottom, always visible,
        /// and a host filling the rest of the client area. The window places and sizes the host's content itself; content
        /// taller than the host scrolls (dark scroll bar in dark mode, PageUp, PageDown, Ctrl+Home and Ctrl+End).
        /// </summary>
        protected ScrollHost AddFillingContent(Control footer)
        {
            if (footer == null) throw new ArgumentNullException(nameof(footer));
            _host = new ScrollHost { Background = ScrollBackground, AutoSize = false, Dock = DockStyle.Fill };
            footer.Dock = DockStyle.Bottom;
            Controls.Add(_host);
            Controls.Add(footer);
            // Docked last, so it fills what the footer leaves.
            _host.BringToFront();
            _footer = footer;
            _footerShown = true;
            _host.ApplyTheme(ThemeManager.Current);
            return _host;
        }

        /// <summary>Shows or hides the footer (a hidden one takes no room).</summary>
        protected void ShowFooter(bool show)
        {
            if (_footer == null) return;
            _footer.Visible = show;
            _footerShown = show;
        }

        /// <summary>
        /// Keeps the window no taller than <paramref name="area"/> (minus <paramref name="extraMargin"/> pixels): the content
        /// scrolls when it doesn't fit, and stops scrolling when it fits again. The frame, padding and footer don't depend on
        /// the window's current height, so this works even when Windows has already limited a too-tall window.
        /// </summary>
        protected void FitToWorkingArea(Rectangle area, int extraMargin = 0)
        {
            if (_host == null || area.Height <= 0) return;
            int frame = Height - ClientSize.Height;
            int footer = _footer != null && _footerShown ? _footer.Height + _footer.Margin.Vertical : 0;
            _host.LimitHeight(area.Height - Math.Max(0, extraMargin) - frame - Padding.Vertical - footer);
        }

        /// <summary>True while the content scrolls because the window would otherwise be taller than its screen.</summary>
        internal bool ContentScrolls => _host?.Scrolls ?? false;

        /// <summary>The working area of a screen (tests and --render-ui can pretend the screen is smaller).</summary>
        protected Rectangle WorkingArea(Func<Screen> screen)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewWorkingArea.HasValue) return PreviewWorkingArea.Value;
#endif
            return screen().WorkingArea;
        }

        /// <summary>The working area of the screen of this window's owner (a dialog opens there), or of this window.</summary>
        protected Rectangle OwnerWorkingArea()
        {
            return WorkingArea(() =>
            {
                IntPtr owner = IsHandleCreated ? NativeUi.GetWindow(Handle, NativeUi.GW_OWNER) : IntPtr.Zero;
                return owner != IntPtr.Zero ? Screen.FromHandle(owner) : Screen.FromControl(this);
            });
        }

        protected void EndLayout()
        {
            ResumeLayout(false);
            PerformLayout();
        }

        // ---------------------------------------------------------------- theme

        /// <summary>
        /// Repaints in <paramref name="theme"/>: the form's background, every component, the title bar and the native theme
        /// names. Fonts and layout stay, so the window keeps its size and focus.
        /// </summary>
        public virtual void ApplyTheme(Theme theme)
        {
            BackColor = theme.WindowBackground;
            if (_hasDialogFrame) Padding = new Padding(FrameWidth(theme));
            ApplyToDescendants(this, theme);
            ApplyFrame(theme);
            Invalidate(true);
        }

        private static void ApplyToDescendants(Control parent, Theme theme)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is IThemed themed) themed.ApplyTheme(theme);
                ApplyToDescendants(c, theme);
            }
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                if (IsHandleCreated) BeginInvoke(new Action(() => OnThemeChanged(sender, e)));
                return;
            }
            ApplyTheme(ThemeManager.Current);
        }

        private void ApplyFrame(Theme theme)
        {
            if (!IsHandleCreated) return;
            bool dark = theme.UseDarkFrame;
            if (_darkFrame == dark) return;
            // A change while the window shows repaints the frame now, not at the next activation.
            bool repaint = _darkFrame.HasValue && Visible;
            if (NativeTheme.ApplyFrame(Handle, dark, repaint)) _darkFrame = dark;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // A recreated handle has the default frame again.
            _darkFrame = null;
            ApplyFrame(ThemeManager.Current);
        }

        /// <summary>
        /// Dialogs only: a 2 epx WindowText frame inside the client edge in high contrast (DialogFrameWidth; 0 otherwise),
        /// in the window's padding.
        /// </summary>
        protected bool HasDialogFrame
        {
            get => _hasDialogFrame;
            set
            {
                _hasDialogFrame = value;
                Padding = value ? new Padding(FrameWidth(ThemeManager.Current)) : Padding.Empty;
                Invalidate();
            }
        }

        /// <summary>The dialog frame's width in pixels: a border, so Stroke (2 epx in high contrast, else none).</summary>
        private int FrameWidth(Theme theme) => theme.DialogFrameWidth > 0 ? Scale.Stroke(theme.DialogFrameWidth) : 0;

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme t = ThemeManager.Current;
            if (!_hasDialogFrame || t.DialogFrameWidth <= 0) return;
            int w = FrameWidth(t);
            // TextPrimary is WindowText in high contrast.
            using (var brush = new SolidBrush(t.TextPrimary))
            {
                Rectangle r = ClientRectangle;
                e.Graphics.FillRectangle(brush, r.X, r.Y, r.Width, w);
                e.Graphics.FillRectangle(brush, r.X, r.Bottom - w, r.Width, w);
                e.Graphics.FillRectangle(brush, r.X, r.Y, w, r.Height);
                e.Graphics.FillRectangle(brush, r.Right - w, r.Y, w, r.Height);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_hasDialogFrame) Invalidate();
        }

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_SYSCOLORCHANGE = 0x0015;
        private const int WM_THEMECHANGED = 0x031A;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            switch (m.Msg)
            {
                case WM_SETTINGCHANGE:
                    // Light/dark app mode switched (lParam 0 is ignored).
                    if (string.Equals(NativeTheme.SettingName(m.LParam), ImmersiveColorSet, StringComparison.Ordinal)) ThemeManager.RequestRefresh();
                    break;
                case WM_SYSCOLORCHANGE:
                    ThemeManager.RequestRefresh();
                    break;
                case WM_THEMECHANGED:
                    ThemeManager.RequestRefresh(reapplyNative: true);
                    break;
#if DEBUG || QUIETHOSTS_UI_TESTS
                case WM_GETMINMAXINFO:
                    AllowPreviewSize(m.LParam);
                    break;
#endif
            }
        }

        // ---------------------------------------------------------------- keyboard

        /// <summary>PageUp, PageDown, Ctrl+Home and Ctrl+End scroll the content while it scrolls (K6).</summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (ScrollByKey(keyData)) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>
        /// Scrolls the content by a page (its visible height minus one Body line) or to an end, from any focused control
        /// except text boxes and the list (they use those keys themselves). The focus stays where it is. Returns whether the
        /// key was used.
        /// </summary>
        internal bool ScrollByKey(Keys keyData)
        {
            ScrollHost? host = _host;
            if (host == null || !host.Scrolls) return false;
            Keys key = keyData & Keys.KeyCode, modifiers = keyData & Keys.Modifiers;
            bool page = modifiers == Keys.None && (key == Keys.PageUp || key == Keys.PageDown);
            bool end = modifiers == Keys.Control && (key == Keys.Home || key == Keys.End);
            if (!page && !end) return false;
            Control? focused = ActiveControl;
            if (focused is TextBoxBase || focused is ListView) return false;
            int step = Math.Max(1, host.ClientSize.Height - Ramp.LineHeight(TextStyle.Body));
            if (key == Keys.PageDown) host.ScrollTo(host.ScrollTop + step);
            else if (key == Keys.PageUp) host.ScrollTo(host.ScrollTop - step);
            else if (key == Keys.End) host.ScrollTo(host.ScrollRange);
            else host.ScrollTo(0);
            return true;
        }

        /// <summary>K2: no Enter on a button that swallows it (the button checks too; tests call this one).</summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (Ui.IsEnterClick(keyData) && ActiveControl is ThemedButton button && button.SwallowsEnter) return true;
            return base.ProcessDialogKey(keyData);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For --render-ui and tests: the working area to fit into instead of the real screen's.</summary>
        public Rectangle? PreviewWorkingArea { get; set; }

        /// <summary>For --render-ui and tests: show without taking focus from other windows.</summary>
        public bool PreviewNoActivate { get; set; }

        protected override bool ShowWithoutActivation => PreviewNoActivate || base.ShowWithoutActivation;

        private const int WM_GETMINMAXINFO = 0x0024;
        private const uint SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

        /// <summary>
        /// A window with a pretend screen gets the size it asks for, even beyond the real screen: Form keeps every window no
        /// larger than Windows' maximum track size (about the real screen) before Windows itself does, so the size is set
        /// again through Windows, which <see cref="AllowPreviewSize"/> lets allow it.
        /// </summary>
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            base.SetBoundsCore(x, y, width, height, specified);
            if (!PreviewWorkingArea.HasValue || !MaximumSize.IsEmpty || !IsHandleCreated || WindowState != FormWindowState.Normal) return;
            if (Width >= width && Height >= height) return;
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, Math.Max(Width, width), Math.Max(Height, height), SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        /// <summary>
        /// Lets a window grow as large as its pretend screen (<see cref="PreviewWorkingArea"/>). Windows keeps every window
        /// about as large as the real screen at most, so on a smaller real screen tests and --render-ui measured a cut window
        /// where the pretend screen had room: a CI runner's 1024x768 screen cut the 841-pixel main window with three notices
        /// to about 780 pixels. A window with a MaximumSize of its own keeps that limit.
        /// </summary>
        private void AllowPreviewSize(IntPtr minMaxInfo)
        {
            if (!PreviewWorkingArea.HasValue || minMaxInfo == IntPtr.Zero || !MaximumSize.IsEmpty) return;
            Rectangle area = PreviewWorkingArea.Value;
            // MINMAXINFO is five POINTs: ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize and ptMaxTrackSize (byte 32).
            const int maxTrackX = 32, maxTrackY = 36;
            int x = System.Runtime.InteropServices.Marshal.ReadInt32(minMaxInfo, maxTrackX);
            int y = System.Runtime.InteropServices.Marshal.ReadInt32(minMaxInfo, maxTrackY);
            System.Runtime.InteropServices.Marshal.WriteInt32(minMaxInfo, maxTrackX, Math.Max(x, area.Width));
            System.Runtime.InteropServices.Marshal.WriteInt32(minMaxInfo, maxTrackY, Math.Max(y, area.Height));
        }
#endif

        protected override void Dispose(bool disposing)
        {
            if (disposing) ThemeManager.ThemeChanged -= OnThemeChanged;
            base.Dispose(disposing);
            if (disposing) Ramp.Dispose();
        }
    }

    /// <summary>
    /// A small dialog (spec 2.8.1, 6.1): fixed size from its content, a card-colored <see cref="Body"/> (padding 24, fixed
    /// width) that scrolls when taller than the screen, and a <see cref="Footer"/> band that always shows. Dialog role, so a
    /// screen reader reads it when it opens; the first-focused control carries the message.
    /// </summary>
    internal class ThemedDialog : ThemedWindow
    {
        protected ThemedDialog(string windowTitle, int widthEpx = Metrics.DialogWidth)
        {
            Text = windowTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AccessibleRole = AccessibleRole.Dialog;
            HasDialogFrame = true;
            int width = Scale.Px(widthEpx);
            Body = new ThemedTable(1)
            {
                Fill = ThemeToken.CardBackground,
                Padding = new Padding(Scale.Px(Metrics.PagePadding)),
                MinimumSize = new Size(width, 0),
                MaximumSize = new Size(width, 0),
            };
            Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Footer = new CommandBar(Ramp) { FixedWidth = width };
            AddScrollingContent(Footer).Add(Body);
        }

        /// <summary>The body: one column, content width = the dialog width minus 48 epx (432 for the usual 480).</summary>
        protected ThemedTable Body { get; }

        /// <summary>The footer band: the "do it" button first, Cancel or Close last, in <see cref="CommandRow.SetRight"/>.</summary>
        protected CommandBar Footer { get; }

        protected override ThemeToken ScrollBackground => ThemeToken.CardBackground;

        /// <summary>
        /// Gives the first focus and lays out. <paramref name="spokenMessage"/> becomes that control's description, so a
        /// screen reader reads the message with it (a themed button keeps its shield sentence after it).
        /// </summary>
        protected void Finish(Control initialFocus, string? spokenMessage)
        {
            ActiveControl = initialFocus;
            if (!string.IsNullOrEmpty(spokenMessage))
            {
                if (initialFocus is ThemedButton button) button.ContextDescription = spokenMessage;
                else if (initialFocus is ThemedLink link) link.ContextDescription = spokenMessage;
                else initialFocus.AccessibleDescription = spokenMessage;
            }
            EndLayout();
        }

        protected override void OnLoad(EventArgs e)
        {
            // Before base.OnLoad centers the dialog on its owner, within that screen.
            FitToWorkingArea(OwnerWorkingArea());
            base.OnLoad(e);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>For tests: fit to <see cref="ThemedWindow.PreviewWorkingArea"/> as showing the dialog would.</summary>
        internal void PreviewFit() => FitToWorkingArea(OwnerWorkingArea());
#endif
    }
}
