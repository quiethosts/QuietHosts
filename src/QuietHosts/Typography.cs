using System;
using System.Collections.Generic;
using System.Drawing;
using Microsoft.Win32;

namespace QuietHosts
{
    /// <summary>The Fluent type ramp's styles (spec 2.6).</summary>
    internal enum TextStyle
    {
        /// <summary>Tags, pills, list header text: the base size.</summary>
        Caption,
        /// <summary>All other text, buttons, links, list rows: 7/6 of the base.</summary>
        Body,
        /// <summary>Card and section titles, field labels: Body in Segoe UI Semibold.</summary>
        BodyStrong,
        /// <summary>Status headline, dialog headings, About's name: 5/3 of the base, Semibold.</summary>
        Subtitle,
        /// <summary>The first-run title only: 7/3 of the base, Semibold.</summary>
        Title,
        /// <summary>Hosts lines in the Microsoft-lines dialog: Body size in Consolas.</summary>
        Mono,
    }

    /// <summary>Font helpers for <see cref="TypeRamp"/>, and the Debug text-size simulation.</summary>
    internal static class UiFonts
    {
#if DEBUG
        /// <summary>Debug --render-ui and tests only: scales the base font, like the Windows text-size setting.</summary>
        public static float TextScale { get; set; } = 1f;
#endif

        /// <summary>"Segoe UI Semibold" when the family is Segoe UI and GDI returns that name; otherwise the family, bold.</summary>
        public static Font Semibold(string family, float points)
        {
            if (string.Equals(family, "Segoe UI", StringComparison.OrdinalIgnoreCase))
            {
                Font? semibold = TryFont("Segoe UI Semibold", points, FontStyle.Regular);
                if (semibold != null) return semibold;
            }
            return new Font(family, points, FontStyle.Bold, GraphicsUnit.Point);
        }

        /// <summary>The font if GDI really gives that family (not a substitute), else null.</summary>
        public static Font? TryFont(string family, float points, FontStyle style)
        {
            try
            {
                var f = new Font(family, points, style, GraphicsUnit.Point);
                if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch (ArgumentException)
            {
            }
            return null;
        }
    }

    /// <summary>
    /// One window's fonts on the Fluent ramp (spec 2.6), from the base size B: the message-box font, with a fallback that
    /// reads Windows' text-size value itself. Fonts are in points, so GDI applies the DPI. Disposed with the window.
    /// </summary>
    internal sealed class TypeRamp : IDisposable
    {
        public const string AccessibilityKey = @"SOFTWARE\Microsoft\Accessibility";
        public const string TextScaleFactorValue = "TextScaleFactor";

        private readonly Dictionary<TextStyle, Font> _fonts = new Dictionary<TextStyle, Font>();
        private readonly UiScale _scale;
        private bool _disposed;

        public TypeRamp() : this(ResolveBasePoints())
        {
        }

        /// <summary>A ramp for a given base size in points (tests: B = 9 or 13.5).</summary>
        internal TypeRamp(float basePoints)
        {
            if (basePoints <= 0) throw new ArgumentOutOfRangeException(nameof(basePoints));
            BasePoints = basePoints;
            _scale = UiScale.Current;
            Family = BaseFamily();
        }

        /// <summary>B, in points.</summary>
        public float BasePoints { get; }
        /// <summary>The base family (Segoe UI on Windows 10 and 11).</summary>
        public string Family { get; }
        public UiScale Scale => _scale;

        public Font Caption => Font(TextStyle.Caption);
        public Font Body => Font(TextStyle.Body);
        public Font BodyStrong => Font(TextStyle.BodyStrong);
        public Font Subtitle => Font(TextStyle.Subtitle);
        public Font Title => Font(TextStyle.Title);
        public Font Mono => Font(TextStyle.Mono);

        /// <summary>The style's size in points (B times the ramp factor).</summary>
        public float Points(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Caption: return BasePoints;
                case TextStyle.Subtitle: return BasePoints * 5f / 3f;
                case TextStyle.Title: return BasePoints * 7f / 3f;
                default: return BasePoints * 7f / 6f;
            }
        }

        /// <summary>The ramp's line height over the font size (16/12, 20/14, 28/20, 36/28).</summary>
        public static float LineRatio(TextStyle style)
        {
            switch (style)
            {
                case TextStyle.Caption: return 16f / 12f;
                case TextStyle.Subtitle: return 28f / 20f;
                case TextStyle.Title: return 36f / 28f;
                default: return 20f / 14f;
            }
        }

        /// <summary>The style's size in device pixels at this ramp's DPI.</summary>
        public float SizePx(TextStyle style) => Points(style) * _scale.Dpi / 72f;

        /// <summary>The ramp's line height in device pixels: round(size in px times the ratio).</summary>
        public int LineHeight(TextStyle style) => (int)Math.Round(SizePx(style) * LineRatio(style), MidpointRounding.AwayFromZero);

        /// <summary>A one-line control's height: max(Px(min), the style's line + Px(pad)).</summary>
        public int TextDriven(int minEpx, TextStyle style, int padEpx) => _scale.TextDriven(minEpx, LineHeight(style), padEpx);

        /// <summary>The style's font (created once, owned by the ramp).</summary>
        public Font Font(TextStyle style)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TypeRamp));
            if (_fonts.TryGetValue(style, out Font? f)) return f;
            float points = Points(style) * _scale.FontFactor;
            switch (style)
            {
                case TextStyle.BodyStrong:
                case TextStyle.Subtitle:
                case TextStyle.Title:
                    f = UiFonts.Semibold(Family, points);
                    break;
                case TextStyle.Mono:
                    f = UiFonts.TryFont("Consolas", points, FontStyle.Regular) ?? new Font(Family, points, FontStyle.Regular, GraphicsUnit.Point);
                    break;
                default:
                    f = new Font(Family, points, FontStyle.Regular, GraphicsUnit.Point);
                    break;
            }
            _fonts[style] = f;
            return f;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Font f in _fonts.Values) f.Dispose();
            _fonts.Clear();
        }

        private static string BaseFamily()
        {
            using (Font mb = SystemFonts.MessageBoxFont) return mb.FontFamily.Name;
        }

        // ---------------------------------------------------------------- the base size

        /// <summary>
        /// B for this PC: the message-box font's size, or that size times Windows' text-size value when the font doesn't
        /// follow the slider (2.6); in Debug times the text-size simulation.
        /// </summary>
        public static float ResolveBasePoints()
        {
            float b;
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewBasePoints.HasValue) b = PreviewBasePoints.Value;
            else
#endif
                b = ResolveBasePoints(MessageBoxPoints(), ReadTextScaleFactor());
#if DEBUG
            b *= UiFonts.TextScale;
#endif
            return b;
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>
        /// Tests: B in points instead of this PC's message-box font and text-size value (null: this PC's), so a window test
        /// lays out alike at any Windows text size (spec 10: no test depends on the PC's text size). The window tests pin it
        /// to 9 pt, the normal text size; the Debug text-size simulation still multiplies it.
        /// </summary>
        internal static float? PreviewBasePoints { get; set; }
#endif

        /// <param name="messageBoxPoints">SystemFonts.MessageBoxFont.SizeInPoints.</param>
        /// <param name="textScaleFactor">The raw TextScaleFactor value; missing, out of range or unreadable is 100.</param>
        public static float ResolveBasePoints(float messageBoxPoints, int? textScaleFactor)
        {
            int tsf = TextScalePercent(textScaleFactor);
            // If the message-box font follows the slider it is already about 9 x tsf/100: nothing is multiplied twice.
            return messageBoxPoints >= 9f * tsf / 100f * 0.95f ? messageBoxPoints : messageBoxPoints * tsf / 100f;
        }

        /// <summary>The text size in percent: the value when it is 100 to 225, else 100.</summary>
        public static int TextScalePercent(int? raw) => raw.HasValue && raw.Value >= 100 && raw.Value <= 225 ? raw.Value : 100;

        /// <summary>HKCU\SOFTWARE\Microsoft\Accessibility\TextScaleFactor as a DWORD, or null. Read-only; never written.</summary>
        public static int? ReadTextScaleFactor()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccessibilityKey, false))
                {
                    return key?.GetValue(TextScaleFactorValue) is int value ? value : (int?)null;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return null;
            }
        }

        /// <summary>The message-box font's size in points.</summary>
        public static float MessageBoxPoints()
        {
            using (Font mb = SystemFonts.MessageBoxFont) return mb.SizeInPoints;
        }
    }
}
