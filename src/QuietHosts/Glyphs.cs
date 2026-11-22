using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>The icon-font glyphs the window uses (code points of Segoe Fluent Icons and Segoe MDL2 Assets).</summary>
    internal enum Glyph
    {
        CheckMark = 0xE73E,
        Close = 0xE711,
        Error = 0xEA39,
        Warning = 0xE7BA,
        Info = 0xE946,
        Success = 0xE930,
        Search = 0xE721,
        OpenInNew = 0xE8A7,
        Globe = 0xE774,
        Lock = 0xE72E,
        Undo = 0xE7A7,
        Sync = 0xE895,
    }

    /// <summary>
    /// Draws glyphs from "Segoe Fluent Icons" (Windows 11), else "Segoe MDL2 Assets" (Windows 10), per glyph from the other
    /// font if the first lacks it, else as vector shapes for the six glyphs that carry meaning (spec 2.8.9). Decorative
    /// glyphs are simply not drawn then; their meaning is always also in text. Glyphs scale with the DPI, never with the
    /// text size, at 16, 20, 24 or 32 epx.
    /// </summary>
    internal static class GlyphFont
    {
        public const string FluentFamily = "Segoe Fluent Icons";
        public const string Mdl2Family = "Segoe MDL2 Assets";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, bool> Installed = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<(string, Glyph), bool> Coverage = new Dictionary<(string, Glyph), bool>();
        private static readonly Dictionary<(string, int), Font> Fonts = new Dictionary<(string, int), Font>();
        private static string? _family;
        private static bool _familyKnown;

        /// <summary>The icon font the window uses: Segoe Fluent Icons, else Segoe MDL2 Assets, else null. Chosen once per process.</summary>
        public static string? Family
        {
            get
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                if (PreviewNoIconFont) return null;
#endif
                lock (Gate)
                {
                    if (!_familyKnown)
                    {
                        _family = IsInstalled(FluentFamily) ? FluentFamily : IsInstalled(Mdl2Family) ? Mdl2Family : null;
                        _familyKnown = true;
                    }
                    return _family;
                }
            }
        }

        /// <summary>The glyph is in <see cref="Family"/> (GetGlyphIndicesW, cached).</summary>
        public static bool Has(Glyph glyph)
        {
            string? family = Family;
            return family != null && Has(family, glyph);
        }

        /// <summary>The six glyphs that carry meaning on their own and get a vector fallback.</summary>
        public static bool CarriesMeaning(Glyph glyph) =>
            glyph == Glyph.CheckMark || glyph == Glyph.Close || glyph == Glyph.Warning || glyph == Glyph.Error || glyph == Glyph.Info || glyph == Glyph.Success;

        /// <summary>
        /// Draws <paramref name="glyph"/> centered in <paramref name="box"/> (a Px(16|20|24|32) square) in
        /// <paramref name="color"/>: from the icon font, or the other icon font, or as a vector shape.
        /// </summary>
        public static void Draw(Graphics g, Glyph glyph, Rectangle box, Color color)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            string? family = FamilyFor(glyph);
            if (family != null)
            {
                Font font = FontFor(family, Math.Min(box.Width, box.Height));
                TextRenderer.DrawText(g, ((char)(int)glyph).ToString(), font, box, color,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform);
                return;
            }
            if (CarriesMeaning(glyph)) DrawVector(g, glyph, box, color);
        }

        /// <summary>The family that draws <paramref name="glyph"/>, or null (vector fallback or nothing).</summary>
        internal static string? FamilyFor(Glyph glyph)
        {
            string? primary = Family;
            if (primary == null) return null;
            if (Has(primary, glyph)) return primary;
            string other = primary == FluentFamily ? Mdl2Family : FluentFamily;
            return IsInstalled(other) && Has(other, glyph) ? other : null;
        }

        private static bool IsInstalled(string family)
        {
            lock (Gate)
            {
                if (Installed.TryGetValue(family, out bool known)) return known;
                bool ok = false;
                try
                {
                    using (var f = new Font(family, 16f, FontStyle.Regular, GraphicsUnit.Pixel))
                        ok = string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase);
                }
                catch (ArgumentException)
                {
                }
                Installed[family] = ok;
                return ok;
            }
        }

        private static bool Has(string family, Glyph glyph)
        {
            lock (Gate)
            {
                if (Coverage.TryGetValue((family, glyph), out bool known)) return known;
                bool has = NativeTheme.FontHasGlyph(FontFor(family, 16), (char)(int)glyph);
                Coverage[(family, glyph)] = has;
                return has;
            }
        }

        private static Font FontFor(string family, int px)
        {
            lock (Gate)
            {
                if (Fonts.TryGetValue((family, px), out Font? f)) return f;
                f = new Font(family, px, FontStyle.Regular, GraphicsUnit.Pixel);
                Fonts[(family, px)] = f;
                return f;
            }
        }

        /// <summary>The shapes for the six meaning glyphs when no icon font has them.</summary>
        private static void DrawVector(Graphics g, Glyph glyph, Rectangle box, Color color)
        {
            float s = Math.Min(box.Width, box.Height);
            float x = box.X + (box.Width - s) / 2f, y = box.Y + (box.Height - s) / 2f;
            PointF P(float fx, float fy) => new PointF(x + s * fx, y + s * fy);
            float width = Math.Max(1f, 1.5f * UiScale.Current.Dpi / 96f);
            SmoothingMode smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var brush = new SolidBrush(color))
                {
                    float r = Math.Max(1f, s * 0.07f);
                    switch (glyph)
                    {
                        case Glyph.CheckMark:
                            g.DrawLines(pen, new[] { P(0.18f, 0.52f), P(0.40f, 0.74f), P(0.84f, 0.28f) });
                            break;
                        case Glyph.Close:
                            g.DrawLine(pen, P(0.25f, 0.25f), P(0.75f, 0.75f));
                            g.DrawLine(pen, P(0.75f, 0.25f), P(0.25f, 0.75f));
                            break;
                        case Glyph.Warning:
                            g.DrawPolygon(pen, new[] { P(0.5f, 0.1f), P(0.93f, 0.88f), P(0.07f, 0.88f) });
                            g.DrawLine(pen, P(0.5f, 0.38f), P(0.5f, 0.62f));
                            g.FillEllipse(brush, x + s * 0.5f - r, y + s * 0.75f - r, 2 * r, 2 * r);
                            break;
                        case Glyph.Error:
                            g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                            g.DrawLine(pen, P(0.36f, 0.36f), P(0.64f, 0.64f));
                            g.DrawLine(pen, P(0.64f, 0.36f), P(0.36f, 0.64f));
                            break;
                        case Glyph.Info:
                            g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                            g.DrawLine(pen, P(0.5f, 0.46f), P(0.5f, 0.72f));
                            g.FillEllipse(brush, x + s * 0.5f - r, y + s * 0.31f - r, 2 * r, 2 * r);
                            break;
                        case Glyph.Success:
                            g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                            g.DrawLines(pen, new[] { P(0.30f, 0.52f), P(0.45f, 0.66f), P(0.70f, 0.38f) });
                            break;
                    }
                }
            }
            finally
            {
                g.SmoothingMode = smoothing;
            }
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: act as if no icon font were installed (the vector fallback).</summary>
        internal static bool PreviewNoIconFont { get; set; }
#endif
    }

    /// <summary>
    /// One glyph that carries meaning on its own (a notice's severity, the error dialog's glyph): not focusable, role
    /// Graphic, always named, so UI Automation shows it as an Image with that name. Decorative glyphs are never controls;
    /// their label or control paints them.
    /// </summary>
    internal sealed class GlyphIcon : Control, IThemed
    {
        private Glyph _glyph;
        private ThemeToken _color;

        public GlyphIcon(Glyph glyph, int sizeEpx, ThemeToken color, string accessibleName)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            _glyph = glyph;
            _color = color;
            SizeEpx = sizeEpx;
            int px = UiScale.Current.Px(sizeEpx);
            Size = new Size(px, px);
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = accessibleName;
        }

        public int SizeEpx { get; }

        public Glyph Glyph
        {
            get => _glyph;
            set
            {
                if (_glyph == value) return;
                _glyph = value;
                Invalidate();
            }
        }

        public ThemeToken ColorToken
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                Invalidate();
            }
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            int px = UiScale.Current.Px(SizeEpx);
            var box = new Rectangle((ClientSize.Width - px) / 2, (ClientSize.Height - px) / 2, px, px);
            GlyphFont.Draw(e.Graphics, _glyph, box, ThemeManager.Current[_color]);
        }
    }
}
