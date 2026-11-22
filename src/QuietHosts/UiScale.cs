using System;
using System.Drawing;

namespace QuietHosts
{
    /// <summary>
    /// Effective pixels (epx) to device pixels, at the system DPI read once for the process (spec 2.7). The app is system
    /// DPI aware; widths scale with the DPI only, never with the text size.
    /// </summary>
    internal sealed class UiScale
    {
        private static UiScale? _current;
        private static int _systemDpi;

        internal UiScale(int dpi, float fontFactor = 1f)
        {
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
            Dpi = dpi;
            FontFactor = fontFactor;
        }

        /// <summary>The process's scale: the system DPI (or, in Debug renders and tests, an override).</summary>
        public static UiScale Current => _current ??= new UiScale(SystemDpi);

        /// <summary>The real system DPI, read once with Graphics.FromHwnd(IntPtr.Zero).DpiX.</summary>
        internal static int SystemDpi
        {
            get
            {
                if (_systemDpi > 0) return _systemDpi;
                int dpi = 96;
                try
                {
                    using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) dpi = (int)Math.Round(g.DpiX);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is OutOfMemoryException || ex is System.ComponentModel.Win32Exception)
                {
                }
                _systemDpi = dpi > 0 ? dpi : 96;
                return _systemDpi;
            }
        }

        public int Dpi { get; }

        /// <summary>
        /// 1, except in a Debug DPI simulation: simulated DPI / real DPI. Fonts are given in points, which GDI turns into
        /// pixels at the real DPI, so the simulation multiplies them by this.
        /// </summary>
        public float FontFactor { get; }

        /// <summary>Spacing, padding, widths, icon and glyph boxes, radii.</summary>
        public int Px(int epx) => (int)Math.Round(epx * Dpi / 96.0, MidpointRounding.AwayFromZero);

        public float PxF(float epx) => epx * Dpi / 96f;

        /// <summary>Borders, rings, bars: never thinner than 1 px.</summary>
        public int Stroke(int epx) => Math.Max(1, (int)Math.Floor(epx * Dpi / 96.0));

        /// <summary>A one-line control's height: max(Px(min), line height + Px(pad)).</summary>
        public int TextDriven(int minEpx, int lineHeightPx, int padEpx) => Math.Max(Px(minEpx), lineHeightPx + Px(padEpx));

        public Size Px(int widthEpx, int heightEpx) => new Size(Px(widthEpx), Px(heightEpx));

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Debug renders (QUIETHOSTS_RENDER_DPI) and tests: act as if the system DPI were <paramref name="dpi"/>.</summary>
        internal static void Override(int dpi) => _current = new UiScale(dpi, dpi / (float)SystemDpi);

        /// <summary>Tests: back to the system DPI.</summary>
        internal static void ResetForTests() => _current = null;
#endif
    }

    /// <summary>The spec's metrics (section 2.7), all in epx. Turn them into pixels with <see cref="UiScale.Px(int)"/>.</summary>
    internal static partial class Metrics
    {
        public const int PagePadding = 24;
        public const int SectionGap = 24;
        /// <summary>Between the cards of a group.</summary>
        public const int CardGap = 4;
        public const int CardPadding = 16;
        public const int StatusCardPadding = 20;
        /// <summary>From a heading to its content.</summary>
        public const int HeadingGap = 8;
        public const int TextToButton = 16;
        public const int ButtonGap = 8;
        public const int Radius = 4;
        public const int Border = 1;
        public const int FocusOuter = 2;
        public const int FocusInner = 1;
        public const int ButtonHeight = 32;
        public const int ButtonMinWidth = 120;
        public const int ButtonPadX = 12;
        /// <summary>The TextDriven pad of a button: its height is at least the Body line plus this.</summary>
        public const int ButtonPadY = 12;
        /// <summary>The UAC shield on a button, and its gap to the text.</summary>
        public const int ShieldIcon = 16;
        public const int ShieldGap = 8;
        public const int NoticeMinHeight = 48;
        public const int NoticePadTop = 12;
        public const int NoticePadBottom = 12;
        public const int NoticePadLeft = 16;
        /// <summary>8 when the bar has a close button, 16 otherwise.</summary>
        public const int NoticePadRight = 8;
        public const int NoticePadRightNotClosable = 16;
        public const int NoticeGlyph = 20;
        public const int NoticeGlyphGap = 12;
        public const int NoticeClose = 32;
        public const int NoticeCloseGlyph = 16;
        /// <summary>Between bars.</summary>
        public const int NoticeGap = 8;
        /// <summary>From the status card to the first bar.</summary>
        public const int NoticeTop = 12;
        public const int CardMinHeight = 68;
        public const int PillHeight = 20;
        public const int PillPadX = 8;
        public const int ListRow = 32;
        public const int SelectionBarWidth = 3;
        public const int SelectionBarHeight = 16;
        public const int SearchHeight = 32;
        public const int ClearButton = 24;
        public const int CheckBoxBox = 20;
        public const int CheckBoxGap = 8;
        public const int ProgressRing = 16;
        public const int StatusShield = 64;
        public const int AppIcon = 48;
        public const int StepBadge = 24;
        public const int MainClientWidth = 576;
        public const int StatusTextX = 100;
        public const int StatusTextWidth = 408;
        public const int LevelRightMinWidth = 120;
        public const int DialogWidth = 480;
        public const int MicrosoftLinesDialogWidth = 560;
        public const int FixDefaultClientWidth = 760;
        public const int FixDefaultClientHeight = 680;
        public const int FixMinClientWidth = 640;
        public const int FixMinClientHeight = 480;
        /// <summary>The main window is at most the work area minus this tall (D14; 16, not 48, since D19).</summary>
        public const int MainHeightMargin = 16;
        /// <summary>Every target is at least this large in both directions.</summary>
        public const int TargetMin = 24;
        /// <summary>Body text is never wider than this (about 60 characters).</summary>
        public const int MaxTextWidth = 432;
    }
}
