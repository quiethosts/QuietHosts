using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// A borderless single-line text box that paints its own hint while empty (spec 2.8.13). The edit control's cue banner
    /// is a fixed #575757, which fails contrast on the dark box; this hint uses InputHint, drawn after the edit's own paint
    /// at the formatting rectangle (where typed text starts), with the caret hidden meanwhile.
    /// </summary>
    internal sealed class HintTextBox : TextBox, IThemed
    {
        private string _hint = string.Empty;

        public HintTextBox(TypeRamp ramp)
        {
            BorderStyle = BorderStyle.None;
            Multiline = false;
            Font = ramp.Body;
            Margin = Padding.Empty;
            ApplyTheme(ThemeManager.Current);
        }

        /// <summary>The hint shown while the box is empty ("Site, company or address, for example yahoo").</summary>
        public string Hint
        {
            get => _hint;
            set
            {
                _hint = value ?? string.Empty;
                Invalidate();
            }
        }

        /// <summary>Tests: whether the hint is painted now.</summary>
        internal bool ShowsHint => TextLength == 0 && _hint.Length > 0;

        public void ApplyTheme(Theme theme)
        {
            ForeColor = Enabled ? theme.TextPrimary : theme.TextDisabled;
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            ApplyTheme(ThemeManager.Current);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // Only the paint messages: TextLength sends WM_GETTEXTLENGTH to this very window, so asking it for every
            // message would recurse without end.
            bool paint = m.Msg == NativeTheme.WM_PAINT;
            bool print = m.Msg == NativeTheme.WM_PRINTCLIENT && m.WParam != IntPtr.Zero;
            if ((!paint && !print) || !IsHandleCreated || !ShowsHint) return;
            DrawHint(print ? m.WParam : IntPtr.Zero);
        }

        private void DrawHint(IntPtr printDc)
        {
            Theme t = ThemeManager.Current;
            Rectangle r = NativeTheme.EditRect(this);
            Color color = Enabled ? t.InputHint : t.TextDisabled;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (printDc != IntPtr.Zero)
            {
                // WM_PRINTCLIENT (DrawToBitmap, a non-full-content PrintWindow): into the DC it gives.
                using (Graphics g = Graphics.FromHdc(printDc)) TextRenderer.DrawText(g, _hint, Font, r, color, flags);
                return;
            }
            // The caret is drawn by inverting pixels: painting over a visible one would leave an inverted stripe.
            bool hidden = NativeTheme.TryHideCaret(Handle);
            try
            {
                using (Graphics g = Graphics.FromHwnd(Handle)) TextRenderer.DrawText(g, _hint, Font, r, color, flags);
            }
            finally
            {
                if (hidden) NativeTheme.ShowCaretAgain(Handle);
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }
    }

    /// <summary>
    /// The search box (spec 2.8.13): a rounded input box around a <see cref="HintTextBox"/>, a clear button (only while
    /// there is text, not a tab stop) and the magnifier glyph. The text box is what screen readers reach: give it the
    /// accessible name and description. The container itself is a Pane and never takes the focus.
    /// </summary>
    internal sealed class SearchBox : Panel, IThemed
    {
        private readonly TypeRamp _ramp;
        private readonly HintTextBox _box;
        private readonly ThemedButton _clear;
        private bool _hover;

        public SearchBox(TypeRamp ramp)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            AccessibleRole = AccessibleRole.Pane;
            // The pane has no name: the text box inside is what screen readers reach (unnamed, the pane took the search text).
            AccessibleName = string.Empty;
            _box = new HintTextBox(ramp);
            _clear = new ThemedButton(ramp, Glyph.Close, Strings.ClearSearchName, Metrics.ClearButton) { TabStop = false, Visible = false };
            Controls.Add(_box);
            Controls.Add(_clear);
            Height = ramp.TextDriven(Metrics.SearchHeight, TextStyle.Body, Metrics.ButtonPadY);
            Width = ramp.Scale.Px(240);

            _box.TextChanged += (s, e) => UpdateClear();
            _box.GotFocus += (s, e) => UpdateFill();
            _box.LostFocus += (s, e) => UpdateFill();
            _box.MouseEnter += (s, e) => SetHover(true);
            _box.MouseLeave += (s, e) => SetHover(ClientRectangle.Contains(PointToClient(MousePosition)));
            _clear.Click += (s, e) => ClearAndFilter();
            ApplyTheme(ThemeManager.Current);
        }

        /// <summary>The text box (focus it, read and set its text, give it the accessible name and description).</summary>
        public HintTextBox TextBox => _box;

        /// <summary>The clear button ("Clear search").</summary>
        public ThemedButton ClearButton => _clear;

        public override string Text
        {
            get => _box.Text;
            set => _box.Text = value;
        }

        public string Hint
        {
            get => _box.Hint;
            set => _box.Hint = value;
        }

        /// <summary>Raised when the clear button emptied the box: apply the filter at once (no typing delay).</summary>
        public event EventHandler? FilterRequested;

        private void ClearAndFilter()
        {
            _box.Text = string.Empty;
            if (_box.CanFocus) _box.Focus();
            FilterRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateClear()
        {
            bool show = _box.TextLength > 0;
            if (_clear.Visible != show) _clear.Visible = show;
        }

        private void SetHover(bool hover)
        {
            if (_hover == hover) return;
            _hover = hover;
            UpdateFill();
        }

        private bool BoxFocused
        {
            get
            {
                if (_box.Focused) return true;
                return (VisualState.Forced(this) & ForcedState.Focused) != 0;
            }
        }

        private Color FillColor(Theme t)
        {
            if (!Enabled) return t.DisabledFill;
            if (BoxFocused) return t.InputFillFocused;
            if (_hover || (VisualState.Forced(this) & ForcedState.Hover) != 0) return t.InputFillHover;
            return t.InputFill;
        }

        private void UpdateFill()
        {
            Theme t = ThemeManager.Current;
            Color fill = FillColor(t);
            if (_box.BackColor.ToArgb() != fill.ToArgb() || _box.BackColor.IsSystemColor != fill.IsSystemColor) _box.BackColor = fill;
            Invalidate();
        }

        public void ApplyTheme(Theme theme)
        {
            _box.ApplyTheme(theme);
            UpdateFill();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            SetHover(true);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(ClientRectangle.Contains(PointToClient(MousePosition)));
        }

        /// <summary>A click anywhere in the box (its padding, the magnifier) focuses the text box: the whole box is the target.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && _box.CanFocus) _box.Focus();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _box.ApplyTheme(ThemeManager.Current);
            UpdateFill();
        }

        public override Size GetPreferredSize(Size proposedSize) =>
            new Size(proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Width,
                _ramp.TextDriven(Metrics.SearchHeight, TextStyle.Body, Metrics.ButtonPadY));

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            UiScale s = _ramp.Scale;
            int w = ClientSize.Width, h = ClientSize.Height;
            int clear = s.Px(Metrics.ClearButton), glyph = s.Px(16);
            int left = s.Px(11);
            int right = s.Px(4) + clear + s.Px(4) + glyph + s.Px(8);
            int boxHeight = _box.PreferredHeight;
            _box.SetBounds(left, Math.Max(0, (h - boxHeight) / 2), Math.Max(1, w - left - right), boxHeight);
            _clear.SetBounds(w - s.Px(8) - glyph - s.Px(4) - clear, (h - clear) / 2, clear, clear);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            Theme t = ThemeManager.Current;
            UiScale s = _ramp.Scale;
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            float radius = s.Px(Metrics.Radius);
            int thin = s.Stroke(1), thick = s.Stroke(2);
            bool focused = Enabled && BoxFocused;
            RoundRect.Prepare(g);
            RoundRect.Fill(g, r, radius, FillColor(t));
            if (!Enabled)
            {
                RoundRect.Stroke(g, r, radius, thin, t.DisabledBorder);
            }
            else if (t.IsHighContrast)
            {
                // High contrast: 2 epx Highlight all round while focused, 1 epx WindowText otherwise.
                if (focused) RoundRect.Stroke(g, r, radius, thick, t.InputBorderFocused);
                else RoundRect.Stroke(g, r, radius, thin, t.InputBorder);
            }
            else
            {
                RoundRect.Stroke(g, r, radius, thin, t.InputBorder);
                // The bottom edge: 1 epx at rest, 2 epx accent while the box has the focus.
                int edge = focused ? thick : thin;
                using (var brush = new SolidBrush(focused ? t.InputBorderFocused : t.InputBorderBottom))
                {
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    int inset = (int)Math.Ceiling(radius / 2f);
                    g.FillRectangle(brush, r.X + inset, r.Bottom - edge, r.Width - 2 * inset, edge);
                }
            }
            // The magnifier: decorative, TextSecondary.
            int glyph = s.Px(16);
            GlyphFont.Draw(g, Glyph.Search, new Rectangle(r.Width - s.Px(8) - glyph, (r.Height - glyph) / 2, glyph, glyph),
                Enabled ? t.TextSecondary : t.TextDisabled);
        }
    }
}
