using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// A window taller than its screen's working area (a large text size on a small screen) scrolls its content and
    /// keeps its bottom buttons on the screen; with room again it is as tall as its content. Some cases change the
    /// Debug-only text scale, which is global (the window tests run one at a time).
    /// </summary>
    [Collection(WindowCollection.Name)]
    public class ScreenFitTests
    {
        private static readonly Rectangle ShortScreen = new Rectangle(0, 0, 1366, 400);
        private static readonly Rectangle LargeScreen = new Rectangle(0, 0, 2560, 1400);
        private static readonly MethodInfo OnClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static void AssertOnScreen(Form f, Control c, Rectangle area, string what)
        {
            Assert.True(f.Height <= area.Height, what + ": the window is " + f.Height + " px tall on a " + area.Height + " px screen");
            Rectangle r = f.RectangleToClient(c.RectangleToScreen(c.ClientRectangle));
            Assert.True(r.Top >= 0 && r.Bottom <= f.ClientSize.Height, what + " is outside the window: " + r + " in " + f.ClientSize);
        }

        private static Button ButtonWithText(Form f, string text) => ControlTree.All(f).OfType<Button>().Single(b => b.Text == text);

        [Fact]
        public void The_first_run_text_scrolls_and_its_buttons_stay_on_a_short_screen()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    int mainHeight;
                    using (var normal = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.Off)), showFirstRun: false))
                    {
                        ControlTree.CreateHandles(normal);
                        mainHeight = normal.Height;
                    }

                    HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.Off, firstRunDone: false);
                    using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: true))
                    {
                        ControlTree.CreateHandles(f);
                        f.PreviewWorkingArea = ShortScreen;
                        f.PreviewFit();
                        Assert.True(f.ContentScrolls);
                        // The first run's buttons are in the footer slot below the text (the hidden page's main button has the same text).
                        AssertOnScreen(f, f.PreviewFirstRun!.TurnOnButton, ShortScreen, "Turn on protection");
                        AssertOnScreen(f, ButtonWithText(f, Strings.FirstRunNotNowButton), ShortScreen, "Not now");

                        // "Not now": the normal content replaces the text and still fits the screen; the footer row stays visible
                        // below the page, which scrolls (D15).
                        OnClick.Invoke(ButtonWithText(f, Strings.FirstRunNotNowButton), new object[] { EventArgs.Empty });
                        Assert.False(f.IsShowingFirstRun);
                        Assert.True(f.ContentScrolls);
                        Assert.True(f.Height <= ShortScreen.Height);
                        foreach (string text in new[] { Strings.FixButton, Strings.UndoButton, Strings.AboutButton })
                            AssertOnScreen(f, ButtonWithText(f, text), ShortScreen, text);
                        Assert.True(f.PreviewFooter.ShowDivider);

                        // With room again, the window is as tall as its content and nothing scrolls.
                        f.PreviewWorkingArea = LargeScreen;
                        f.PreviewFit();
                        Assert.False(f.ContentScrolls);
                        Assert.Equal(mainHeight, f.Height);
                        Assert.False(f.PreviewFooter.ShowDivider);
                    }
                });
            }
        }

        [Fact]
        public void A_window_that_fits_is_unchanged()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var f = new MainForm(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On)), showFirstRun: false))
                    {
                        ControlTree.CreateHandles(f);
                        Size before = f.Size;
                        f.PreviewWorkingArea = LargeScreen;
                        f.PreviewFit();
                        Assert.False(f.ContentScrolls);
                        Assert.Equal(before, f.Size);
                    }
                });
            }
        }

        [Fact]
        public void How_it_works_scrolls_and_its_close_button_stays_on_a_short_screen()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new HowItWorksDialog(p.Context))
                    {
                        ControlTree.CreateHandles(d);
                        d.PreviewWorkingArea = ShortScreen;
                        d.PreviewFit();
                        Assert.True(d.ContentScrolls);
                        AssertOnScreen(d, (Button)d.CancelButton!, ShortScreen, "Close");
                    }
                });
            }
        }

        [Fact]
        public void The_fix_dialog_and_its_smallest_size_fit_a_short_screen()
        {
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                    {
                        ControlTree.CreateHandles(d);
                        Assert.True(d.MinimumSize.Height > ShortScreen.Height, "the case this covers: the smallest size is taller than the screen");
                        d.PreviewWorkingArea = ShortScreen;
                        d.PreviewFit();
                        Assert.True(d.MinimumSize.Height <= ShortScreen.Height);
                        Assert.True(d.Height <= ShortScreen.Height);
                        AssertOnScreen(d, (Button)d.CancelButton!, ShortScreen, "Close");
                        AssertOnScreen(d, d.PreviewAllow, ShortScreen, "Allow");
                        AssertOnScreen(d, d.PreviewBlockAgain, ShortScreen, "Block again");
                        // D18: the list keeps its four rows; the body scrolls above the footer instead.
                        Assert.True(d.ContentScrolls, "the body doesn't scroll on a short screen");
                        Assert.True(d.PreviewListCard.Height >= d.PreviewMinListCardHeight, "the list lost rows: " + d.PreviewListCard.Height + " < " + d.PreviewMinListCardHeight);

                        // With room again, the list takes it and nothing scrolls.
                        d.PreviewWorkingArea = LargeScreen;
                        d.PreviewFit();
                        d.ClientSize = new Size(d.ClientSize.Width, UiScale.Current.Px(Metrics.FixDefaultClientHeight) + UiScale.Current.Px(200));
                        Assert.False(d.ContentScrolls);
                        Assert.True(d.PreviewListCard.Height > d.PreviewMinListCardHeight);
                    }
                });
            }
        }

#if DEBUG
        /// <summary>A 1366 x 768 laptop with Windows text size 150% (the Debug text scale stands in for it).</summary>
        [Fact]
        public void At_150_percent_text_the_first_run_buttons_stay_on_a_laptop_screen()
        {
            Rectangle laptop = UiScenarios.LaptopScreen;
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiFonts.TextScale = 1.5f;
                    try
                    {
                        HostsStatus s = UiScenarios.Status(p.Lists, ProtectionState.OlderList, firstRunDone: false);
                        using (var f = new MainForm(new AppController(p.Context, s), showFirstRun: true))
                        {
                            ControlTree.CreateHandles(f);
                            f.PreviewWorkingArea = laptop;
                            f.PreviewFit();
                            Assert.True(f.ContentScrolls);
                            AssertOnScreen(f, f.PreviewFirstRun!.TurnOnButton, laptop, "Turn on protection");
                        }
                        // The page at 150% text: the footer row stays on the screen below it.
                        HostsStatus warnings = UiScenarios.Status(p.Lists, ProtectionState.On, warnings: HostsWarnings.EditedBlock | HostsWarnings.LargeFile);
                        using (var f = new MainForm(new AppController(p.Context, warnings), showFirstRun: false))
                        {
                            ControlTree.CreateHandles(f);
                            f.PreviewWorkingArea = laptop;
                            f.PreviewFit();
                            Assert.True(f.ContentScrolls);
                            foreach (string text in new[] { Strings.FixButton, Strings.UndoButton, Strings.AboutButton })
                                AssertOnScreen(f, ButtonWithText(f, text), laptop, text);
                        }
                        using (var d = new HowItWorksDialog(p.Context))
                        {
                            ControlTree.CreateHandles(d);
                            d.PreviewWorkingArea = laptop;
                            d.PreviewFit();
                            AssertOnScreen(d, (Button)d.CancelButton!, laptop, "Close");
                        }
                    }
                    finally
                    {
                        UiFonts.TextScale = 1f;
                    }
                });
            }
        }

        /// <summary>At 200% text the Fix dialog's smallest size alone is taller than a 768-pixel screen.</summary>
        [Fact]
        public void At_200_percent_text_the_fix_dialog_buttons_stay_on_a_laptop_screen()
        {
            Rectangle laptop = UiScenarios.LaptopScreen;
            using (var p = new PreviewContext())
            {
                Sta.Run(() =>
                {
                    UiFonts.TextScale = 2f;
                    try
                    {
                        using (var d = new FixDialog(new AppController(p.Context, UiScenarios.Status(p.Lists, ProtectionState.On))))
                        {
                            ControlTree.CreateHandles(d);
                            d.PreviewWorkingArea = laptop;
                            d.PreviewFit();
                            Assert.True(d.MinimumSize.Height <= laptop.Height && d.MinimumSize.Width <= laptop.Width);
                            AssertOnScreen(d, (Button)d.CancelButton!, laptop, "Close");
                            AssertOnScreen(d, d.PreviewAllow, laptop, "Allow");
                            Assert.True(d.ContentScrolls);
                            Assert.True(d.PreviewListCard.Height >= d.PreviewMinListCardHeight);
                        }
                    }
                    finally
                    {
                        UiFonts.TextScale = 1f;
                    }
                });
            }
        }
#endif
    }
}
