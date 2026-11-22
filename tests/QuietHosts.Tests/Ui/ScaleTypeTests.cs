using System;
using System.Drawing;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// Effective pixels, strokes and the type ramp (spec 2.6, 2.7; T-A-10, T-A-15). Every case sets the DPI it needs with
    /// <see cref="UiScale.Override"/> and builds ramps from a given base size, so nothing depends on this PC's display
    /// scale or text size.
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class ScaleTypeTests
    {
        private static readonly int[] Sizes = { 1, 2, 4, 16, 24, 32, 576 };

        [Theory]
        [InlineData(96, new[] { 1, 2, 4, 16, 24, 32, 576 }, new[] { 1, 2, 4, 16, 24, 32, 576 })]
        [InlineData(120, new[] { 1, 3, 5, 20, 30, 40, 720 }, new[] { 1, 2, 5, 20, 30, 40, 720 })]
        [InlineData(144, new[] { 2, 3, 6, 24, 36, 48, 864 }, new[] { 1, 3, 6, 24, 36, 48, 864 })]
        [InlineData(168, new[] { 2, 4, 7, 28, 42, 56, 1008 }, new[] { 1, 3, 7, 28, 42, 56, 1008 })]
        [InlineData(192, new[] { 2, 4, 8, 32, 48, 64, 1152 }, new[] { 2, 4, 8, 32, 48, 64, 1152 })]
        public void Effective_pixels_and_strokes_scale_with_the_dpi(int dpi, int[] px, int[] stroke)
        {
            var s = new UiScale(dpi);
            for (int i = 0; i < Sizes.Length; i++)
            {
                Assert.Equal(px[i], s.Px(Sizes[i]));
                Assert.Equal(stroke[i], s.Stroke(Sizes[i]));
            }
            Assert.Equal(dpi, s.Dpi);
            Assert.Equal(new Size(s.Px(760), s.Px(680)), s.Px(760, 680));
        }

        [Fact]
        public void A_dpi_override_scales_sizes_and_fonts_together()
        {
            try
            {
                UiScale.Override(192);
                Assert.Equal(192, UiScale.Current.Dpi);
                Assert.Equal(192f / UiScale.SystemDpi, UiScale.Current.FontFactor, 4);
                Assert.Equal(48, UiScale.Current.Px(24));
                UiScale.ResetForTests();
                Assert.Equal(UiScale.SystemDpi, UiScale.Current.Dpi);
                Assert.Equal(1f, UiScale.Current.FontFactor);
            }
            finally
            {
                UiGlobals.Reset();
            }
        }

        [Theory]
        [InlineData(96, 9f, 32)]
        [InlineData(96, 13.5f, 42)]
        [InlineData(144, 9f, 48)]
        [InlineData(144, 13.5f, 63)]
        public void One_line_controls_grow_with_the_text(int dpi, float basePoints, int expected)
        {
            try
            {
                UiScale.Override(dpi);
                using (var ramp = new TypeRamp(basePoints))
                {
                    // max(Px(32), Body line + Px(12)): 32 epx at 100% text, 42 epx at 150% text.
                    Assert.Equal(expected, ramp.TextDriven(Metrics.ButtonHeight, TextStyle.Body, Metrics.ButtonPadY));
                }
            }
            finally
            {
                UiGlobals.Reset();
            }
        }

        // ---------------------------------------------------------------- T-A-15

        [Theory]
        [InlineData(9f, 9f, 10.5f, 15f, 21f)]
        [InlineData(13.5f, 13.5f, 15.75f, 22.5f, 31.5f)]
        public void The_ramp_has_the_fluent_sizes(float b, float caption, float body, float subtitle, float title)
        {
            try
            {
                UiScale.Override(96);
                using (var ramp = new TypeRamp(b))
                {
                    Assert.Equal(caption, ramp.Points(TextStyle.Caption), 3);
                    Assert.Equal(body, ramp.Points(TextStyle.Body), 3);
                    Assert.Equal(body, ramp.Points(TextStyle.BodyStrong), 3);
                    Assert.Equal(body, ramp.Points(TextStyle.Mono), 3);
                    Assert.Equal(subtitle, ramp.Points(TextStyle.Subtitle), 3);
                    Assert.Equal(title, ramp.Points(TextStyle.Title), 3);
                    // Fonts in points: GDI applies the DPI (times the simulation factor when the override isn't this PC's DPI).
                    float factor = UiScale.Current.FontFactor;
                    Assert.Equal(body * factor, ramp.Body.SizeInPoints, 2);
                    Assert.Equal(GraphicsUnit.Point, ramp.Body.Unit);
                    Assert.Equal(title * factor, ramp.Title.SizeInPoints, 2);
                    Assert.Equal(caption * factor, ramp.Caption.SizeInPoints, 2);
                }
            }
            finally
            {
                UiGlobals.Reset();
            }
        }

        [Theory]
        [InlineData(9f, 16, 20, 28, 36)]
        [InlineData(13.5f, 24, 30, 42, 54)]
        public void Line_heights_follow_the_ramp_in_pixels(float b, int caption, int body, int subtitle, int title)
        {
            try
            {
                UiScale.Override(96);
                using (var ramp = new TypeRamp(b))
                {
                    Assert.Equal(caption, ramp.LineHeight(TextStyle.Caption));
                    Assert.Equal(body, ramp.LineHeight(TextStyle.Body));
                    Assert.Equal(body, ramp.LineHeight(TextStyle.BodyStrong));
                    Assert.Equal(subtitle, ramp.LineHeight(TextStyle.Subtitle));
                    Assert.Equal(title, ramp.LineHeight(TextStyle.Title));
                }
            }
            finally
            {
                UiGlobals.Reset();
            }
        }

        [Fact]
        public void Strong_text_is_segoe_ui_semibold_when_gdi_has_it()
        {
            using (var ramp = new TypeRamp(9f))
            {
                Font? semibold = UiFonts.TryFont("Segoe UI Semibold", 10.5f, FontStyle.Regular);
                bool hasSemibold = semibold != null && string.Equals(ramp.Family, "Segoe UI", StringComparison.OrdinalIgnoreCase);
                semibold?.Dispose();
                foreach (Font f in new[] { ramp.BodyStrong, ramp.Subtitle, ramp.Title })
                {
                    if (hasSemibold)
                    {
                        Assert.Equal("Segoe UI Semibold", f.Name);
                        Assert.False(f.Bold);
                    }
                    else
                    {
                        Assert.True(f.Bold);
                    }
                }
                Assert.False(ramp.Body.Bold);
                Assert.False(ramp.Body.Italic);
                Assert.Equal(ramp.Family, ramp.Body.Name);
                if (UiFonts.TryFont("Consolas", 10.5f, FontStyle.Regular) is Font consolas)
                {
                    consolas.Dispose();
                    Assert.Equal("Consolas", ramp.Mono.Name);
                }
            }
        }

        [Theory]
        [InlineData(9f, 100, 9f)]
        [InlineData(9f, 150, 13.5f)]
        [InlineData(13.5f, 150, 13.5f)]
        [InlineData(11f, 100, 11f)]
        [InlineData(11f, 150, 16.5f)]
        [InlineData(9f, null, 9f)]
        [InlineData(9f, 99, 9f)]
        [InlineData(9f, 226, 9f)]
        [InlineData(9f, 225, 20.25f)]
        public void The_base_size_follows_the_text_size_without_counting_it_twice(float messageBoxPoints, int? textScaleFactor, float expected)
        {
            Assert.Equal(expected, TypeRamp.ResolveBasePoints(messageBoxPoints, textScaleFactor), 3);
        }

        [Fact]
        public void The_text_size_line_for_copy_details_names_the_percent_and_the_font()
        {
            Assert.Equal("150% (message font 13.5 pt)", Strings.DetailsTextSizeValue(150, 13.5f));
            Assert.Equal("100% (message font 9.0 pt)", Strings.DetailsTextSizeValue(100, 9f));
            string line = DetailsReport.TextSizeValue();
            Assert.Matches(@"\A(1[0-9]{2}|2[0-2][0-9])% \(message font [0-9]+\.[0-9] pt\)\z", line);
            Assert.Equal(100, TypeRamp.TextScalePercent(null));
            Assert.Equal(100, TypeRamp.TextScalePercent(300));
            Assert.Equal(175, TypeRamp.TextScalePercent(175));
        }

        [Fact]
        public void Window_tests_lay_out_at_the_normal_text_size_on_any_pc()
        {
            // Spec 10: no test depends on the PC's text size. Window tests ran at whatever base size this PC's message font and
            // text-size setting gave, so 9 of them failed on a PC at 125% (behavior-1): they pin the base to 9 pt.
            Sta.Run(() =>
            {
                try
                {
                    Assert.Equal(UiGlobals.TestBasePoints, TypeRamp.PreviewBasePoints);
                    Assert.Equal(9f, TypeRamp.ResolveBasePoints(), 3);
                    using (var w = new TestWindow()) Assert.Equal(9f, w.Fonts.BasePoints, 3);
                    // A PC at 125% would give 11.25 pt; a test that asks for it gets exactly that, whatever this PC says.
                    TypeRamp.PreviewBasePoints = 11.25f;
                    using (var w = new TestWindow()) Assert.Equal(11.25f, w.Fonts.BasePoints, 3);
                }
                finally
                {
                    UiGlobals.Reset();
                }
                Assert.Equal(UiGlobals.TestBasePoints, TypeRamp.PreviewBasePoints);
            });
        }

#if DEBUG
        [Fact]
        public void The_debug_text_scale_multiplies_the_base_size()
        {
            try
            {
                float normal = TypeRamp.ResolveBasePoints();
                UiFonts.TextScale = 1.5f;
                Assert.Equal(normal * 1.5f, TypeRamp.ResolveBasePoints(), 3);
            }
            finally
            {
                UiGlobals.Reset();
            }
        }
#endif
    }
}
