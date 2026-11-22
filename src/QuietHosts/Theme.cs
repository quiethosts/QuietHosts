using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuietHosts
{
    /// <summary>The three looks: light, dark (Windows 11 only) and high contrast (always wins).</summary>
    internal enum ThemeKind
    {
        Light,
        Dark,
        HighContrast,
    }

    /// <summary>
    /// Every color the app paints. Controls never hold color constants of their own: they ask the current
    /// <see cref="Theme"/> for one of these. The names are the spec's token names; the ones after
    /// <see cref="ProgressRing"/> are state colors the spec's control tables give per state.
    /// </summary>
    internal enum ThemeToken
    {
        WindowBackground,
        CardBackground,
        CardBorder,
        Divider,
        TextPrimary,
        TextSecondary,
        TextDisabled,
        Link,
        LinkHover,
        AccentFill,
        AccentFillHover,
        AccentFillPressed,
        TextOnAccent,
        AccentText,
        ButtonFill,
        ButtonFillHover,
        ButtonFillPressed,
        ButtonText,
        ButtonTextPressed,
        ButtonBorder,
        ButtonBorderBottom,
        DisabledFill,
        DisabledBorder,
        SubtleHover,
        SubtlePressed,
        SuccessGlyph,
        SuccessBar,
        CautionGlyph,
        CautionBar,
        CriticalGlyph,
        CriticalBar,
        InfoGlyph,
        ShieldOn,
        ShieldOff,
        ShieldAttention,
        ShieldSymbol,
        ListHover,
        ListSelected,
        SelectionBar,
        PillAccentFill,
        PillAccentText,
        PillNeutralFill,
        PillNeutralText,
        PillNeutralFillSelected,
        PillOutlineBorder,
        PillOutlineText,
        InputFill,
        InputFillHover,
        InputFillFocused,
        InputBorder,
        InputBorderBottom,
        InputBorderFocused,
        InputHint,
        CheckBoxFill,
        CheckBoxFillHover,
        CheckBoxBorder,
        CheckBoxDisabledBorder,
        FocusOuter,
        FocusInner,
        ProgressRing,

        // State colors from the control tables (2.8.3, 2.8.12, 2.8.14): what HC paints on hover, press and selection.
        /// <summary>Text on an accent button while hovered or pressed (HC: HighlightText).</summary>
        TextOnAccentHover,
        /// <summary>Standard button text while hovered (HC: HighlightText).</summary>
        ButtonTextHover,
        /// <summary>Standard button border while hovered (HC: Highlight).</summary>
        ButtonBorderHover,
        /// <summary>A subtle (glyph-only) button's glyph while hovered (HC: HighlightText).</summary>
        SubtleGlyphHover,
        /// <summary>A subtle button's glyph while pressed (HC: HighlightText).</summary>
        SubtleGlyphPressed,
        /// <summary>Primary text on a hovered or selected list row (HC: HighlightText).</summary>
        ListTextSelected,
        /// <summary>Secondary text on a hovered or selected list row (HC: HighlightText).</summary>
        ListSecondaryTextSelected,
        /// <summary>The unchecked box's border while hovered (HC: Highlight).</summary>
        CheckBoxBorderHover,
        /// <summary>The checked box (HC: Highlight).</summary>
        CheckBoxCheckedFill,
        CheckBoxCheckedFillHover,
        /// <summary>The check mark in a checked box (HC: HighlightText).</summary>
        CheckBoxGlyph,
        /// <summary>A disabled box (HC: Window).</summary>
        CheckBoxDisabledFill,
        /// <summary>A disabled checked box (HC: GrayText).</summary>
        CheckBoxDisabledCheckedFill,
        /// <summary>The check mark in a disabled checked box (HC: Window).</summary>
        CheckBoxDisabledGlyph,
    }

    /// <summary>
    /// The eight system colors high contrast uses. <see cref="System"/> returns the live <see cref="SystemColors"/>
    /// objects (they resolve when painted, so they follow a contrast-theme change at once); the two simulated palettes
    /// (render and tests only) are the classic "High Contrast Black" and "High Contrast White".
    /// </summary>
    internal sealed class HighContrastPalette
    {
        public HighContrastPalette(string name, Color window, Color windowText, Color hotTrack, Color grayText, Color highlight,
            Color highlightText, Color buttonFace, Color buttonText)
        {
            Name = name;
            Window = window;
            WindowText = windowText;
            HotTrack = hotTrack;
            GrayText = grayText;
            Highlight = highlight;
            HighlightText = highlightText;
            ButtonFace = buttonFace;
            ButtonText = buttonText;
        }

        public string Name { get; }
        public Color Window { get; }
        public Color WindowText { get; }
        public Color HotTrack { get; }
        public Color GrayText { get; }
        public Color Highlight { get; }
        public Color HighlightText { get; }
        public Color ButtonFace { get; }
        public Color ButtonText { get; }

        /// <summary>The eight colors, in the order of the spec's table.</summary>
        public IReadOnlyList<Color> Colors => new[] { Window, WindowText, HotTrack, GrayText, Highlight, HighlightText, ButtonFace, ButtonText };

        /// <summary>The colors' current ARGB values (for the system palette: what Windows uses right now).</summary>
        public int[] Snapshot()
        {
            IReadOnlyList<Color> c = Colors;
            var values = new int[c.Count];
            for (int i = 0; i < values.Length; i++) values[i] = c[i].ToArgb();
            return values;
        }

        /// <summary>The live system colors (a real contrast theme).</summary>
        public static HighContrastPalette System { get; } = new HighContrastPalette("hc",
            SystemColors.Window, SystemColors.WindowText, SystemColors.HotTrack, SystemColors.GrayText,
            SystemColors.Highlight, SystemColors.HighlightText, SystemColors.ButtonFace, SystemColors.ControlText);

        public static HighContrastPalette HcBlack { get; } = new HighContrastPalette("hc-black",
            Rgb(0x000000), Rgb(0xFFFFFF), Rgb(0xFFFF00), Rgb(0x3FF23F), Rgb(0x1AEBFF), Rgb(0x000000), Rgb(0x000000), Rgb(0xFFFFFF));

        public static HighContrastPalette HcWhite { get; } = new HighContrastPalette("hc-white",
            Rgb(0xFFFFFF), Rgb(0x000000), Rgb(0x00009F), Rgb(0x600000), Rgb(0x37006E), Rgb(0xFFFFFF), Rgb(0xFFFFFF), Rgb(0x000000));

        internal static Color Rgb(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    /// <summary>
    /// One set of colors and theme numbers (spec 2.2). <see cref="Light"/> and <see cref="Dark"/> are fixed;
    /// <see cref="ForHighContrast"/> builds the high-contrast set from a palette.
    /// </summary>
    internal sealed class Theme
    {
        private static readonly int TokenCount = Enum.GetValues(typeof(ThemeToken)).Length;
        private readonly Color[] _colors;

        private Theme(ThemeKind kind, string name, Color[] colors, HighContrastPalette? palette)
        {
            Kind = kind;
            Name = name;
            _colors = colors;
            Palette = palette;
        }

        public ThemeKind Kind { get; }
        /// <summary>"light", "dark", "hc" (a real contrast theme), "hc-black" or "hc-white".</summary>
        public string Name { get; }
        /// <summary>The palette a high-contrast theme was built from; null otherwise.</summary>
        public HighContrastPalette? Palette { get; }
        public bool IsHighContrast => Kind == ThemeKind.HighContrast;

        public Color this[ThemeToken token] => _colors[(int)token];

        // ---------------------------------------------------------------- numbers (2.2)

        /// <summary>Epx; 1 in every theme.</summary>
        public int CardBorderWidth => 1;
        /// <summary>Epx, drawn in WindowText inside the client edge of every dialog: 2 in high contrast, else 0.</summary>
        public int DialogFrameWidth => IsHighContrast ? 2 : 0;
        /// <summary>Epx border of accent buttons: 2 in high contrast (ButtonText), else 0.</summary>
        public int AccentBorderWidth => IsHighContrast ? 2 : 0;
        public bool LinksUnderlinedAtRest => IsHighContrast;
        /// <summary>The value for DWM attribute 20 (immersive dark mode).</summary>
        public bool UseDarkFrame => Kind == ThemeKind.Dark;
        /// <summary>The theme name for scroll bars, the list and tooltips (undocumented; Microsoft's WinForms uses it).</summary>
        public string? NativeThemeName => Kind == ThemeKind.Dark ? "DarkMode_Explorer" : null;
        /// <summary>The theme name for the list header.</summary>
        public string? HeaderThemeName => Kind == ThemeKind.Dark ? "DarkMode_ItemsView" : null;

        // ---------------------------------------------------------------- colors

        public Color WindowBackground => this[ThemeToken.WindowBackground];
        public Color CardBackground => this[ThemeToken.CardBackground];
        public Color CardBorder => this[ThemeToken.CardBorder];
        public Color Divider => this[ThemeToken.Divider];
        public Color TextPrimary => this[ThemeToken.TextPrimary];
        public Color TextSecondary => this[ThemeToken.TextSecondary];
        public Color TextDisabled => this[ThemeToken.TextDisabled];
        public Color Link => this[ThemeToken.Link];
        public Color LinkHover => this[ThemeToken.LinkHover];
        public Color AccentFill => this[ThemeToken.AccentFill];
        public Color AccentFillHover => this[ThemeToken.AccentFillHover];
        public Color AccentFillPressed => this[ThemeToken.AccentFillPressed];
        public Color TextOnAccent => this[ThemeToken.TextOnAccent];
        public Color AccentText => this[ThemeToken.AccentText];
        public Color ButtonFill => this[ThemeToken.ButtonFill];
        public Color ButtonFillHover => this[ThemeToken.ButtonFillHover];
        public Color ButtonFillPressed => this[ThemeToken.ButtonFillPressed];
        public Color ButtonText => this[ThemeToken.ButtonText];
        public Color ButtonTextPressed => this[ThemeToken.ButtonTextPressed];
        public Color ButtonBorder => this[ThemeToken.ButtonBorder];
        public Color ButtonBorderBottom => this[ThemeToken.ButtonBorderBottom];
        public Color DisabledFill => this[ThemeToken.DisabledFill];
        public Color DisabledBorder => this[ThemeToken.DisabledBorder];
        public Color SubtleHover => this[ThemeToken.SubtleHover];
        public Color SubtlePressed => this[ThemeToken.SubtlePressed];
        public Color SuccessGlyph => this[ThemeToken.SuccessGlyph];
        public Color SuccessBar => this[ThemeToken.SuccessBar];
        public Color CautionGlyph => this[ThemeToken.CautionGlyph];
        public Color CautionBar => this[ThemeToken.CautionBar];
        public Color CriticalGlyph => this[ThemeToken.CriticalGlyph];
        public Color CriticalBar => this[ThemeToken.CriticalBar];
        public Color InfoGlyph => this[ThemeToken.InfoGlyph];
        public Color ShieldOn => this[ThemeToken.ShieldOn];
        public Color ShieldOff => this[ThemeToken.ShieldOff];
        public Color ShieldAttention => this[ThemeToken.ShieldAttention];
        public Color ShieldSymbol => this[ThemeToken.ShieldSymbol];
        public Color ListHover => this[ThemeToken.ListHover];
        public Color ListSelected => this[ThemeToken.ListSelected];
        public Color SelectionBar => this[ThemeToken.SelectionBar];
        public Color PillAccentFill => this[ThemeToken.PillAccentFill];
        public Color PillAccentText => this[ThemeToken.PillAccentText];
        public Color PillNeutralFill => this[ThemeToken.PillNeutralFill];
        public Color PillNeutralText => this[ThemeToken.PillNeutralText];
        public Color PillNeutralFillSelected => this[ThemeToken.PillNeutralFillSelected];
        public Color PillOutlineBorder => this[ThemeToken.PillOutlineBorder];
        public Color PillOutlineText => this[ThemeToken.PillOutlineText];
        public Color InputFill => this[ThemeToken.InputFill];
        public Color InputFillHover => this[ThemeToken.InputFillHover];
        public Color InputFillFocused => this[ThemeToken.InputFillFocused];
        public Color InputBorder => this[ThemeToken.InputBorder];
        public Color InputBorderBottom => this[ThemeToken.InputBorderBottom];
        public Color InputBorderFocused => this[ThemeToken.InputBorderFocused];
        public Color InputHint => this[ThemeToken.InputHint];
        public Color CheckBoxFill => this[ThemeToken.CheckBoxFill];
        public Color CheckBoxFillHover => this[ThemeToken.CheckBoxFillHover];
        public Color CheckBoxBorder => this[ThemeToken.CheckBoxBorder];
        public Color CheckBoxDisabledBorder => this[ThemeToken.CheckBoxDisabledBorder];
        public Color FocusOuter => this[ThemeToken.FocusOuter];
        public Color FocusInner => this[ThemeToken.FocusInner];
        public Color ProgressRing => this[ThemeToken.ProgressRing];
        public Color TextOnAccentHover => this[ThemeToken.TextOnAccentHover];
        public Color ButtonTextHover => this[ThemeToken.ButtonTextHover];
        public Color ButtonBorderHover => this[ThemeToken.ButtonBorderHover];
        public Color SubtleGlyphHover => this[ThemeToken.SubtleGlyphHover];
        public Color SubtleGlyphPressed => this[ThemeToken.SubtleGlyphPressed];
        public Color ListTextSelected => this[ThemeToken.ListTextSelected];
        public Color ListSecondaryTextSelected => this[ThemeToken.ListSecondaryTextSelected];
        public Color CheckBoxBorderHover => this[ThemeToken.CheckBoxBorderHover];
        public Color CheckBoxCheckedFill => this[ThemeToken.CheckBoxCheckedFill];
        public Color CheckBoxCheckedFillHover => this[ThemeToken.CheckBoxCheckedFillHover];
        public Color CheckBoxGlyph => this[ThemeToken.CheckBoxGlyph];
        public Color CheckBoxDisabledFill => this[ThemeToken.CheckBoxDisabledFill];
        public Color CheckBoxDisabledCheckedFill => this[ThemeToken.CheckBoxDisabledCheckedFill];
        public Color CheckBoxDisabledGlyph => this[ThemeToken.CheckBoxDisabledGlyph];

        // ---------------------------------------------------------------- the sets

        /// <summary>The light set: WinUI 3 light tokens composited over their surfaces.</summary>
        public static Theme Light { get; } = Build(ThemeKind.Light, "light", new Dictionary<ThemeToken, int>
        {
            [ThemeToken.WindowBackground] = 0xF3F3F3,
            [ThemeToken.CardBackground] = 0xFBFBFB,
            [ThemeToken.CardBorder] = 0xE5E5E5,
            [ThemeToken.Divider] = 0xE5E5E5,
            [ThemeToken.TextPrimary] = 0x1A1A1A,
            [ThemeToken.TextSecondary] = 0x5F5F5F,
            [ThemeToken.TextDisabled] = 0xA0A0A0,
            [ThemeToken.Link] = 0x005FB8,
            [ThemeToken.LinkHover] = 0x003E92,
            [ThemeToken.AccentFill] = 0x005FB8,
            [ThemeToken.AccentFillHover] = 0x196EBF,
            [ThemeToken.AccentFillPressed] = 0x004C93,
            [ThemeToken.TextOnAccent] = 0xFFFFFF,
            [ThemeToken.AccentText] = 0x005FB8,
            [ThemeToken.ButtonFill] = 0xFEFEFE,
            [ThemeToken.ButtonFillHover] = 0xFAFAFA,
            [ThemeToken.ButtonFillPressed] = 0xF5F5F5,
            [ThemeToken.ButtonText] = 0x1A1A1A,
            [ThemeToken.ButtonTextPressed] = 0x5F5F5F,
            [ThemeToken.ButtonBorder] = 0xE5E5E5,
            [ThemeToken.ButtonBorderBottom] = 0xD3D3D3,
            [ThemeToken.DisabledFill] = 0xF9F9F9,
            [ThemeToken.DisabledBorder] = 0xE5E5E5,
            [ThemeToken.SuccessGlyph] = 0x0F7B0F,
            [ThemeToken.SuccessBar] = 0xDFF6DD,
            [ThemeToken.CautionGlyph] = 0x9D5D00,
            [ThemeToken.CautionBar] = 0xFFF4CE,
            [ThemeToken.CriticalGlyph] = 0xC42B1C,
            [ThemeToken.CriticalBar] = 0xFDE7E9,
            [ThemeToken.InfoGlyph] = 0x005FB8,
            [ThemeToken.ShieldOn] = 0x0F7B0F,
            [ThemeToken.ShieldOff] = 0x6E6E6E,
            [ThemeToken.ShieldAttention] = 0x9D5D00,
            [ThemeToken.ShieldSymbol] = 0xFFFFFF,
            [ThemeToken.ListHover] = 0xF2F2F2,
            [ThemeToken.ListSelected] = 0xECECEC,
            [ThemeToken.SelectionBar] = 0x005FB8,
            [ThemeToken.PillAccentFill] = 0xE1EBF4,
            [ThemeToken.PillAccentText] = 0x005FB8,
            [ThemeToken.PillNeutralFill] = 0xECECEC,
            [ThemeToken.PillNeutralText] = 0x1A1A1A,
            [ThemeToken.PillNeutralFillSelected] = 0xDCDCDC,
            [ThemeToken.PillOutlineBorder] = 0x8B8B8B,
            [ThemeToken.PillOutlineText] = 0x5F5F5F,
            [ThemeToken.InputFill] = 0xFBFBFB,
            [ThemeToken.InputFillHover] = 0xF6F6F6,
            [ThemeToken.InputFillFocused] = 0xFFFFFF,
            [ThemeToken.InputBorder] = 0xE5E5E5,
            [ThemeToken.InputBorderBottom] = 0x8B8B8B,
            [ThemeToken.InputBorderFocused] = 0x005FB8,
            [ThemeToken.InputHint] = 0x5F5F5F,
            [ThemeToken.CheckBoxFill] = 0xEDEDED,
            [ThemeToken.CheckBoxFillHover] = 0xE4E4E4,
            [ThemeToken.CheckBoxBorder] = 0x868686,
            [ThemeToken.CheckBoxDisabledBorder] = 0xBFBFBF,
            [ThemeToken.FocusOuter] = 0x1A1A1A,
            [ThemeToken.FocusInner] = 0xFFFFFF,
            [ThemeToken.ProgressRing] = 0x005FB8,
            [ThemeToken.TextOnAccentHover] = 0xFFFFFF,
            [ThemeToken.ButtonTextHover] = 0x1A1A1A,
            [ThemeToken.ButtonBorderHover] = 0xE5E5E5,
            [ThemeToken.SubtleGlyphHover] = 0x1A1A1A,
            [ThemeToken.SubtleGlyphPressed] = 0x5F5F5F,
            [ThemeToken.ListTextSelected] = 0x1A1A1A,
            [ThemeToken.ListSecondaryTextSelected] = 0x5F5F5F,
            [ThemeToken.CheckBoxBorderHover] = 0x868686,
            [ThemeToken.CheckBoxCheckedFill] = 0x005FB8,
            [ThemeToken.CheckBoxCheckedFillHover] = 0x196EBF,
            [ThemeToken.CheckBoxGlyph] = 0xFFFFFF,
            [ThemeToken.CheckBoxDisabledFill] = 0xF9F9F9,
            [ThemeToken.CheckBoxDisabledCheckedFill] = 0xBFBFBF,
            [ThemeToken.CheckBoxDisabledGlyph] = 0xFFFFFF,
        }, subtleHover: Color.FromArgb(0x0A, 0, 0, 0), subtlePressed: Color.FromArgb(0x06, 0, 0, 0));

        /// <summary>The dark set (Windows 11 only): WinUI 3 dark tokens composited over their surfaces.</summary>
        public static Theme Dark { get; } = Build(ThemeKind.Dark, "dark", new Dictionary<ThemeToken, int>
        {
            [ThemeToken.WindowBackground] = 0x202020,
            [ThemeToken.CardBackground] = 0x2B2B2B,
            [ThemeToken.CardBorder] = 0x1D1D1D,
            [ThemeToken.Divider] = 0x323232,
            [ThemeToken.TextPrimary] = 0xFFFFFF,
            [ThemeToken.TextSecondary] = 0xCFCFCF,
            [ThemeToken.TextDisabled] = 0x7E7E7E,
            [ThemeToken.Link] = 0x60CDFF,
            [ThemeToken.LinkHover] = 0x99EBFF,
            [ThemeToken.AccentFill] = 0x60CDFF,
            [ThemeToken.AccentFillHover] = 0x5BBDEA,
            [ThemeToken.AccentFillPressed] = 0x55ADD5,
            [ThemeToken.TextOnAccent] = 0x000000,
            [ThemeToken.AccentText] = 0x60CDFF,
            [ThemeToken.ButtonFill] = 0x373737,
            [ThemeToken.ButtonFillHover] = 0x3C3C3C,
            [ThemeToken.ButtonFillPressed] = 0x323232,
            [ThemeToken.ButtonText] = 0xFFFFFF,
            [ThemeToken.ButtonTextPressed] = 0xCFCFCF,
            [ThemeToken.ButtonBorder] = 0x3F3F3F,
            [ThemeToken.ButtonBorderBottom] = 0x3F3F3F,
            [ThemeToken.DisabledFill] = 0x343434,
            [ThemeToken.DisabledBorder] = 0x424242,
            [ThemeToken.SuccessGlyph] = 0x6CCB5F,
            [ThemeToken.SuccessBar] = 0x393D1B,
            [ThemeToken.CautionGlyph] = 0xFCE100,
            [ThemeToken.CautionBar] = 0x433519,
            [ThemeToken.CriticalGlyph] = 0xFF99A4,
            [ThemeToken.CriticalBar] = 0x442726,
            [ThemeToken.InfoGlyph] = 0x60CDFF,
            [ThemeToken.ShieldOn] = 0x6CCB5F,
            [ThemeToken.ShieldOff] = 0x9F9F9F,
            [ThemeToken.ShieldAttention] = 0xFCE100,
            [ThemeToken.ShieldSymbol] = 0x000000,
            [ThemeToken.ListHover] = 0x373737,
            [ThemeToken.ListSelected] = 0x3C3C3C,
            [ThemeToken.SelectionBar] = 0x60CDFF,
            [ThemeToken.PillAccentFill] = 0x31434B,
            [ThemeToken.PillAccentText] = 0x60CDFF,
            [ThemeToken.PillNeutralFill] = 0x454545,
            [ThemeToken.PillNeutralText] = 0xFFFFFF,
            [ThemeToken.PillNeutralFillSelected] = 0x505050,
            [ThemeToken.PillOutlineBorder] = 0x9F9F9F,
            [ThemeToken.PillOutlineText] = 0xCFCFCF,
            [ThemeToken.InputFill] = 0x2D2D2D,
            [ThemeToken.InputFillHover] = 0x323232,
            [ThemeToken.InputFillFocused] = 0x1F1F1F,
            [ThemeToken.InputBorder] = 0x3C3C3C,
            [ThemeToken.InputBorderBottom] = 0x9A9A9A,
            [ThemeToken.InputBorderFocused] = 0x60CDFF,
            [ThemeToken.InputHint] = 0xCFCFCF,
            [ThemeToken.CheckBoxFill] = 0x1D1D1D,
            [ThemeToken.CheckBoxFillHover] = 0x2A2A2A,
            [ThemeToken.CheckBoxBorder] = 0x9A9A9A,
            [ThemeToken.CheckBoxDisabledBorder] = 0x434343,
            [ThemeToken.FocusOuter] = 0xFFFFFF,
            [ThemeToken.FocusInner] = 0x000000,
            [ThemeToken.ProgressRing] = 0x60CDFF,
            [ThemeToken.TextOnAccentHover] = 0x000000,
            [ThemeToken.ButtonTextHover] = 0xFFFFFF,
            [ThemeToken.ButtonBorderHover] = 0x3F3F3F,
            [ThemeToken.SubtleGlyphHover] = 0xFFFFFF,
            [ThemeToken.SubtleGlyphPressed] = 0xCFCFCF,
            [ThemeToken.ListTextSelected] = 0xFFFFFF,
            [ThemeToken.ListSecondaryTextSelected] = 0xCFCFCF,
            [ThemeToken.CheckBoxBorderHover] = 0x9A9A9A,
            [ThemeToken.CheckBoxCheckedFill] = 0x60CDFF,
            [ThemeToken.CheckBoxCheckedFillHover] = 0x5BBDEA,
            [ThemeToken.CheckBoxGlyph] = 0x000000,
            [ThemeToken.CheckBoxDisabledFill] = 0x343434,
            [ThemeToken.CheckBoxDisabledCheckedFill] = 0x434343,
            [ThemeToken.CheckBoxDisabledGlyph] = 0xA7A7A7,
        }, subtleHover: Color.FromArgb(0x0F, 255, 255, 255), subtlePressed: Color.FromArgb(0x0A, 255, 255, 255));

        private static Theme Build(ThemeKind kind, string name, Dictionary<ThemeToken, int> rgb, Color subtleHover, Color subtlePressed)
        {
            var colors = new Color[TokenCount];
            var set = new bool[TokenCount];
            foreach (KeyValuePair<ThemeToken, int> kv in rgb)
            {
                colors[(int)kv.Key] = HighContrastPalette.Rgb(kv.Value);
                set[(int)kv.Key] = true;
            }
            colors[(int)ThemeToken.SubtleHover] = subtleHover;
            colors[(int)ThemeToken.SubtlePressed] = subtlePressed;
            set[(int)ThemeToken.SubtleHover] = set[(int)ThemeToken.SubtlePressed] = true;
            for (int i = 0; i < set.Length; i++)
            {
                if (!set[i]) throw new InvalidOperationException("The " + name + " theme has no value for " + (ThemeToken)i + ".");
            }
            return new Theme(kind, name, colors, null);
        }

        /// <summary>
        /// The high-contrast set: every token is one of the palette's eight colors (spec 2.2's HC column). With
        /// <see cref="HighContrastPalette.System"/> these are the SystemColors objects themselves.
        /// </summary>
        public static Theme ForHighContrast(HighContrastPalette p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var c = new Color[TokenCount];
            void Set(ThemeToken t, Color value) => c[(int)t] = value;
            Set(ThemeToken.WindowBackground, p.Window);
            Set(ThemeToken.CardBackground, p.Window);
            Set(ThemeToken.CardBorder, p.WindowText);
            Set(ThemeToken.Divider, p.WindowText);
            Set(ThemeToken.TextPrimary, p.WindowText);
            Set(ThemeToken.TextSecondary, p.WindowText);
            Set(ThemeToken.TextDisabled, p.GrayText);
            Set(ThemeToken.Link, p.HotTrack);
            Set(ThemeToken.LinkHover, p.HotTrack);
            Set(ThemeToken.AccentFill, p.ButtonFace);
            Set(ThemeToken.AccentFillHover, p.Highlight);
            Set(ThemeToken.AccentFillPressed, p.Highlight);
            Set(ThemeToken.TextOnAccent, p.ButtonText);
            Set(ThemeToken.AccentText, p.WindowText);
            Set(ThemeToken.ButtonFill, p.ButtonFace);
            Set(ThemeToken.ButtonFillHover, p.Highlight);
            Set(ThemeToken.ButtonFillPressed, p.Highlight);
            Set(ThemeToken.ButtonText, p.ButtonText);
            Set(ThemeToken.ButtonTextPressed, p.HighlightText);
            Set(ThemeToken.ButtonBorder, p.ButtonText);
            Set(ThemeToken.ButtonBorderBottom, p.ButtonText);
            Set(ThemeToken.DisabledFill, p.ButtonFace);
            Set(ThemeToken.DisabledBorder, p.GrayText);
            Set(ThemeToken.SubtleHover, p.Highlight);
            Set(ThemeToken.SubtlePressed, p.Highlight);
            Set(ThemeToken.SuccessGlyph, p.WindowText);
            Set(ThemeToken.SuccessBar, p.Window);
            Set(ThemeToken.CautionGlyph, p.WindowText);
            Set(ThemeToken.CautionBar, p.Window);
            Set(ThemeToken.CriticalGlyph, p.WindowText);
            Set(ThemeToken.CriticalBar, p.Window);
            Set(ThemeToken.InfoGlyph, p.WindowText);
            Set(ThemeToken.ShieldOn, p.WindowText);
            Set(ThemeToken.ShieldOff, p.WindowText);
            Set(ThemeToken.ShieldAttention, p.WindowText);
            Set(ThemeToken.ShieldSymbol, p.Window);
            Set(ThemeToken.ListHover, p.Highlight);
            Set(ThemeToken.ListSelected, p.Highlight);
            Set(ThemeToken.SelectionBar, p.HighlightText);
            // Pills have no fill in high contrast (only a WindowText border); the fill values stand for "none".
            Set(ThemeToken.PillAccentFill, p.Window);
            Set(ThemeToken.PillAccentText, p.WindowText);
            Set(ThemeToken.PillNeutralFill, p.Window);
            Set(ThemeToken.PillNeutralText, p.WindowText);
            Set(ThemeToken.PillNeutralFillSelected, p.Highlight);
            Set(ThemeToken.PillOutlineBorder, p.WindowText);
            Set(ThemeToken.PillOutlineText, p.WindowText);
            Set(ThemeToken.InputFill, p.Window);
            Set(ThemeToken.InputFillHover, p.Window);
            Set(ThemeToken.InputFillFocused, p.Window);
            Set(ThemeToken.InputBorder, p.WindowText);
            Set(ThemeToken.InputBorderBottom, p.WindowText);
            Set(ThemeToken.InputBorderFocused, p.Highlight);
            Set(ThemeToken.InputHint, p.WindowText);
            Set(ThemeToken.CheckBoxFill, p.Window);
            Set(ThemeToken.CheckBoxFillHover, p.Window);
            Set(ThemeToken.CheckBoxBorder, p.WindowText);
            Set(ThemeToken.CheckBoxDisabledBorder, p.GrayText);
            Set(ThemeToken.FocusOuter, p.WindowText);
            Set(ThemeToken.FocusInner, p.Window);
            Set(ThemeToken.ProgressRing, p.Highlight);
            Set(ThemeToken.TextOnAccentHover, p.HighlightText);
            Set(ThemeToken.ButtonTextHover, p.HighlightText);
            Set(ThemeToken.ButtonBorderHover, p.Highlight);
            Set(ThemeToken.SubtleGlyphHover, p.HighlightText);
            Set(ThemeToken.SubtleGlyphPressed, p.HighlightText);
            Set(ThemeToken.ListTextSelected, p.HighlightText);
            Set(ThemeToken.ListSecondaryTextSelected, p.HighlightText);
            Set(ThemeToken.CheckBoxBorderHover, p.Highlight);
            Set(ThemeToken.CheckBoxCheckedFill, p.Highlight);
            Set(ThemeToken.CheckBoxCheckedFillHover, p.Highlight);
            Set(ThemeToken.CheckBoxGlyph, p.HighlightText);
            Set(ThemeToken.CheckBoxDisabledFill, p.Window);
            Set(ThemeToken.CheckBoxDisabledCheckedFill, p.GrayText);
            Set(ThemeToken.CheckBoxDisabledGlyph, p.Window);
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i].IsEmpty) throw new InvalidOperationException("The high-contrast theme has no value for " + (ThemeToken)i + ".");
            }
            return new Theme(ThemeKind.HighContrast, p.Name, c, p);
        }

        /// <summary>A theme by its render or debug name: light, dark, hc-black, hc-white; null for anything else.</summary>
        public static Theme? FromName(string? name)
        {
            switch ((name ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "light": return Light;
                case "dark": return Dark;
                case "hc-black": return ForHighContrast(HighContrastPalette.HcBlack);
                case "hc-white": return ForHighContrast(HighContrastPalette.HcWhite);
                default: return null;
            }
        }
    }

    /// <summary>Which theme applies (spec 2.1): high contrast first, then dark on Windows 11, else light.</summary>
    internal static class ThemeResolver
    {
        public const int FirstWindows11Build = 22000;
        public const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        public const string AppsUseLightThemeValue = "AppsUseLightTheme";

        /// <param name="appsUseLightTheme">The registry value as read: only the DWORD 0 means dark.</param>
        public static ThemeKind Resolve(bool highContrast, int osBuild, object? appsUseLightTheme)
        {
            if (highContrast) return ThemeKind.HighContrast;
            if (osBuild >= FirstWindows11Build && appsUseLightTheme is int value && value == 0) return ThemeKind.Dark;
            return ThemeKind.Light;
        }

        /// <summary>HKCU\...\Themes\Personalize\AppsUseLightTheme, or null when missing or unreadable. Read-only.</summary>
        public static object? ReadAppsUseLightTheme()
        {
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, false))
                {
                    return key?.GetValue(AppsUseLightThemeValue);
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return null;
            }
        }

        /// <summary>What Windows says right now.</summary>
        public static ThemeKind SystemKind()
        {
            bool highContrast;
            try { highContrast = SystemInformation.HighContrast; }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { highContrast = false; }
            return Resolve(highContrast, NativeTheme.OsBuild, ReadAppsUseLightTheme());
        }
    }

    /// <summary>
    /// The current theme, and the one event every window repaints on (spec 2.4). UI thread only. In tests and
    /// --render-ui nothing is detected: <see cref="Current"/> stays Light until <see cref="Override"/>.
    /// </summary>
    internal static class ThemeManager
    {
        private const int DebounceMs = 150;
        private static Theme _current = Theme.Light;
        private static System.Windows.Forms.Timer? _timer;
        private static SynchronizationContext? _context;
        private static int _uiThread;
        private static bool _pendingNative;
        private static int[]? _snapshot;
        private static Func<Theme>? _detect;
        private static bool _subscribed;

        public static Theme Current => _current;

        /// <summary>Raised on the UI thread, at most once per real change.</summary>
        public static event EventHandler? ThemeChanged;

        /// <summary>
        /// Program.RunWindow only, before the first window: resolves the theme, subscribes to Windows' setting changes and
        /// creates the debounce timer.
        /// </summary>
        public static void Initialize()
        {
            if (_timer != null) return;
            Start(Detect);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            Application.ApplicationExit += OnApplicationExit;
            _subscribed = true;
        }

        private static void Start(Func<Theme> detect)
        {
            _context = EnsureWinFormsContext();
            _uiThread = Thread.CurrentThread.ManagedThreadId;
            _detect = detect;
            SetCurrent(detect());
            _timer = new System.Windows.Forms.Timer { Interval = DebounceMs };
            _timer.Tick += OnTick;
        }

        /// <summary>
        /// Installs a WinForms synchronization context on the calling (UI) thread if it has none, and returns it. Before the
        /// first control exists the thread has none, and the first SystemEvents subscription would otherwise install a plain
        /// one whose Post runs on a thread-pool thread.
        /// </summary>
        internal static SynchronizationContext EnsureWinFormsContext()
        {
            if (!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            return SynchronizationContext.Current!;
        }

        /// <summary>The theme Windows asks for now (spec 2.1); in Debug builds QUIETHOSTS_THEME can force one.</summary>
        internal static Theme Detect()
        {
#if DEBUG
            Theme? forced = Theme.FromName(Environment.GetEnvironmentVariable(ForcedThemeVariable));
            if (forced != null) return forced;
#endif
            switch (ThemeResolver.SystemKind())
            {
                case ThemeKind.HighContrast: return Theme.ForHighContrast(HighContrastPalette.System);
                case ThemeKind.Dark: return Theme.Dark;
                default: return Theme.Light;
            }
        }

#if DEBUG
        /// <summary>Debug only: forces the theme of a live --sandbox window (light, dark, hc-black, hc-white).</summary>
        public const string ForcedThemeVariable = "QUIETHOSTS_THEME";
#endif

        /// <summary>Asks for a re-check after a short pause, so the several messages of one switch repaint once.</summary>
        public static void RequestRefresh(bool reapplyNative = false)
        {
            System.Windows.Forms.Timer? timer = _timer;
            if (timer == null) return;
            if (Thread.CurrentThread.ManagedThreadId != _uiThread)
            {
                // Only the UI thread may touch the timer: a call from another thread goes there first. (No Debug.Fail: its
                // message box would show on the desktop.)
                _context?.Post(_ => RequestRefresh(reapplyNative), null);
                return;
            }
#if DEBUG || QUIETHOSTS_UI_TESTS
            RefreshObserver?.Invoke(Thread.CurrentThread.ManagedThreadId);
#endif
            if (reapplyNative) _pendingNative = true;
            timer.Stop();
            timer.Start();
        }

        /// <summary>Windows' setting changed (any category): re-check on the UI thread.</summary>
        internal static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            SynchronizationContext? context = _context;
            if (context == null) return;
            context.Post(_ => RequestRefresh(), null);
        }

        private static void OnTick(object? sender, EventArgs e)
        {
            _timer?.Stop();
            Func<Theme>? detect = _detect;
            if (detect == null) return;
            bool native = _pendingNative;
            _pendingNative = false;
            Theme detected = detect();
            int[]? snapshot = detected.Palette?.Snapshot();
            bool changed = detected.Kind != _current.Kind || !string.Equals(detected.Name, _current.Name, StringComparison.Ordinal) || !Same(snapshot, _snapshot);
            if (!changed && !native) return;
            SetCurrent(detected);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void SetCurrent(Theme theme)
        {
            _current = theme;
            _snapshot = theme.Palette?.Snapshot();
        }

        private static bool Same(int[]? a, int[]? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void OnApplicationExit(object? sender, EventArgs e) => Stop();

        private static void Stop()
        {
            if (_subscribed)
            {
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                Application.ApplicationExit -= OnApplicationExit;
                _subscribed = false;
            }
            _timer?.Dispose();
            _timer = null;
            _context = null;
            _detect = null;
            _pendingNative = false;
        }

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests and --render-ui: use this theme now (raises <see cref="ThemeChanged"/>).</summary>
        internal static void Override(Theme theme)
        {
            SetCurrent(theme ?? throw new ArgumentNullException(nameof(theme)));
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Tests: back to Light, no timer, no subscriptions.</summary>
        internal static void ResetForTests()
        {
            Stop();
            SetCurrent(Theme.Light);
            RefreshObserver = null;
        }

        /// <summary>Tests: start like <see cref="Initialize"/> on the calling thread, with <paramref name="detect"/> as what Windows says, without subscribing to Windows.</summary>
        internal static void StartForTests(Func<Theme> detect)
        {
            Stop();
            Start(detect ?? throw new ArgumentNullException(nameof(detect)));
        }

        /// <summary>Tests: called with the thread id each time a refresh is requested on the UI thread.</summary>
        internal static Action<int>? RefreshObserver { get; set; }

        /// <summary>Tests: how many handlers listen to <see cref="ThemeChanged"/>.</summary>
        internal static int SubscriberCount => ThemeChanged?.GetInvocationList().Length ?? 0;
#endif
    }
}
