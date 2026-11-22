using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>What the status icon shows. The state is always also in words next to it.</summary>
    internal enum ShieldKind
    {
        /// <summary>Grey shield with a dash: protection is off.</summary>
        Off,
        /// <summary>Green shield with a check mark: protection is on.</summary>
        On,
        /// <summary>Amber shield with an exclamation mark: something needs attention.</summary>
        Attention,
    }

    /// <summary>
    /// The main window's status shield (spec 2.8.11): vectors in the token colors (ShieldOn, ShieldOff, ShieldAttention,
    /// with the ShieldSymbol inside); WindowText with a Window symbol in high contrast. Its accessible name is the headline.
    /// The window's ApplyTheme repaints it on a theme change, and it repaints itself on a system color change. By default it is the
    /// status card's 64 epx square at the current DPI, with no margin (the card's layout places it).
    /// </summary>
    internal sealed class ShieldIcon : Control, IThemed
    {
        private ShieldKind _kind;

        public ShieldIcon()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            // The surface it sits on (the status card) shows around the shield.
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            int px = UiScale.Current.Px(Metrics.StatusShield);
            Size = new Size(px, px);
            Margin = Padding.Empty;
        }

        public ShieldKind Kind
        {
            get => _kind;
            set
            {
                if (_kind == value) return;
                _kind = value;
                Invalidate();
            }
        }

        public static ShieldKind KindFor(ProtectionState state)
        {
            switch (state)
            {
                case ProtectionState.On: return ShieldKind.On;
                case ProtectionState.Off: return ShieldKind.Off;
                default: return ShieldKind.Attention;
            }
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Theme t = ThemeManager.Current;
            ShieldPainter.Paint(e.Graphics, ClientRectangle, ShieldPainter.FillFor(_kind, t), ShieldPainter.SymbolFor(_kind, t), _kind);
        }
    }

    internal static class ShieldPainter
    {
        /// <summary>The shield's color: ShieldOn, ShieldOff or ShieldAttention (all WindowText in high contrast).</summary>
        public static Color FillFor(ShieldKind kind, Theme theme)
        {
            switch (kind)
            {
                case ShieldKind.On: return theme.ShieldOn;
                case ShieldKind.Off: return theme.ShieldOff;
                default: return theme.ShieldAttention;
            }
        }

        /// <summary>The check, dash or "!" inside it: ShieldSymbol (Window in high contrast).</summary>
        public static Color SymbolFor(ShieldKind kind, Theme theme) => theme.ShieldSymbol;

        public static void Paint(Graphics g, Rectangle bounds, Color fill, Color symbol, ShieldKind kind)
        {
            float s = Math.Min(bounds.Width, bounds.Height);
            if (s < 4) return;
            float x = bounds.X + (bounds.Width - s) / 2f;
            float y = bounds.Y + (bounds.Height - s) / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (GraphicsPath shield = ShieldPath(x, y, s))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, shield);
            }

            float width = Math.Max(1.5f, s * 0.1f);
            using (var pen = new Pen(symbol, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var dot = new SolidBrush(symbol))
            {
                switch (kind)
                {
                    case ShieldKind.On:
                        g.DrawLines(pen, new[] { P(x, y, s, 0.32f, 0.50f), P(x, y, s, 0.45f, 0.63f), P(x, y, s, 0.69f, 0.37f) });
                        break;
                    case ShieldKind.Off:
                        g.DrawLine(pen, P(x, y, s, 0.35f, 0.48f), P(x, y, s, 0.65f, 0.48f));
                        break;
                    default:
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.27f), P(x, y, s, 0.5f, 0.53f));
                        float r = Math.Max(1f, s * 0.062f);
                        PointF c = P(x, y, s, 0.5f, 0.70f);
                        g.FillEllipse(dot, c.X - r, c.Y - r, 2 * r, 2 * r);
                        break;
                }
            }
        }

        private static PointF P(float x, float y, float s, float fx, float fy) => new PointF(x + s * fx, y + s * fy);

        /// <summary>A shield in the square (x, y, s): a gently curved top, straight sides and a rounded point.</summary>
        public static GraphicsPath ShieldPath(float x, float y, float s)
        {
            var path = new GraphicsPath();
            PointF leftTop = P(x, y, s, 0.12f, 0.18f);
            PointF top = P(x, y, s, 0.50f, 0.05f);
            PointF rightTop = P(x, y, s, 0.88f, 0.18f);
            PointF rightMid = P(x, y, s, 0.88f, 0.47f);
            PointF bottom = P(x, y, s, 0.50f, 0.96f);
            PointF leftMid = P(x, y, s, 0.12f, 0.47f);
            path.StartFigure();
            path.AddBezier(leftTop, P(x, y, s, 0.27f, 0.16f), P(x, y, s, 0.40f, 0.11f), top);
            path.AddBezier(top, P(x, y, s, 0.60f, 0.11f), P(x, y, s, 0.73f, 0.16f), rightTop);
            path.AddLine(rightTop, rightMid);
            path.AddBezier(rightMid, P(x, y, s, 0.88f, 0.72f), P(x, y, s, 0.70f, 0.87f), bottom);
            path.AddBezier(bottom, P(x, y, s, 0.30f, 0.87f), P(x, y, s, 0.12f, 0.72f), leftMid);
            path.CloseFigure();
            return path;
        }
    }
}
