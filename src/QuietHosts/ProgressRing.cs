using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>
    /// The progress ring (spec 2.8.15), the app's only animation: 16 epx, not focusable, role ProgressBar named after the
    /// progress text next to it. Its 33 ms timer runs only while it is active and visible. With Windows' "Animation
    /// effects" off (read at each activation) it shows the static Sync glyph instead and runs no timer.
    /// </summary>
    internal sealed class ProgressRing : Control, IThemed
    {
        private const int TickMs = 33;
        private const float DegreesPerTick = 12f;
        private readonly Timer _timer;
        private bool _active, _animate;
        private float _angle;
        private DateTime _started;

        public ProgressRing()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
            Margin = Padding.Empty;
            AccessibleRole = AccessibleRole.ProgressBar;
            int px = UiScale.Current.Px(Metrics.ProgressRing);
            Size = new Size(px, px);
            _timer = new Timer { Interval = TickMs };
            _timer.Tick += OnTick;
        }

        /// <summary>Shows progress (spins, or the static glyph with animations off). Inactive, it draws nothing.</summary>
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                if (value) Activate();
                UpdateTimer();
                Invalidate();
            }
        }

        /// <summary>Tests: the animation timer is running.</summary>
        internal bool TimerRunning => _timer.Enabled;

        /// <summary>Whether it spins (false: animations were off at the last activation).</summary>
        internal bool Animates => _animate;

        private void Activate()
        {
            _animate = AnimationsOn();
            _angle = 0f;
            _started = DateTime.UtcNow;
        }

        private bool AnimationsOn()
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewAnimations.HasValue) return PreviewAnimations.Value;
            if (AnimationsOverride.HasValue) return AnimationsOverride.Value;
#endif
            return NativeTheme.ClientAreaAnimation();
        }

        private void UpdateTimer()
        {
            bool run = _active && _animate && Visible && IsHandleCreated;
            if (_timer.Enabled != run) _timer.Enabled = run;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Showing again is a new activation: the animation setting is read again.
            if (Visible && _active) Activate();
            UpdateTimer();
        }

        protected override void OnParentVisibleChanged(EventArgs e)
        {
            base.OnParentVisibleChanged(e);
            UpdateTimer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateTimer();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _timer.Enabled = false;
            base.OnHandleDestroyed(e);
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!_active || !Visible)
            {
                UpdateTimer();
                return;
            }
            _angle = (_angle + DegreesPerTick) % 360f;
            Invalidate();
        }

        public void ApplyTheme(Theme theme) => Invalidate();

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_active) return;
            Theme t = ThemeManager.Current;
            UiScale s = UiScale.Current;
            int px = s.Px(Metrics.ProgressRing);
            var box = new Rectangle((ClientSize.Width - px) / 2, (ClientSize.Height - px) / 2, px, px);
            if (!_animate)
            {
                GlyphFont.Draw(e.Graphics, Glyph.Sync, box, t.ProgressRing);
                return;
            }
            float seconds = (float)(DateTime.UtcNow - _started).TotalSeconds;
            float sweep = 60f + 200f * (1f - (float)Math.Cos(2 * Math.PI * seconds / 2.0)) / 2f;
            int width = s.Stroke(2);
            float inset = width / 2f + 0.5f;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(t.ProgressRing, width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, box.X + inset, box.Y + inset, box.Width - 2 * inset, box.Height - 2 * inset, _angle - 90f, sweep);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests and renders: Windows' animation setting as if it were this (null: the real one).</summary>
        internal static bool? AnimationsOverride { get; set; }

        /// <summary>The gallery and one render scenario: this ring's animation setting (null: <see cref="AnimationsOverride"/> or the real one).</summary>
        internal bool? PreviewAnimations { get; set; }
#endif
    }
}
