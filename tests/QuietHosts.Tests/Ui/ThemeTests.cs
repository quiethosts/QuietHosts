using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// The theme model (spec 2.1-2.4): which theme applies, the token table in light, dark and high contrast, the contrast
    /// pairs, live switching and its debounce (T-A-01..03, T-A-11..14, T-A-30). Nothing here depends on the PC's theme:
    /// windows use <see cref="ThemeManager.Override"/> and every test resets the globals it touched.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class ThemeTests
    {
        // ---------------------------------------------------------------- T-A-01

        [Theory]
        [InlineData(true, 19045, 1, "HighContrast")]
        [InlineData(true, 22000, 0, "HighContrast")]
        [InlineData(true, 26300, null, "HighContrast")]
        [InlineData(false, 19045, 0, "Light")]
        [InlineData(false, 21999, 0, "Light")]
        [InlineData(false, 22000, 0, "Dark")]
        [InlineData(false, 26300, 0, "Dark")]
        [InlineData(false, 22000, 1, "Light")]
        [InlineData(false, 22000, null, "Light")]
        [InlineData(false, 22000, "0", "Light")]
        [InlineData(false, 22000, 2, "Light")]
        [InlineData(false, 26300, -1, "Light")]
        public void The_theme_follows_high_contrast_then_the_windows_11_app_mode(bool highContrast, int build, object? appsUseLightTheme, string expected)
        {
            Assert.Equal(expected, ThemeResolver.Resolve(highContrast, build, appsUseLightTheme).ToString());
        }

        [Fact]
        public void A_long_or_a_byte_array_value_is_not_the_dword_zero()
        {
            Assert.Equal(ThemeKind.Light, ThemeResolver.Resolve(false, 26300, 0L));
            Assert.Equal(ThemeKind.Light, ThemeResolver.Resolve(false, 26300, new byte[] { 0, 0, 0, 0 }));
        }

        // ---------------------------------------------------------------- T-A-02

        /// <summary>Every color token, light and dark, as spec 2.2 (and the control tables of 2.8) give it: 0xAARRGGBB.</summary>
        private static readonly Dictionary<ThemeToken, (uint Light, uint Dark)> Table = new Dictionary<ThemeToken, (uint, uint)>
        {
            [ThemeToken.WindowBackground] = (0xFFF3F3F3, 0xFF202020),
            [ThemeToken.CardBackground] = (0xFFFBFBFB, 0xFF2B2B2B),
            [ThemeToken.CardBorder] = (0xFFE5E5E5, 0xFF1D1D1D),
            [ThemeToken.Divider] = (0xFFE5E5E5, 0xFF323232),
            [ThemeToken.TextPrimary] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.TextSecondary] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.TextDisabled] = (0xFFA0A0A0, 0xFF7E7E7E),
            [ThemeToken.Link] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.LinkHover] = (0xFF003E92, 0xFF99EBFF),
            [ThemeToken.AccentFill] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.AccentFillHover] = (0xFF196EBF, 0xFF5BBDEA),
            [ThemeToken.AccentFillPressed] = (0xFF004C93, 0xFF55ADD5),
            [ThemeToken.TextOnAccent] = (0xFFFFFFFF, 0xFF000000),
            [ThemeToken.AccentText] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.ButtonFill] = (0xFFFEFEFE, 0xFF373737),
            [ThemeToken.ButtonFillHover] = (0xFFFAFAFA, 0xFF3C3C3C),
            [ThemeToken.ButtonFillPressed] = (0xFFF5F5F5, 0xFF323232),
            [ThemeToken.ButtonText] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.ButtonTextPressed] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.ButtonBorder] = (0xFFE5E5E5, 0xFF3F3F3F),
            [ThemeToken.ButtonBorderBottom] = (0xFFD3D3D3, 0xFF3F3F3F),
            [ThemeToken.DisabledFill] = (0xFFF9F9F9, 0xFF343434),
            [ThemeToken.DisabledBorder] = (0xFFE5E5E5, 0xFF424242),
            [ThemeToken.SubtleHover] = (0x0A000000, 0x0FFFFFFF),
            [ThemeToken.SubtlePressed] = (0x06000000, 0x0AFFFFFF),
            [ThemeToken.SuccessGlyph] = (0xFF0F7B0F, 0xFF6CCB5F),
            [ThemeToken.SuccessBar] = (0xFFDFF6DD, 0xFF393D1B),
            [ThemeToken.CautionGlyph] = (0xFF9D5D00, 0xFFFCE100),
            [ThemeToken.CautionBar] = (0xFFFFF4CE, 0xFF433519),
            [ThemeToken.CriticalGlyph] = (0xFFC42B1C, 0xFFFF99A4),
            [ThemeToken.CriticalBar] = (0xFFFDE7E9, 0xFF442726),
            [ThemeToken.InfoGlyph] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.ShieldOn] = (0xFF0F7B0F, 0xFF6CCB5F),
            [ThemeToken.ShieldOff] = (0xFF6E6E6E, 0xFF9F9F9F),
            [ThemeToken.ShieldAttention] = (0xFF9D5D00, 0xFFFCE100),
            [ThemeToken.ShieldSymbol] = (0xFFFFFFFF, 0xFF000000),
            [ThemeToken.ListHover] = (0xFFF2F2F2, 0xFF373737),
            [ThemeToken.ListSelected] = (0xFFECECEC, 0xFF3C3C3C),
            [ThemeToken.SelectionBar] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.PillAccentFill] = (0xFFE1EBF4, 0xFF31434B),
            [ThemeToken.PillAccentText] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.PillNeutralFill] = (0xFFECECEC, 0xFF454545),
            [ThemeToken.PillNeutralText] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.PillNeutralFillSelected] = (0xFFDCDCDC, 0xFF505050),
            [ThemeToken.PillOutlineBorder] = (0xFF8B8B8B, 0xFF9F9F9F),
            [ThemeToken.PillOutlineText] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.InputFill] = (0xFFFBFBFB, 0xFF2D2D2D),
            [ThemeToken.InputFillHover] = (0xFFF6F6F6, 0xFF323232),
            [ThemeToken.InputFillFocused] = (0xFFFFFFFF, 0xFF1F1F1F),
            [ThemeToken.InputBorder] = (0xFFE5E5E5, 0xFF3C3C3C),
            [ThemeToken.InputBorderBottom] = (0xFF8B8B8B, 0xFF9A9A9A),
            [ThemeToken.InputBorderFocused] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.InputHint] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.CheckBoxFill] = (0xFFEDEDED, 0xFF1D1D1D),
            [ThemeToken.CheckBoxFillHover] = (0xFFE4E4E4, 0xFF2A2A2A),
            [ThemeToken.CheckBoxBorder] = (0xFF868686, 0xFF9A9A9A),
            [ThemeToken.CheckBoxDisabledBorder] = (0xFFBFBFBF, 0xFF434343),
            [ThemeToken.FocusOuter] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.FocusInner] = (0xFFFFFFFF, 0xFF000000),
            [ThemeToken.ProgressRing] = (0xFF005FB8, 0xFF60CDFF),
            // The state colors of the control tables (2.8.3, 2.8.12, 2.8.14).
            [ThemeToken.TextOnAccentHover] = (0xFFFFFFFF, 0xFF000000),
            [ThemeToken.ButtonTextHover] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.ButtonBorderHover] = (0xFFE5E5E5, 0xFF3F3F3F),
            [ThemeToken.SubtleGlyphHover] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.SubtleGlyphPressed] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.ListTextSelected] = (0xFF1A1A1A, 0xFFFFFFFF),
            [ThemeToken.ListSecondaryTextSelected] = (0xFF5F5F5F, 0xFFCFCFCF),
            [ThemeToken.CheckBoxBorderHover] = (0xFF868686, 0xFF9A9A9A),
            [ThemeToken.CheckBoxCheckedFill] = (0xFF005FB8, 0xFF60CDFF),
            [ThemeToken.CheckBoxCheckedFillHover] = (0xFF196EBF, 0xFF5BBDEA),
            [ThemeToken.CheckBoxGlyph] = (0xFFFFFFFF, 0xFF000000),
            [ThemeToken.CheckBoxDisabledFill] = (0xFFF9F9F9, 0xFF343434),
            [ThemeToken.CheckBoxDisabledCheckedFill] = (0xFFBFBFBF, 0xFF434343),
            [ThemeToken.CheckBoxDisabledGlyph] = (0xFFFFFFFF, 0xFFA7A7A7),
        };

        [Fact]
        public void Light_and_dark_follow_the_token_table()
        {
            Assert.Equal(Enum.GetValues(typeof(ThemeToken)).Length, Table.Count);
            var wrong = new List<string>();
            foreach (KeyValuePair<ThemeToken, (uint Light, uint Dark)> row in Table)
            {
                uint light = unchecked((uint)Theme.Light[row.Key].ToArgb()), dark = unchecked((uint)Theme.Dark[row.Key].ToArgb());
                if (light != row.Value.Light) wrong.Add("light " + row.Key + ": " + light.ToString("X8") + ", expected " + row.Value.Light.ToString("X8"));
                if (dark != row.Value.Dark) wrong.Add("dark " + row.Key + ": " + dark.ToString("X8") + ", expected " + row.Value.Dark.ToString("X8"));
            }
            Assert.Empty(wrong);
        }

        [Fact]
        public void The_theme_numbers_follow_the_table()
        {
            Theme hc = Theme.ForHighContrast(HighContrastPalette.HcBlack);
            Assert.Equal(1, Theme.Light.CardBorderWidth);
            Assert.Equal(1, hc.CardBorderWidth);
            Assert.Equal(0, Theme.Light.DialogFrameWidth);
            Assert.Equal(0, Theme.Dark.DialogFrameWidth);
            Assert.Equal(2, hc.DialogFrameWidth);
            Assert.Equal(0, Theme.Dark.AccentBorderWidth);
            Assert.Equal(2, hc.AccentBorderWidth);
            Assert.False(Theme.Light.LinksUnderlinedAtRest);
            Assert.True(hc.LinksUnderlinedAtRest);
            Assert.False(Theme.Light.UseDarkFrame);
            Assert.True(Theme.Dark.UseDarkFrame);
            Assert.False(hc.UseDarkFrame);
            Assert.Null(Theme.Light.NativeThemeName);
            Assert.Equal("DarkMode_Explorer", Theme.Dark.NativeThemeName);
            Assert.Null(hc.NativeThemeName);
            Assert.Null(Theme.Light.HeaderThemeName);
            Assert.Equal("DarkMode_ItemsView", Theme.Dark.HeaderThemeName);
            Assert.Null(hc.HeaderThemeName);
        }

        // ---------------------------------------------------------------- T-A-03

        /// <summary>Which of the eight colors each token is in high contrast (spec 2.2's HC column).</summary>
        private static readonly Dictionary<ThemeToken, Func<HighContrastPalette, Color>> HighContrastColumn = new Dictionary<ThemeToken, Func<HighContrastPalette, Color>>
        {
            [ThemeToken.WindowBackground] = p => p.Window,
            [ThemeToken.CardBackground] = p => p.Window,
            [ThemeToken.CardBorder] = p => p.WindowText,
            [ThemeToken.Divider] = p => p.WindowText,
            [ThemeToken.TextPrimary] = p => p.WindowText,
            [ThemeToken.TextSecondary] = p => p.WindowText,
            [ThemeToken.TextDisabled] = p => p.GrayText,
            [ThemeToken.Link] = p => p.HotTrack,
            [ThemeToken.LinkHover] = p => p.HotTrack,
            [ThemeToken.AccentFill] = p => p.ButtonFace,
            [ThemeToken.AccentFillHover] = p => p.Highlight,
            [ThemeToken.AccentFillPressed] = p => p.Highlight,
            [ThemeToken.TextOnAccent] = p => p.ButtonText,
            [ThemeToken.AccentText] = p => p.WindowText,
            [ThemeToken.ButtonFill] = p => p.ButtonFace,
            [ThemeToken.ButtonFillHover] = p => p.Highlight,
            [ThemeToken.ButtonFillPressed] = p => p.Highlight,
            [ThemeToken.ButtonText] = p => p.ButtonText,
            [ThemeToken.ButtonTextPressed] = p => p.HighlightText,
            [ThemeToken.ButtonBorder] = p => p.ButtonText,
            [ThemeToken.ButtonBorderBottom] = p => p.ButtonText,
            [ThemeToken.DisabledFill] = p => p.ButtonFace,
            [ThemeToken.DisabledBorder] = p => p.GrayText,
            [ThemeToken.SubtleHover] = p => p.Highlight,
            [ThemeToken.SubtlePressed] = p => p.Highlight,
            [ThemeToken.SuccessGlyph] = p => p.WindowText,
            [ThemeToken.SuccessBar] = p => p.Window,
            [ThemeToken.CautionGlyph] = p => p.WindowText,
            [ThemeToken.CautionBar] = p => p.Window,
            [ThemeToken.CriticalGlyph] = p => p.WindowText,
            [ThemeToken.CriticalBar] = p => p.Window,
            [ThemeToken.InfoGlyph] = p => p.WindowText,
            [ThemeToken.ShieldOn] = p => p.WindowText,
            [ThemeToken.ShieldOff] = p => p.WindowText,
            [ThemeToken.ShieldAttention] = p => p.WindowText,
            [ThemeToken.ShieldSymbol] = p => p.Window,
            [ThemeToken.ListHover] = p => p.Highlight,
            [ThemeToken.ListSelected] = p => p.Highlight,
            [ThemeToken.SelectionBar] = p => p.HighlightText,
            [ThemeToken.PillAccentFill] = p => p.Window,
            [ThemeToken.PillAccentText] = p => p.WindowText,
            [ThemeToken.PillNeutralFill] = p => p.Window,
            [ThemeToken.PillNeutralText] = p => p.WindowText,
            [ThemeToken.PillNeutralFillSelected] = p => p.Highlight,
            [ThemeToken.PillOutlineBorder] = p => p.WindowText,
            [ThemeToken.PillOutlineText] = p => p.WindowText,
            [ThemeToken.InputFill] = p => p.Window,
            [ThemeToken.InputFillHover] = p => p.Window,
            [ThemeToken.InputFillFocused] = p => p.Window,
            [ThemeToken.InputBorder] = p => p.WindowText,
            [ThemeToken.InputBorderBottom] = p => p.WindowText,
            [ThemeToken.InputBorderFocused] = p => p.Highlight,
            [ThemeToken.InputHint] = p => p.WindowText,
            [ThemeToken.CheckBoxFill] = p => p.Window,
            [ThemeToken.CheckBoxFillHover] = p => p.Window,
            [ThemeToken.CheckBoxBorder] = p => p.WindowText,
            [ThemeToken.CheckBoxDisabledBorder] = p => p.GrayText,
            [ThemeToken.FocusOuter] = p => p.WindowText,
            [ThemeToken.FocusInner] = p => p.Window,
            [ThemeToken.ProgressRing] = p => p.Highlight,
            [ThemeToken.TextOnAccentHover] = p => p.HighlightText,
            [ThemeToken.ButtonTextHover] = p => p.HighlightText,
            [ThemeToken.ButtonBorderHover] = p => p.Highlight,
            [ThemeToken.SubtleGlyphHover] = p => p.HighlightText,
            [ThemeToken.SubtleGlyphPressed] = p => p.HighlightText,
            [ThemeToken.ListTextSelected] = p => p.HighlightText,
            [ThemeToken.ListSecondaryTextSelected] = p => p.HighlightText,
            [ThemeToken.CheckBoxBorderHover] = p => p.Highlight,
            [ThemeToken.CheckBoxCheckedFill] = p => p.Highlight,
            [ThemeToken.CheckBoxCheckedFillHover] = p => p.Highlight,
            [ThemeToken.CheckBoxGlyph] = p => p.HighlightText,
            [ThemeToken.CheckBoxDisabledFill] = p => p.Window,
            [ThemeToken.CheckBoxDisabledCheckedFill] = p => p.GrayText,
            [ThemeToken.CheckBoxDisabledGlyph] = p => p.Window,
        };

        [Fact]
        public void Real_high_contrast_uses_the_live_system_colors_for_every_token()
        {
            Assert.Equal(Enum.GetValues(typeof(ThemeToken)).Length, HighContrastColumn.Count);
            Theme hc = Theme.ForHighContrast(HighContrastPalette.System);
            Assert.Equal(ThemeKind.HighContrast, hc.Kind);
            foreach (ThemeToken token in Enum.GetValues(typeof(ThemeToken)))
            {
                Color c = hc[token];
                Assert.True(c.IsSystemColor, token + " is not a system color: " + c);
                Assert.Equal(HighContrastColumn[token](HighContrastPalette.System), c);
            }
            Assert.Equal(SystemColors.ControlText, HighContrastPalette.System.ButtonText);
            Assert.Equal(SystemColors.ButtonFace, HighContrastPalette.System.ButtonFace);
            Assert.Equal(SystemColors.HotTrack, HighContrastPalette.System.HotTrack);
        }

        [Fact]
        public void Simulated_high_contrast_uses_only_its_palette()
        {
            foreach (HighContrastPalette p in new[] { HighContrastPalette.HcBlack, HighContrastPalette.HcWhite })
            {
                Theme hc = Theme.ForHighContrast(p);
                var palette = new HashSet<int>(p.Colors.Select(c => c.ToArgb()));
                Assert.Equal(8, p.Colors.Count);
                foreach (ThemeToken token in Enum.GetValues(typeof(ThemeToken)))
                {
                    Assert.Contains(hc[token].ToArgb(), palette);
                    Assert.Equal(HighContrastColumn[token](p).ToArgb(), hc[token].ToArgb());
                }
                // Secondary and hint text are WindowText, never GrayText (K8).
                Assert.Equal(p.WindowText.ToArgb(), hc.TextSecondary.ToArgb());
                Assert.Equal(p.WindowText.ToArgb(), hc.InputHint.ToArgb());
                Assert.NotEqual(p.GrayText.ToArgb(), hc.TextSecondary.ToArgb());
            }
            Assert.Equal(new[] { 0x000000, 0xFFFFFF, 0xFFFF00, 0x3FF23F, 0x1AEBFF, 0x000000, 0x000000, 0xFFFFFF },
                HighContrastPalette.HcBlack.Colors.Select(Pixels.Rgb).ToArray());
            Assert.Equal(new[] { 0xFFFFFF, 0x000000, 0x00009F, 0x600000, 0x37006E, 0xFFFFFF, 0xFFFFFF, 0x000000 },
                HighContrastPalette.HcWhite.Colors.Select(Pixels.Rgb).ToArray());
        }

        [Fact]
        public void Render_and_debug_names_pick_the_themes()
        {
            Assert.Same(Theme.Light, Theme.FromName("light"));
            Assert.Same(Theme.Dark, Theme.FromName(" Dark "));
            Assert.Equal("hc-black", Theme.FromName("hc-black")!.Name);
            Assert.Equal("hc-white", Theme.FromName("hc-white")!.Name);
            Assert.Null(Theme.FromName("blue"));
            Assert.Null(Theme.FromName(null));
        }

        // ---------------------------------------------------------------- T-A-11

        private static double Luminance(Color c)
        {
            double Channel(int v)
            {
                double s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        /// <summary>The WCAG 2 contrast ratio of two opaque colors.</summary>
        internal static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        [Fact]
        public void The_contrast_function_follows_wcag()
        {
            Assert.Equal(21.0, Contrast(Color.Black, Color.White), 2);
            Assert.Equal(1.0, Contrast(Color.Gray, Color.Gray), 3);
            // The spec's own figures: light secondary text on a selected row, the search box's bottom edge on the page.
            Assert.Equal(5.4, Contrast(Theme.Light.TextSecondary, Theme.Light.ListSelected), 1);
            Assert.Equal(3.07, Contrast(Theme.Light.InputBorderBottom, Theme.Light.WindowBackground), 2);
        }

        /// <summary>Spec 2.2's contrast table: each part against every surface it appears on, with the ratio it needs.</summary>
        private static IEnumerable<(string What, ThemeToken Part, ThemeToken[] Surfaces, double Needed)> ContrastPairs()
        {
            ThemeToken[] bars = { ThemeToken.SuccessBar, ThemeToken.CautionBar, ThemeToken.CriticalBar };
            ThemeToken[] text = new[] { ThemeToken.CardBackground, ThemeToken.WindowBackground, ThemeToken.ListSelected, ThemeToken.ListHover }.Concat(bars).ToArray();
            ThemeToken[] secondary = text.Concat(new[] { ThemeToken.InputFill, ThemeToken.InputFillHover, ThemeToken.InputFillFocused }).ToArray();
            ThemeToken[] links = { ThemeToken.CardBackground, ThemeToken.WindowBackground, ThemeToken.CautionBar, ThemeToken.SuccessBar };
            yield return ("primary text", ThemeToken.TextPrimary, text, 4.5);
            yield return ("secondary text", ThemeToken.TextSecondary, secondary, 4.5);
            yield return ("hint text", ThemeToken.InputHint, secondary, 4.5);
            yield return ("link at rest", ThemeToken.Link, links, 4.5);
            yield return ("link hover", ThemeToken.LinkHover, links, 4.5);
            yield return ("text on accent at rest", ThemeToken.TextOnAccent, new[] { ThemeToken.AccentFill }, 4.5);
            yield return ("text on accent, hover", ThemeToken.TextOnAccentHover, new[] { ThemeToken.AccentFillHover }, 4.5);
            yield return ("text on accent, pressed", ThemeToken.TextOnAccentHover, new[] { ThemeToken.AccentFillPressed }, 4.5);
            yield return ("button text at rest", ThemeToken.ButtonText, new[] { ThemeToken.ButtonFill }, 4.5);
            yield return ("button text, hover", ThemeToken.ButtonTextHover, new[] { ThemeToken.ButtonFillHover }, 4.5);
            yield return ("button text, pressed", ThemeToken.ButtonTextPressed, new[] { ThemeToken.ButtonFillPressed }, 4.5);
            yield return ("accent pill", ThemeToken.PillAccentText, new[] { ThemeToken.PillAccentFill }, 4.5);
            yield return ("neutral pill", ThemeToken.PillNeutralText, new[] { ThemeToken.PillNeutralFill, ThemeToken.PillNeutralFillSelected }, 4.5);
            yield return ("outline pill", ThemeToken.PillOutlineText, new[] { ThemeToken.CardBackground, ThemeToken.ListSelected, ThemeToken.ListHover }, 4.5);
            yield return ("success glyph", ThemeToken.SuccessGlyph, new[] { ThemeToken.SuccessBar }, 3);
            yield return ("caution glyph", ThemeToken.CautionGlyph, new[] { ThemeToken.CautionBar, ThemeToken.WindowBackground }, 3);
            yield return ("critical glyph", ThemeToken.CriticalGlyph, new[] { ThemeToken.CriticalBar, ThemeToken.CardBackground }, 3);
            yield return ("info glyph", ThemeToken.InfoGlyph, new[] { ThemeToken.CardBackground }, 3);
            yield return ("status shield on", ThemeToken.ShieldOn, new[] { ThemeToken.CardBackground }, 3);
            yield return ("status shield off", ThemeToken.ShieldOff, new[] { ThemeToken.CardBackground }, 3);
            yield return ("status shield attention", ThemeToken.ShieldAttention, new[] { ThemeToken.CardBackground }, 3);
            yield return ("shield symbol", ThemeToken.ShieldSymbol, new[] { ThemeToken.ShieldOn, ThemeToken.ShieldOff, ThemeToken.ShieldAttention }, 3);
            yield return ("search box bottom edge", ThemeToken.InputBorderBottom, new[] { ThemeToken.InputFill, ThemeToken.InputFillHover, ThemeToken.WindowBackground }, 3);
            yield return ("focused search box edge", ThemeToken.InputBorderFocused, new[] { ThemeToken.InputFillFocused, ThemeToken.WindowBackground }, 3);
            yield return ("check box border", ThemeToken.CheckBoxBorder, new[] { ThemeToken.CheckBoxFill, ThemeToken.WindowBackground }, 3);
            yield return ("selection bar", ThemeToken.SelectionBar, new[] { ThemeToken.ListSelected }, 3);
            yield return ("checked box", ThemeToken.CheckBoxCheckedFill, new[] { ThemeToken.WindowBackground, ThemeToken.CardBackground }, 3);
            yield return ("\"In use\" glyph", ThemeToken.AccentText, new[] { ThemeToken.CardBackground }, 3);
            yield return ("check mark in the box", ThemeToken.CheckBoxGlyph, new[] { ThemeToken.CheckBoxCheckedFill }, 3);
            yield return ("focus ring, outer", ThemeToken.FocusOuter, new[] { ThemeToken.WindowBackground, ThemeToken.CardBackground, ThemeToken.ListSelected }.Concat(bars).ToArray(), 3);
            yield return ("focus ring, inner", ThemeToken.FocusInner, new[] { ThemeToken.AccentFill }, 3);
        }

        [Fact]
        public void Every_contrast_pair_meets_its_ratio_in_light_and_dark()
        {
            var failures = new List<string>();
            foreach (Theme theme in new[] { Theme.Light, Theme.Dark })
            {
                foreach ((string what, ThemeToken part, ThemeToken[] surfaces, double needed) in ContrastPairs())
                {
                    foreach (ThemeToken surface in surfaces)
                    {
                        double ratio = Contrast(theme[part], theme[surface]);
                        if (ratio < needed) failures.Add(theme.Name + ": " + what + " on " + surface + " is " + ratio.ToString("0.00") + ":1, needs " + needed + ":1");
                    }
                }
            }
            Assert.Empty(failures);
        }

        // ---------------------------------------------------------------- T-A-12 (gallery samples), T-A-28

        [Fact]
        public void The_gallery_shows_only_palette_colors_in_cards_bars_buttons_pills_and_the_selected_row()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (HighContrastPalette palette in new[] { HighContrastPalette.HcBlack, HighContrastPalette.HcWhite })
                        {
                            ThemeManager.Override(Theme.ForHighContrast(palette));
                            var colors = new HashSet<int>(palette.Colors.Select(Pixels.Rgb));
                            using (var gallery = new GalleryForm(p.Context))
                            {
                                Sta.ShowOffScreen(gallery);
                                IReadOnlyList<(Control Control, Point Point, string What)> samples = gallery.SamplePoints;
                                Assert.Contains(samples, s => s.What == "selected row");
                                Assert.Contains(samples, s => s.What == "pill on a selected row");
                                Assert.Contains(samples, s => s.What.EndsWith(" bar", StringComparison.Ordinal));
                                Assert.Contains(samples, s => s.What.EndsWith(" button", StringComparison.Ordinal));
                                foreach ((Control control, Point point, string what) in samples)
                                {
                                    using (Bitmap bmp = Pixels.Of(control))
                                    {
                                        int rgb = Pixels.Rgb(bmp, point.X, point.Y);
                                        bool inPalette = colors.Contains(rgb);
                                        Assert.True(inPalette, palette.Name + ", " + what + ": #" + rgb.ToString("X6") + " is not a palette color");
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        [Fact]
        public void The_gallery_builds_and_renders_in_every_theme()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    try
                    {
                        foreach (string name in new[] { "light", "dark", "hc-black", "hc-white" })
                        {
                            ThemeManager.Override(Theme.FromName(name)!);
                            using (var gallery = new GalleryForm(p.Context))
                            {
                                Sta.ShowOffScreen(gallery);
                                using (Bitmap bmp = Pixels.Of(gallery.Root))
                                {
                                    // The page color fills the gallery's corner.
                                    Assert.Equal(Pixels.Rgb(ThemeManager.Current.WindowBackground), Pixels.Rgb(bmp, 2, 2));
                                }
                                Assert.Equal(ThemeManager.Current.WindowBackground, gallery.BackColor);
                            }
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
            }
        }

        // ---------------------------------------------------------------- T-A-13, T-A-14

        [Fact]
        public void Override_raises_the_change_once_and_windows_follow_it()
        {
            Sta.Run(() =>
            {
                int raised = 0;
                EventHandler count = (s, e) => raised++;
                ThemeManager.ThemeChanged += count;
                try
                {
                    using (var w = new TestWindow())
                    {
                        ThemedLabel label = w.Add(new ThemedLabel(w.Fonts, "Label"));
                        ThemedLabel secondary = w.Add(new ThemedLabel(w.Fonts, "Secondary", TextStyle.Body, TextRole.Secondary));
                        ThemedButton button = w.Add(new ThemedButton(w.Fonts, Strings.AboutButton));
                        var card = new CardPanel();
                        card.Content.Controls.Add(new ThemedLabel(w.Fonts, "In a card"));
                        w.Add(card);
                        Sta.ShowOffScreen(w);

                        ThemeManager.Override(Theme.Dark);
                        Assert.Equal(1, raised);
                        AssertLook(w, label, secondary, button, card, Theme.Dark);

                        // A control made after the switch has the new look too.
                        var bar = new NoticeBar(w.Fonts, NoticeSeverity.Info, "A notice made after the switch.", closable: true);
                        w.Add(bar);
                        w.PerformLayout();
                        Assert.Equal(Theme.Dark.TextPrimary, bar.MessageLabel.ForeColor);
                        using (Bitmap bmp = Pixels.Of(bar)) Assert.Equal(Pixels.Rgb(Theme.Dark.CardBackground), Pixels.Rgb(bmp, w.Fonts.Scale.Px(8), bar.Height / 2));

                        ThemeManager.Override(Theme.Light);
                        Assert.Equal(2, raised);
                        AssertLook(w, label, secondary, button, card, Theme.Light);
                        Assert.Equal(Theme.Light.TextPrimary, bar.MessageLabel.ForeColor);
                    }
                }
                finally
                {
                    ThemeManager.ThemeChanged -= count;
                    UiGlobals.Reset();
                }
            });
        }

        private static void AssertLook(TestWindow w, ThemedLabel label, ThemedLabel secondary, ThemedButton button, CardPanel card, Theme theme)
        {
            Assert.Equal(theme.WindowBackground, w.BackColor);
            Assert.Equal(theme.TextPrimary, label.ForeColor);
            Assert.Equal(theme.TextSecondary, secondary.ForeColor);
            int inside = w.Fonts.Scale.Px(6);
            using (Bitmap bmp = Pixels.Of(button)) Assert.Equal(Pixels.Rgb(theme.ButtonFill), Pixels.Rgb(bmp, inside, inside));
            using (Bitmap bmp = Pixels.Of(card)) Assert.Equal(Pixels.Rgb(theme.CardBackground), Pixels.Rgb(bmp, inside, inside));
        }

        [Fact]
        public void A_closed_window_stops_listening_for_theme_changes()
        {
            Sta.Run(() =>
            {
                int before = ThemeManager.SubscriberCount;
                var w = new TestWindow();
                Assert.Equal(before + 1, ThemeManager.SubscriberCount);
                using (var dialog = new TestDialog(1))
                {
                    Assert.Equal(before + 2, ThemeManager.SubscriberCount);
                }
                Assert.Equal(before + 1, ThemeManager.SubscriberCount);
                w.Dispose();
                Assert.Equal(before, ThemeManager.SubscriberCount);
            });
        }

        /// <summary>Pumps the calling thread's messages for about <paramref name="ms"/> milliseconds.</summary>
        private static void Pump(int ms)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        [Fact]
        public void Refresh_requests_are_debounced_into_at_most_one_change()
        {
            Sta.Run(() =>
            {
                int raised = 0;
                EventHandler count = (s, e) => raised++;
                try
                {
                    Theme windows = Theme.Light;
                    ThemeManager.StartForTests(() => windows);
                    ThemeManager.ThemeChanged += count;

                    // Nothing changed: no event.
                    ThemeManager.RequestRefresh();
                    Pump(400);
                    Assert.Equal(0, raised);

                    // Five requests within the debounce, one real change: one event.
                    windows = Theme.Dark;
                    for (int i = 0; i < 5; i++) ThemeManager.RequestRefresh();
                    Pump(400);
                    Assert.Equal(1, raised);
                    Assert.Same(Theme.Dark, ThemeManager.Current);

                    // A theme change of Windows' own (WM_THEMECHANGED) re-applies the native parts even without a new kind.
                    ThemeManager.RequestRefresh(reapplyNative: true);
                    Pump(400);
                    Assert.Equal(2, raised);
                }
                finally
                {
                    ThemeManager.ThemeChanged -= count;
                    UiGlobals.Reset();
                }
            });
        }

        [Fact]
        public void A_new_contrast_palette_is_a_change_even_with_the_same_kind()
        {
            Sta.Run(() =>
            {
                int raised = 0;
                EventHandler count = (s, e) => raised++;
                try
                {
                    // A real contrast theme keeps its kind and name ("hc"); only the colors' values change (Night sky to Desert).
                    HighContrastPalette Real(int window, int text) => new HighContrastPalette("hc", HighContrastPalette.Rgb(window), HighContrastPalette.Rgb(text),
                        HighContrastPalette.Rgb(0xFFFF00), HighContrastPalette.Rgb(0x3FF23F), HighContrastPalette.Rgb(0x1AEBFF), HighContrastPalette.Rgb(window),
                        HighContrastPalette.Rgb(window), HighContrastPalette.Rgb(text));
                    Theme windows = Theme.ForHighContrast(Real(0x000000, 0xFFFFFF));
                    ThemeManager.StartForTests(() => windows);
                    ThemeManager.ThemeChanged += count;

                    windows = Theme.ForHighContrast(Real(0x000000, 0xFFFFFF));
                    ThemeManager.RequestRefresh();
                    Pump(400);
                    Assert.Equal(0, raised);

                    windows = Theme.ForHighContrast(Real(0xFFFAEF, 0x3D3D3D));
                    ThemeManager.RequestRefresh();
                    Pump(400);
                    Assert.Equal(1, raised);
                    Assert.Equal(0xFFFAEF, Pixels.Rgb(ThemeManager.Current.WindowBackground));
                }
                finally
                {
                    ThemeManager.ThemeChanged -= count;
                    UiGlobals.Reset();
                }
            });
        }

        [Fact]
        public void The_windows_setting_handler_runs_the_refresh_on_the_ui_thread()
        {
            Sta.Run(() =>
            {
                try
                {
                    // A fresh STA thread before any control has no context; Initialize installs the WinForms one.
                    Assert.False(SynchronizationContext.Current is WindowsFormsSynchronizationContext);
                    SynchronizationContext context = ThemeManager.EnsureWinFormsContext();
                    Assert.IsType<WindowsFormsSynchronizationContext>(context);
                    Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);

                    int ui = Thread.CurrentThread.ManagedThreadId;
                    ThemeManager.StartForTests(() => Theme.Light);
                    Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
                    var seen = new List<int>();
                    ThemeManager.RefreshObserver = id => seen.Add(id);

                    // SystemEvents call their handlers on another thread.
                    int worker = -1;
                    var thread = new Thread(() =>
                    {
                        worker = Thread.CurrentThread.ManagedThreadId;
                        ThemeManager.OnUserPreferenceChanged(null, new UserPreferenceChangedEventArgs(UserPreferenceCategory.General));
                    });
                    thread.Start();
                    thread.Join();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (seen.Count == 0 && watch.ElapsedMilliseconds < 3000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert.Equal(new[] { ui }, seen);
                    Assert.NotEqual(ui, worker);

                    // A request made on another thread goes to the UI thread too.
                    seen.Clear();
                    var other = new Thread(() => ThemeManager.RequestRefresh());
                    other.Start();
                    other.Join();
                    watch.Restart();
                    while (seen.Count == 0 && watch.ElapsedMilliseconds < 3000)
                    {
                        Application.DoEvents();
                        Thread.Sleep(10);
                    }
                    Assert.Equal(new[] { ui }, seen);
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        [Fact]
        public void Without_initialize_nothing_is_detected_and_the_theme_stays_light()
        {
            Sta.Run(() =>
            {
                try
                {
                    UiGlobals.Reset();
                    ThemeManager.RequestRefresh(reapplyNative: true);
                    Pump(300);
                    Assert.Same(Theme.Light, ThemeManager.Current);
                }
                finally
                {
                    UiGlobals.Reset();
                }
            });
        }

        // ---------------------------------------------------------------- T-A-30

#if !DEBUG
        [Fact]
        public void Release_builds_ignore_the_debug_theme_and_dpi_variables()
        {
            string? theme = Environment.GetEnvironmentVariable("QUIETHOSTS_THEME");
            string? dpi = Environment.GetEnvironmentVariable("QUIETHOSTS_RENDER_DPI");
            try
            {
                Environment.SetEnvironmentVariable("QUIETHOSTS_RENDER_DPI", "144");
                UiScale.ResetForTests();
                Assert.Equal(UiScale.SystemDpi, UiScale.Current.Dpi);
                Assert.Equal(1f, UiScale.Current.FontFactor);

                Environment.SetEnvironmentVariable("QUIETHOSTS_THEME", "dark");
                Assert.Equal(ThemeResolver.SystemKind(), ThemeManager.Detect().Kind);
                // A name only the variable could give: never taken.
                Environment.SetEnvironmentVariable("QUIETHOSTS_THEME", "hc-white");
                Assert.NotEqual("hc-white", ThemeManager.Detect().Name);
                Assert.Equal(ThemeResolver.SystemKind(), ThemeManager.Detect().Kind);
            }
            finally
            {
                Environment.SetEnvironmentVariable("QUIETHOSTS_THEME", theme);
                Environment.SetEnvironmentVariable("QUIETHOSTS_RENDER_DPI", dpi);
                UiGlobals.Reset();
            }
        }
#endif
    }
}
