using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace QuietHosts
{
    /// <summary>
    /// A component that follows the theme (spec 2.8). ApplyTheme sets what native parts keep themselves (colors of labels,
    /// text boxes and the list, theme names) and invalidates; it never changes fonts or sizes. Owner-drawn parts read
    /// <see cref="ThemeManager.Current"/> when they paint.
    /// </summary>
    internal interface IThemed
    {
        void ApplyTheme(Theme theme);
    }

    /// <summary>A state the gallery, the renders and tests force on one control (hover, pressed, focused).</summary>
    [Flags]
    internal enum ForcedState
    {
        None = 0,
        Hover = 1,
        Pressed = 2,
        /// <summary>Drawn with the focus ring, as if it had the keyboard focus.</summary>
        Focused = 4,
    }

#if DEBUG || QUIETHOSTS_UI_TESTS
    /// <summary>Render and test hooks for the components' states (spec 2.8.5, 2.9).</summary>
    internal static class UiOverrides
    {
        private static readonly ConditionalWeakTable<Control, StrongBox<ForcedState>> States = new ConditionalWeakTable<Control, StrongBox<ForcedState>>();

        /// <summary>Focus rings show for the focused control even without keyboard use (tests, the gallery, main-focus).</summary>
        public static bool ForceFocusCues { get; set; }

        /// <summary>Forces <paramref name="state"/> on one control (None clears it).</summary>
        public static void Force(Control control, ForcedState state)
        {
            States.Remove(control);
            if (state != ForcedState.None) States.Add(control, new StrongBox<ForcedState>(state));
            control.Invalidate();
        }

        public static ForcedState Get(Control control) => States.TryGetValue(control, out StrongBox<ForcedState>? box) ? box.Value : ForcedState.None;
    }
#endif

    /// <summary>Helpers for components that lay out their children themselves.</summary>
    internal static class LayoutUtil
    {
        private const int StateVisible = 0x00000002;
        private static readonly System.Reflection.MethodInfo? GetState = typeof(Control).GetMethod("GetState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(int) }, null);

        /// <summary>
        /// The control's own Visible flag, which WinForms' layout engines use too: <see cref="Control.Visible"/> is false
        /// for every control of a window that isn't shown yet, so a layout done before showing would drop them all.
        /// </summary>
        public static bool IsShown(Control c)
        {
            if (GetState != null)
            {
                try
                {
                    return (bool)GetState.Invoke(c, new object[] { StateVisible });
                }
                catch (System.Reflection.TargetInvocationException)
                {
                }
            }
            return c.Visible;
        }
    }

    /// <summary>Hover, pressed and focus as the components paint them, including the render and test overrides.</summary>
    internal static class VisualState
    {
        public static ForcedState Forced(Control control)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            return UiOverrides.Get(control);
#else
            return ForcedState.None;
#endif
        }

        public static bool CuesForced
        {
            get
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                return UiOverrides.ForceFocusCues;
#else
                return false;
#endif
            }
        }

        /// <summary>The focus ring shows: keyboard focus with focus cues on, or a forced focus.</summary>
        public static bool ShowsRing(Control control, bool focused, bool showFocusCues) =>
            (Forced(control) & ForcedState.Focused) != 0 || (focused && (showFocusCues || CuesForced));
    }

    /// <summary>Rounded rectangles drawn crisply: fills on the pixel grid, strokes on the half-pixel inside the bounds.</summary>
    internal static class RoundRect
    {
        public static GraphicsPath Path(RectangleF r, float radiusPx)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radiusPx * 2f, Math.Min(r.Width, r.Height));
            if (d < 1f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>Antialiased fills and borders on the pixel grid (PixelOffsetMode.Half).</summary>
        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public static void Fill(Graphics g, Rectangle bounds, float radiusPx, Color color)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || color.A == 0) return;
            using (GraphicsPath p = Path(bounds, radiusPx))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, p);
            }
        }

        /// <summary>A border <paramref name="widthPx"/> wide just inside <paramref name="bounds"/>.</summary>
        public static void Stroke(Graphics g, Rectangle bounds, float radiusPx, float widthPx, Color color)
        {
            if (widthPx <= 0 || bounds.Width <= 0 || bounds.Height <= 0 || color.A == 0) return;
            float half = widthPx / 2f;
            var r = new RectangleF(bounds.X + half, bounds.Y + half, bounds.Width - widthPx, bounds.Height - widthPx);
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Path(r, Math.Max(0f, radiusPx - half)))
            using (var pen = new Pen(color, widthPx))
            {
                g.DrawPath(pen, p);
            }
        }
    }

    /// <summary>
    /// The keyboard focus ring (spec 2.8.5): a 2 epx outer ring in FocusOuter on the control's rounded outer edge and a
    /// 1 epx inner ring in FocusInner directly inside it. Drawn last, only with focus cues on.
    /// <para>
    /// A control with a fill or border of its own (a button) draws that body inside the ring while the ring shows:
    /// <see cref="FillBand"/>, the body at <see cref="Inside"/>, then <see cref="DrawOuter"/>. Drawn on top of the body's
    /// own edge, the ring could otherwise vanish, for example in high contrast, where an accent button's 2 epx ButtonText
    /// border and the outer ring can be the same color and the inner ring then lands on a fill of its own color.
    /// </para>
    /// </summary>
    internal static class FocusRing
    {
        /// <summary>The ring's width in pixels: the outer and the inner ring.</summary>
        public static int Width(UiScale s) => s.Stroke(Metrics.FocusOuter) + s.Stroke(Metrics.FocusInner);

        /// <summary>Where a control draws its own fill and border while its ring shows: just inside the ring.</summary>
        public static Rectangle Inside(Rectangle bounds)
        {
            int w = Width(UiScale.Current);
            return Rectangle.Inflate(bounds, -w, -w);
        }

        public static void Draw(Graphics g, Rectangle bounds, Theme theme)
        {
            UiScale s = UiScale.Current;
            int outer = s.Stroke(Metrics.FocusOuter), inner = s.Stroke(Metrics.FocusInner);
            float radius = s.Px(Metrics.Radius);
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            RoundRect.Stroke(g, bounds, radius, outer, theme.FocusOuter);
            RoundRect.Stroke(g, Rectangle.Inflate(bounds, -outer, -outer), Math.Max(0f, radius - outer), inner, theme.FocusInner);
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }

        /// <summary>
        /// Before an opaque body is drawn at <see cref="Inside"/>: the whole box in FocusInner, so the band between the ring
        /// and the body is the inner ring's color, rounded corners included.
        /// </summary>
        public static void FillBand(Graphics g, Rectangle bounds, Theme theme)
        {
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            RoundRect.Fill(g, bounds, UiScale.Current.Px(Metrics.Radius), theme.FocusInner);
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }

        /// <summary>After the body: the outer ring in FocusOuter on the box's edge.</summary>
        public static void DrawOuter(Graphics g, Rectangle bounds, Theme theme)
        {
            UiScale s = UiScale.Current;
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            RoundRect.Stroke(g, bounds, s.Px(Metrics.Radius), s.Stroke(Metrics.FocusOuter), theme.FocusOuter);
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }
    }

    /// <summary>The colors a label's text can have.</summary>
    internal enum TextRole
    {
        Primary,
        Secondary,
        Disabled,
    }

    /// <summary>
    /// A text label on the type ramp (spec 2.8.2): transparent, so it shows the surface its parent paints; wraps inside its
    /// maximum width; at least one ramp line tall. It can paint a decorative glyph of its own before the text
    /// (<see cref="LeadingGlyph"/>); its accessible name stays its text.
    /// </summary>
    internal class ThemedLabel : Label, IThemed
    {
        private readonly TypeRamp _ramp;
        private TextStyle _style;
        private TextRole _role;
        private int _maxWidthEpx;
        private Glyph? _glyph;
        private ThemeToken _glyphColor = ThemeToken.TextSecondary;
        private int _glyphSize = 16, _glyphGap = 8;
        private bool _glyphHidden, _tight, _emptyName;

        public ThemedLabel(TypeRamp ramp, string text = "", TextStyle style = TextStyle.Body, TextRole role = TextRole.Primary, int maxWidthEpx = 0)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            // GDI text (TextRenderer) here and in the tests alike.
            UseCompatibleTextRendering = false;
            AutoSize = true;
            UseMnemonic = false;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            BackColor = Color.Transparent;
            _style = style;
            _role = role;
            _maxWidthEpx = maxWidthEpx;
            Text = text;
            // A label made empty gets no text change, so it is named by its empty text here (see OnTextChanged).
            if (string.IsNullOrEmpty(Text) && AccessibleName == null)
            {
                AccessibleName = string.Empty;
                _emptyName = true;
            }
            ApplyStyle();
            ApplyTheme(ThemeManager.Current);
        }

        protected TypeRamp Ramp => _ramp;

        public TextStyle Style
        {
            get => _style;
            set
            {
                if (_style == value) return;
                _style = value;
                ApplyStyle();
            }
        }

        public TextRole Role
        {
            get => _role;
            set
            {
                if (_role == value) return;
                _role = value;
                ForeColor = ThemeManager.Current[RoleToken];
            }
        }

        /// <summary>The column width it wraps in, in epx (0: no limit).</summary>
        public int MaxWidthEpx
        {
            get => _maxWidthEpx;
            set
            {
                if (_maxWidthEpx == value) return;
                _maxWidthEpx = value;
                ApplyStyle();
            }
        }

        /// <summary>A decorative glyph painted before the first line (first-run facts, "In use", the details line).</summary>
        public Glyph? LeadingGlyph
        {
            get => _glyph;
            set
            {
                _glyph = value;
                ApplyStyle();
                Invalidate();
            }
        }

        public ThemeToken LeadingGlyphColor
        {
            get => _glyphColor;
            set
            {
                _glyphColor = value;
                Invalidate();
            }
        }

        /// <summary>The glyph's size in epx: 16 (default) or 20.</summary>
        public int LeadingGlyphSize
        {
            get => _glyphSize;
            set
            {
                _glyphSize = value;
                ApplyStyle();
            }
        }

        /// <summary>The gap between glyph and text in epx: 8 (default) or 12.</summary>
        public int LeadingGlyphGap
        {
            get => _glyphGap;
            set
            {
                _glyphGap = value;
                ApplyStyle();
            }
        }

        /// <summary>The glyph's room stays but nothing is painted there (the details line without a note, so text doesn't jump).</summary>
        public bool LeadingGlyphHidden
        {
            get => _glyphHidden;
            set
            {
                _glyphHidden = value;
                Invalidate();
            }
        }

        /// <summary>
        /// The text is measured and drawn without GDI's blank margins (a label's text otherwise starts ceil(height / 6) after
        /// its left edge and leaves 1.5 times that after the text): its letters start right at the label's edge (after its
        /// padding, so a leading glyph's gap is exactly the gap), so the text lines up with a button, a glyph or an icon at
        /// the same x, and two tight labels side by side show only the space their texts hold.
        /// </summary>
        public bool Tight
        {
            get => _tight;
            set
            {
                if (_tight == value) return;
                _tight = value;
                if (AutoSize) Size = GetPreferredSize(Size.Empty);
                Parent?.PerformLayout(this, nameof(Bounds));
                Invalidate();
            }
        }

        private ThemeToken RoleToken => _role == TextRole.Secondary ? ThemeToken.TextSecondary : _role == TextRole.Disabled ? ThemeToken.TextDisabled : ThemeToken.TextPrimary;

        private void ApplyStyle()
        {
            UiScale s = UiScale.Current;
            Font = _ramp.Font(_style);
            MinimumSize = new Size(0, _ramp.LineHeight(_style));
            MaximumSize = _maxWidthEpx > 0 ? new Size(s.Px(_maxWidthEpx), 0) : Size.Empty;
            Padding = _glyph.HasValue ? new Padding(s.Px(_glyphSize) + s.Px(_glyphGap), 0, 0, 0) : Padding.Empty;
        }

        public virtual void ApplyTheme(Theme theme)
        {
            ForeColor = theme[RoleToken];
            BackColor = Color.Transparent;
            Invalidate();
        }

        /// <summary>
        /// An empty label is named by its empty text (otherwise Windows would name it after a label before it, which a screen
        /// reader then reads; the Fix dialog's empty details line read "Go to step 2"), and an empty live region says nothing.
        /// </summary>
        protected override void OnTextChanged(EventArgs e)
        {
            bool empty = string.IsNullOrEmpty(Text);
            if (empty && AccessibleName == null)
            {
                AccessibleName = string.Empty;
                _emptyName = true;
            }
            else if (!empty && _emptyName)
            {
                _emptyName = false;
                // Named by its text again (unless its owner has named it meanwhile).
                if (AccessibleName != null && AccessibleName.Length == 0) AccessibleName = null;
            }
            AutomationLiveSetting live = LiveSetting;
            bool quiet = empty && live != AutomationLiveSetting.Off;
            if (quiet) LiveSetting = AutomationLiveSetting.Off;
            try
            {
                base.OnTextChanged(e);
            }
            finally
            {
                if (quiet) LiveSetting = live;
            }
        }

        /// <summary>The formatting a tight label measures and draws with (as a Label: wrapping like an edit control).</summary>
        private TextFormatFlags TightFlags()
        {
            TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;
            switch (TextAlign)
            {
                case ContentAlignment.TopCenter:
                case ContentAlignment.MiddleCenter:
                case ContentAlignment.BottomCenter:
                    flags |= TextFormatFlags.HorizontalCenter;
                    break;
                case ContentAlignment.TopRight:
                case ContentAlignment.MiddleRight:
                case ContentAlignment.BottomRight:
                    flags |= TextFormatFlags.Right;
                    break;
            }
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            return flags;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (!_tight) return base.GetPreferredSize(proposedSize);
            // As a Label: a proposed width of 0 or 1 means no limit; the maximum width limits it.
            int maxWidth = proposedSize.Width <= 1 ? int.MaxValue : proposedSize.Width;
            if (MaximumSize.Width > 0) maxWidth = Math.Min(maxWidth, MaximumSize.Width);
            int room = maxWidth == int.MaxValue ? int.MaxValue : Math.Max(1, maxWidth - Padding.Horizontal);
            Size text = string.IsNullOrEmpty(Text) ? new Size(0, Font.Height) : GdiText.Measure(Text, Font, TightFlags(), new Size(room, int.MaxValue));
            var size = new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical);
            if (MaximumSize.Width > 0) size.Width = Math.Min(size.Width, MaximumSize.Width);
            if (MaximumSize.Height > 0) size.Height = Math.Min(size.Height, MaximumSize.Height);
            return new Size(Math.Max(size.Width, MinimumSize.Width), Math.Max(size.Height, MinimumSize.Height));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_tight)
            {
                // The parent's surface is already painted (OnPaintBackground); the text goes right at the padding's edge.
                var face = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical));
                TextFormatFlags flags = TightFlags() | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
                if (UseMnemonic && !ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
                TextRenderer.DrawText(e.Graphics, Text, Font, face, Enabled ? ForeColor : ThemeManager.Current.TextDisabled, flags);
            }
            else
            {
                base.OnPaint(e);
            }
            if (!_glyph.HasValue || _glyphHidden) return;
            int px = UiScale.Current.Px(_glyphSize);
            // Centered on the first line of text.
            int line = Math.Max(Font.Height, 1);
            var box = new Rectangle(0, Math.Max(0, (line - px) / 2), px, px);
            GlyphFont.Draw(e.Graphics, _glyph.Value, box, ThemeManager.Current[_glyphColor]);
        }
    }

    /// <summary>A transparent table for laying out components (rows added with <see cref="Ui.AddRow"/>), or one with a fill.</summary>
    internal class ThemedTable : TableLayoutPanel, IThemed
    {
        private ThemeToken? _fill;

        public ThemedTable(int columns = 1)
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            ColumnCount = Math.Max(1, columns);
            RowCount = 0;
            GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            BackColor = Color.Transparent;
            // A layout panel has no name: unnamed, Windows would name it after a label before it, and a screen reader would
            // read that label's text as the context of whatever gets the focus inside.
            AccessibleName = string.Empty;
        }

        /// <summary>An opaque surface instead of transparency (a dialog's body: CardBackground).</summary>
        public ThemeToken? Fill
        {
            get => _fill;
            set
            {
                _fill = value;
                ApplyTheme(ThemeManager.Current);
            }
        }

        public void ApplyTheme(Theme theme)
        {
            BackColor = _fill.HasValue ? theme[_fill.Value] : Color.Transparent;
            Invalidate();
        }
    }

    /// <summary>The three button looks (spec 2.8.3).</summary>
    internal enum ButtonStyle
    {
        /// <summary>The one recommended action: accent fill.</summary>
        Accent,
        Standard,
        /// <summary>Glyph-only, no fill at rest (notice close, clear search).</summary>
        Subtle,
    }

    /// <summary>
    /// An owner-drawn button (spec 2.8.3) that stays a Button for UI Automation (Button with the Invoke pattern). It can
    /// carry the UAC shield (<see cref="Shield"/>: the click starts an elevated change) or the external-link glyph, and can
    /// swallow Enter (<see cref="SwallowsEnter"/>, K2).
    /// </summary>
    internal class ThemedButton : Button, IThemed
    {
        private readonly TypeRamp _ramp;
        private ButtonStyle _style;
        private bool _shield, _external, _swallowsEnter;
        private Glyph? _glyphOnly;
        private int _glyphBoxEpx = Metrics.NoticeClose;
        private string? _context;
        private bool _hover, _mouseDown, _mouseInside, _spaceDown;

        public ThemedButton(TypeRamp ramp, string text, ButtonStyle style = ButtonStyle.Standard)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            // ButtonBase sets Opaque, which skips the background layer: OnPaintBackground must paint the parent behind the
            // corners (and behind a Subtle button at rest), or the double buffer keeps whatever was painted before.
            SetStyle(ControlStyles.Opaque, false);
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            UseMnemonic = true;
            UseCompatibleTextRendering = false;
            TabStop = true;
            Font = ramp.Body;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            _style = style;
            Text = text;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            UpdateDescription();
        }

        /// <summary>A glyph-only Subtle button: <paramref name="boxEpx"/> square (32 for a notice's close, 24 for clear search).</summary>
        public ThemedButton(TypeRamp ramp, Glyph glyph, string accessibleName, int boxEpx = Metrics.NoticeClose)
            : this(ramp, string.Empty, ButtonStyle.Subtle)
        {
            _glyphOnly = glyph;
            _glyphBoxEpx = Math.Max(Metrics.TargetMin, boxEpx);
            AccessibleName = accessibleName;
            Relayout();
        }

        public ButtonStyle Style
        {
            get => _style;
            set
            {
                if (_style == value) return;
                _style = value;
                Invalidate();
            }
        }

        /// <summary>The UAC shield before the text: this click immediately starts an elevated change. Adds the shield sentence to the description.</summary>
        public bool Shield
        {
            get => _shield;
            set
            {
                if (_shield == value) return;
                _shield = value;
                UpdateDescription();
                Relayout();
            }
        }

        /// <summary>The external-link glyph after the text: the click opens the browser. Adds "Opens in your browser."</summary>
        public bool External
        {
            get => _external;
            set
            {
                if (_external == value) return;
                _external = value;
                UpdateDescription();
                Relayout();
            }
        }

        /// <summary>A Subtle glyph-only button's glyph (Text is empty; AccessibleName names it).</summary>
        public Glyph? GlyphOnly
        {
            get => _glyphOnly;
            set
            {
                _glyphOnly = value;
                Relayout();
            }
        }

        /// <summary>Enter and Shift+Enter do nothing on this button (K2); Space, the access key and a click still work.</summary>
        public bool SwallowsEnter
        {
            get => _swallowsEnter;
            set => _swallowsEnter = value;
        }

        /// <summary>The description before the shield and external sentences (the card's text, the status, ...).</summary>
        public string? ContextDescription
        {
            get => _context;
            set
            {
                _context = value;
                UpdateDescription();
            }
        }

        private void UpdateDescription()
        {
            string d = _context ?? string.Empty;
            if (_shield) d += " " + Strings.ShieldDescription;
            if (_external) d += " " + Strings.OpensInBrowser;
            d = d.Trim();
            AccessibleDescription = d.Length > 0 ? d : null;
        }

        private void Relayout()
        {
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Parent?.PerformLayout(this, nameof(Bounds));
            Invalidate();
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        // ---------------------------------------------------------------- size

        private string? _measuredText;
        private int _measuredWidth;

        /// <summary>
        /// The label's width as <see cref="DrawContent"/> draws it (NoPadding), measured on a device context: measured without
        /// one, GDI's blank margins would count and put the label off centre (2.8.3).
        /// </summary>
        private int TextWidth()
        {
            if (string.IsNullOrEmpty(Text)) return 0;
            if (!string.Equals(_measuredText, Text, StringComparison.Ordinal))
            {
                _measuredWidth = GdiText.Measure(Text, _ramp.Body, TextFormatFlags.SingleLine).Width;
                _measuredText = Text;
            }
            return _measuredWidth;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            UiScale s = UiScale.Current;
            if (_glyphOnly.HasValue)
            {
                int box = Math.Max(s.Px(_glyphBoxEpx), s.Px(Metrics.TargetMin));
                return new Size(box, box);
            }
            int w = s.Px(Metrics.ButtonPadX) + (_shield ? s.Px(Metrics.ShieldIcon) + s.Px(Metrics.ShieldGap) : 0) + TextWidth() +
                (_external ? s.Px(4) + s.Px(16) : 0) + s.Px(Metrics.ButtonPadX);
            int h = _ramp.TextDriven(Metrics.ButtonHeight, TextStyle.Body, Metrics.ButtonPadY);
            return new Size(Math.Max(s.Px(Metrics.ButtonMinWidth), w), h);
        }

        // ---------------------------------------------------------------- states

        private enum Look
        {
            Rest,
            Hover,
            Pressed,
            Disabled,
        }

        private Look CurrentLook()
        {
            if (!Enabled) return Look.Disabled;
            ForcedState forced = VisualState.Forced(this);
            if ((forced & ForcedState.Pressed) != 0 || (_mouseDown && _mouseInside) || _spaceDown) return Look.Pressed;
            if ((forced & ForcedState.Hover) != 0 || _hover) return Look.Hover;
            return Look.Rest;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            _mouseInside = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _mouseInside = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = true;
            _mouseInside = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_mouseDown) return;
            bool inside = ClientRectangle.Contains(e.Location);
            if (inside == _mouseInside) return;
            _mouseInside = inside;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !_mouseDown) return;
            _mouseDown = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Space || _spaceDown) return;
            _spaceDown = true;
            Invalidate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode != Keys.Space || !_spaceDown) return;
            _spaceDown = false;
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
            _spaceDown = false;
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _mouseDown = false;
            _spaceDown = false;
            if (!Enabled) _hover = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        /// <summary>K2: Enter (also Shift+Enter) is swallowed here, before the form's default-button handling sees it.</summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (_swallowsEnter && Ui.IsEnterClick(keyData)) return true;
            return base.ProcessDialogKey(keyData);
        }

        // ---------------------------------------------------------------- paint

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = ThemeManager.Current;
            Graphics g = e.Graphics;
            UiScale s = UiScale.Current;
            Look look = CurrentLook();
            Rectangle r = ClientRectangle;
            float radius = s.Px(Metrics.Radius);
            int border = s.Stroke(Metrics.Border);
            bool ring = VisualState.ShowsRing(this, Focused, ShowFocusCues);
            // A subtle button has no fill or border at rest: the plain ring goes over it. Every other look has a body of
            // its own, which moves inside the ring while the ring shows, so the ring stays visible in every theme (2.8.5).
            bool opaque = _style != ButtonStyle.Subtle;
            Rectangle body = ring && opaque ? FocusRing.Inside(r) : r;
            if (ring && opaque) FocusRing.FillBand(g, r, t);
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);

            Color text;
            if (look == Look.Disabled && _style != ButtonStyle.Subtle)
            {
                RoundRect.Fill(g, body, radius, t.DisabledFill);
                RoundRect.Stroke(g, body, radius, border, t.DisabledBorder);
                text = t.TextDisabled;
            }
            else if (_style == ButtonStyle.Accent)
            {
                Color fill = look == Look.Pressed ? t.AccentFillPressed : look == Look.Hover ? t.AccentFillHover : t.AccentFill;
                RoundRect.Fill(g, body, radius, fill);
                if (t.AccentBorderWidth > 0)
                    RoundRect.Stroke(g, body, radius, s.Stroke(t.AccentBorderWidth), look == Look.Rest ? t.ButtonBorder : t.ButtonBorderHover);
                text = look == Look.Rest ? t.TextOnAccent : t.TextOnAccentHover;
            }
            else if (_style == ButtonStyle.Standard)
            {
                Color fill = look == Look.Pressed ? t.ButtonFillPressed : look == Look.Hover ? t.ButtonFillHover : t.ButtonFill;
                Color edge = look == Look.Rest ? t.ButtonBorder : t.ButtonBorderHover;
                // The darker bottom edge at rest and hover; pressed (and high contrast's hover) has one color all round.
                Color bottom = look == Look.Pressed || (look == Look.Hover && t.IsHighContrast) ? edge : t.ButtonBorderBottom;
                RoundRect.Fill(g, body, radius, fill);
                RoundRect.Stroke(g, body, radius, border, edge);
                if (bottom.ToArgb() != edge.ToArgb())
                {
                    using (var pen = new Pen(bottom, border))
                    {
                        float y = body.Bottom - border / 2f;
                        g.DrawLine(pen, body.X + radius, y, body.Right - radius, y);
                    }
                }
                text = look == Look.Pressed ? t.ButtonTextPressed : look == Look.Hover ? t.ButtonTextHover : t.ButtonText;
            }
            else
            {
                if (look == Look.Hover) RoundRect.Fill(g, r, radius, t.SubtleHover);
                else if (look == Look.Pressed) RoundRect.Fill(g, r, radius, t.SubtlePressed);
                text = look == Look.Disabled ? t.TextDisabled : look == Look.Hover ? t.SubtleGlyphHover : look == Look.Pressed ? t.SubtleGlyphPressed : t.TextSecondary;
            }
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;

            // The content keeps its place whether or not the ring shows.
            if (_glyphOnly.HasValue)
            {
                int px = s.Px(Metrics.NoticeCloseGlyph);
                GlyphFont.Draw(g, _glyphOnly.Value, new Rectangle((r.Width - px) / 2, (r.Height - px) / 2, px, px), text);
            }
            else
            {
                DrawContent(g, r, text, look == Look.Disabled);
            }

            if (ring)
            {
                if (opaque) FocusRing.DrawOuter(g, r, t);
                else FocusRing.Draw(g, r, t);
            }
        }

        private void DrawContent(Graphics g, Rectangle r, Color text, bool disabled)
        {
            UiScale s = UiScale.Current;
            int shield = _shield ? s.Px(Metrics.ShieldIcon) + s.Px(Metrics.ShieldGap) : 0;
            int external = _external ? s.Px(4) + s.Px(16) : 0;
            int textWidth = TextWidth();
            int padX = s.Px(Metrics.ButtonPadX);
            int available = Math.Max(0, r.Width - 2 * padX - shield - external);
            int shown = Math.Min(textWidth, available);
            int x = r.X + Math.Max(padX, (r.Width - (shield + shown + external)) / 2);
            if (_shield)
            {
                int px = s.Px(Metrics.ShieldIcon);
                UacShield.Draw(g, new Rectangle(x, r.Y + (r.Height - px) / 2, px, px), disabled);
                x += shield;
            }
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(g, Text, _ramp.Body, new Rectangle(x, r.Y, shown, r.Height), text, flags);
            x += shown;
            if (_external)
            {
                int px = s.Px(16);
                GlyphFont.Draw(g, Glyph.OpenInNew, new Rectangle(x + s.Px(4), r.Y + (r.Height - px) / 2, px, px), text);
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: the button would draw its focus ring now.</summary>
        internal bool PreviewShowsFocusRing => VisualState.ShowsRing(this, Focused, ShowFocusCues);

        /// <summary>Tests: what the system's own focus-cue setting says for this button.</summary>
        internal bool PreviewSystemFocusCues => ShowFocusCues;
#endif
    }

    /// <summary>
    /// A link (spec 2.8.4). It is a <see cref="LinkLabel"/>, which UI Automation shows as a Hyperlink with the Invoke pattern
    /// (a Button with the Link role would be a pattern-less "pane"). The whole text is one link; a click anywhere in its
    /// box, Enter, Space or its access key raise <see cref="Invoked"/>. External links carry the external-link glyph.
    /// </summary>
    internal class ThemedLink : LinkLabel, IThemed
    {
        private readonly TypeRamp _ramp;
        private bool _external, _hover, _downInBox;
        private string? _context;

        public ThemedLink(TypeRamp ramp, string text)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            UseCompatibleTextRendering = false;
            AutoSize = true;
            UseMnemonic = true;
            TabStop = true;
            Font = ramp.Body;
            Margin = Padding.Empty;
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Text = text;
            UpdateBox();
            UpdateDescription();
            ApplyTheme(ThemeManager.Current);
        }

        public event EventHandler? Invoked;

        /// <summary>Opens the browser: the glyph after the text and "Opens in your browser." in the description.</summary>
        public bool External
        {
            get => _external;
            set
            {
                if (_external == value) return;
                _external = value;
                UpdateBox();
                UpdateDescription();
                Invalidate();
            }
        }

        /// <summary>The description before the external sentence.</summary>
        public string? ContextDescription
        {
            get => _context;
            set
            {
                _context = value;
                UpdateDescription();
            }
        }

        /// <summary>Raises <see cref="Invoked"/> as a click would.</summary>
        public void PerformClick()
        {
            if (!Enabled) return;
            OnLinkClicked(new LinkLabelLinkClickedEventArgs(Links.Count > 0 ? Links[0] : new Link()));
        }

        private void UpdateBox()
        {
            UiScale s = UiScale.Current;
            int side = s.Px(4);
            int vertical = Math.Max(0, (s.Px(Metrics.TargetMin) - _ramp.LineHeight(TextStyle.Body)) / 2);
            Padding = new Padding(side, vertical, side + (_external ? s.Px(4) + s.Px(16) : 0), vertical);
            MinimumSize = new Size(0, s.Px(Metrics.TargetMin));
        }

        private void UpdateDescription()
        {
            string d = (_context ?? string.Empty) + (_external ? " " + Strings.OpensInBrowser : string.Empty);
            d = d.Trim();
            AccessibleDescription = d.Length > 0 ? d : null;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Strings.WithoutAccessKey(Text);
        }

        public void ApplyTheme(Theme theme)
        {
            LinkColor = _hover || (VisualState.Forced(this) & ForcedState.Hover) != 0 ? theme.LinkHover : theme.Link;
            ActiveLinkColor = theme.Link;
            VisitedLinkColor = theme.Link;
            DisabledLinkColor = theme.TextDisabled;
            BackColor = Color.Transparent;
            UpdateUnderline(theme);
            Invalidate();
        }

        private void UpdateUnderline(Theme theme)
        {
            bool hover = _hover || (VisualState.Forced(this) & (ForcedState.Hover | ForcedState.Pressed)) != 0;
            LinkBehavior = !Enabled ? LinkBehavior.NeverUnderline
                : theme.LinksUnderlinedAtRest || hover ? LinkBehavior.AlwaysUnderline
                : LinkBehavior.HoverUnderline;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            if (!Enabled) _hover = false;
            ApplyTheme(ThemeManager.Current);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            ApplyTheme(ThemeManager.Current);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _downInBox = false;
            ApplyTheme(ThemeManager.Current);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _downInBox = e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fromBox = _downInBox;
            _downInBox = false;
            base.OnMouseUp(e);
            // The text itself raises LinkClicked; this covers the rest of the box (the padding around the text).
            if (fromBox && e.Button == MouseButtons.Left && Enabled && ClientRectangle.Contains(e.Location) && PointInLink(e.X, e.Y) == null)
                OnLinkClicked(new LinkLabelLinkClickedEventArgs(Links.Count > 0 ? Links[0] : new Link()));
        }

        protected override void OnLinkClicked(LinkLabelLinkClickedEventArgs e)
        {
            base.OnLinkClicked(e);
            Invoked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Enter goes to the link itself (it activates it), never to the form's default button.</summary>
        protected override bool IsInputKey(Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Enter && (keyData & (Keys.Alt | Keys.Control)) == Keys.None) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space && !e.Alt && !e.Control && Enabled)
            {
                e.Handled = true;
                OnLinkClicked(new LinkLabelLinkClickedEventArgs(Links.Count > 0 ? Links[0] : new Link()));
            }
        }

        /// <summary>The access key focuses the link and activates it (a plain LinkLabel's would only move the focus).</summary>
        protected override bool ProcessMnemonic(char charCode)
        {
            if (!UseMnemonic || !CanSelect || !IsMnemonic(charCode, Text)) return false;
            Focus();
            Invoked?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Never LinkLabel's dotted rectangle: the control draws the focus ring around its box instead.</summary>
        protected override bool ShowFocusCues => false;

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

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = ThemeManager.Current;
            UiScale s = UiScale.Current;
            if (Enabled)
            {
                base.OnPaint(e);
            }
            else
            {
                // A disabled LinkLabel paints its text itself, embossed in colors taken from the background
                // (ControlPaint.DrawStringDisabled); DisabledLinkColor only applies to a disabled link inside an enabled
                // control. So the disabled text is drawn here, in TextDisabled and without an underline (2.8.4).
                InvokePaintBackground(this, e);
                TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak |
                    TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
                if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
                else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
                var text = new Rectangle(Padding.Left, Padding.Top, Math.Max(0, ClientSize.Width - Padding.Horizontal), Math.Max(0, ClientSize.Height - Padding.Vertical));
                TextRenderer.DrawText(e.Graphics, Text, Font, text, t.TextDisabled, flags);
            }
            if (_external)
            {
                int px = s.Px(16);
                Color color = !Enabled ? t.TextDisabled : LinkColor;
                int line = Math.Max(Font.Height, 1);
                int y = Padding.Top + Math.Max(0, (line - px) / 2);
                GlyphFont.Draw(e.Graphics, Glyph.OpenInNew, new Rectangle(ClientSize.Width - Padding.Right + s.Px(4), y, px, px), color);
            }
            if (VisualState.ShowsRing(this, Focused, base.ShowFocusCues)) FocusRing.Draw(e.Graphics, ClientRectangle, t);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: whether Enter is the link's own key (so the form's default button never sees it).</summary>
        internal bool PreviewTakesEnter => IsInputKey(Keys.Enter);
#endif
    }

    /// <summary>
    /// An owner-drawn check box (spec 2.8.12): the native glyph can't follow dark mode or the HC simulation. The box starts
    /// 4 epx in from the left edge, as the text ends 4 epx before the right one, so the focus ring around box and text never
    /// covers the box's edge.
    /// </summary>
    internal class ThemedCheckBox : CheckBox, IThemed
    {
        /// <summary>Epx left of the box and right of the text: room for the focus ring (3 px at 96 DPI, never more than this).</summary>
        internal const int RingRoom = 4;
        private readonly TypeRamp _ramp;
        private bool _hover, _mouseDown, _spaceDown;

        public ThemedCheckBox(TypeRamp ramp, string text)
        {
            _ramp = ramp ?? throw new ArgumentNullException(nameof(ramp));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            // As in ThemedButton: without this ButtonBase's Opaque style skips the background, so the parent never shows.
            SetStyle(ControlStyles.Opaque, false);
            FlatStyle = FlatStyle.Standard;
            UseVisualStyleBackColor = false;
            BackColor = Color.Transparent;
            UseMnemonic = true;
            UseCompatibleTextRendering = false;
            TabStop = true;
            Font = ramp.Body;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Text = text;
            AutoSize = true;
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        /// <summary>The text's width as it is drawn (NoPadding), measured on a device context (2.8.12).</summary>
        private int TextWidth() => string.IsNullOrEmpty(Text) ? 0 : GdiText.Measure(Text, _ramp.Body, TextFormatFlags.SingleLine).Width;

        public override Size GetPreferredSize(Size proposedSize)
        {
            UiScale s = UiScale.Current;
            return new Size(s.Px(RingRoom) + s.Px(Metrics.CheckBoxBox) + s.Px(Metrics.CheckBoxGap) + TextWidth() + s.Px(RingRoom),
                _ramp.TextDriven(Metrics.ButtonHeight, TextStyle.Body, Metrics.ButtonPadY));
        }

        /// <summary>The box (20 epx, vertically centered) in client coordinates.</summary>
        internal Rectangle BoxBounds
        {
            get
            {
                UiScale s = UiScale.Current;
                int boxPx = s.Px(Metrics.CheckBoxBox);
                return new Rectangle(s.Px(RingRoom), (ClientSize.Height - boxPx) / 2, boxPx, boxPx);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) _mouseDown = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _mouseDown = false;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space) _spaceDown = true;
            Invalidate();
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.Space) _spaceDown = false;
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
            _spaceDown = false;
            Invalidate();
        }

        protected override void OnChangeUICues(UICuesEventArgs e)
        {
            base.OnChangeUICues(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _mouseDown = _spaceDown = false;
            if (!Enabled) _hover = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = ThemeManager.Current;
            UiScale s = UiScale.Current;
            Graphics g = e.Graphics;
            ForcedState forced = VisualState.Forced(this);
            bool hot = Enabled && (_hover || _mouseDown || _spaceDown || (forced & (ForcedState.Hover | ForcedState.Pressed)) != 0);
            Rectangle box = BoxBounds;
            int boxPx = box.Width;
            float radius = s.Px(Metrics.Radius);
            int border = s.Stroke(Metrics.Border);
            SmoothingMode smoothing = g.SmoothingMode;
            PixelOffsetMode offset = g.PixelOffsetMode;
            RoundRect.Prepare(g);
            Color glyph;
            if (!Enabled)
            {
                if (Checked)
                {
                    RoundRect.Fill(g, box, radius, t.CheckBoxDisabledCheckedFill);
                }
                else
                {
                    RoundRect.Fill(g, box, radius, t.CheckBoxDisabledFill);
                    RoundRect.Stroke(g, box, radius, border, t.CheckBoxDisabledBorder);
                }
                glyph = t.CheckBoxDisabledGlyph;
            }
            else if (Checked)
            {
                RoundRect.Fill(g, box, radius, hot ? t.CheckBoxCheckedFillHover : t.CheckBoxCheckedFill);
                glyph = t.CheckBoxGlyph;
            }
            else
            {
                RoundRect.Fill(g, box, radius, hot ? t.CheckBoxFillHover : t.CheckBoxFill);
                RoundRect.Stroke(g, box, radius, border, hot ? t.CheckBoxBorderHover : t.CheckBoxBorder);
                glyph = t.CheckBoxGlyph;
            }
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
            if (Checked)
            {
                int px = s.Px(16);
                GlyphFont.Draw(g, Glyph.CheckMark, new Rectangle(box.X + (boxPx - px) / 2, box.Y + (boxPx - px) / 2, px, px), glyph);
            }

            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
            if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            int textX = box.Right + s.Px(Metrics.CheckBoxGap);
            TextRenderer.DrawText(g, Text, _ramp.Body, new Rectangle(textX, 0, Math.Max(0, ClientSize.Width - textX - s.Px(RingRoom)), ClientSize.Height),
                Enabled ? t.TextPrimary : t.TextDisabled, flags);

            if (VisualState.ShowsRing(this, Focused, ShowFocusCues)) FocusRing.Draw(g, ClientRectangle, t);
        }
    }

    /// <summary>
    /// The app icon at 48 epx (spec 2.8.17), sharp at any DPI: the shell scales it down from the exe's 256 px frame;
    /// otherwise from the embedded icon. Role Graphic, name "QuietHosts". The same image in every theme.
    /// </summary>
    internal sealed class AppIconImage : Control, IThemed
    {
        private Bitmap? _image;

        public AppIconImage(string? exePath)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = Strings.AppName;
            int px = UiScale.Current.Px(Metrics.AppIcon);
            Size = new Size(px, px);
            _image = Load(exePath, px);
        }

        /// <summary>Tests: the image was found.</summary>
        internal bool HasImage => _image != null;

        private static Bitmap? Load(string? exePath, int px)
        {
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                IntPtr h = NativeTheme.ExtractIcon(exePath!, 0, px);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon icon = Icon.FromHandle(h)) return icon.ToBitmap();
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
                    {
                    }
                    finally
                    {
                        NativeTheme.Destroy(h);
                    }
                }
            }
            try
            {
                using (Stream? s = typeof(AppIconImage).Assembly.GetManifestResourceStream(AppIcon.ResourceName))
                {
                    if (s == null) return null;
                    using (var icon = new Icon(s, px, px)) return icon.ToBitmap();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_image == null) return;
            int px = UiScale.Current.Px(Metrics.AppIcon);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(_image, new Rectangle((ClientSize.Width - px) / 2, (ClientSize.Height - px) / 2, px, px));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _image?.Dispose();
                _image = null;
            }
            base.Dispose(disposing);
        }
    }
}
