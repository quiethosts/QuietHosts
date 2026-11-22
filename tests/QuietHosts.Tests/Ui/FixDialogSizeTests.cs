using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    [Collection(WindowCollection.Name)]
    public class FixDialogSizeTests
    {
        private static readonly MethodInfo OnFormClosed = typeof(Form).GetMethod("OnFormClosed", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public static IEnumerable<object[]> Scales()
        {
            yield return new object[] { 1f };
#if DEBUG
            yield return new object[] { 1.5f };
#endif
        }

        /// <summary>The client size is saved in epx (D11), so it comes back the same at any text size.</summary>
        [Theory]
        [MemberData(nameof(Scales))]
        public void The_fix_dialog_remembers_its_size(float textScale)
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                // Not a preview, so the dialog saves its size; the in-memory user store keeps it. No change is run.
                var ctx = new UiContext(env, BlockLists.LoadEmbedded(), @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                HostsStatus status = UiScenarios.Status(ctx.Lists, ProtectionState.On);
                env.User.SetFixSize(820, 610);
                Sta.Run(() =>
                {
#if DEBUG
                    UiFonts.TextScale = textScale;
#endif
                    try
                    {
                        using (var d = new FixDialog(new AppController(ctx, status)))
                        {
                            UiScale s = UiScale.Current;
                            // The same client size at every text size (epx scale with the DPI only).
                            Assert.True(new Size(s.Px(820), s.Px(610)) == d.ClientSize, "at text scale " + textScale + " the client size is " + d.ClientSize);
                            OnFormClosed.Invoke(d, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
                        }
                    }
                    finally
                    {
                        UiGlobals.Reset();
                    }
                });
                UserSettings saved = env.User.Read();
                Assert.InRange(saved.FixW ?? 0, 819, 821);
                Assert.InRange(saved.FixH ?? 0, 609, 611);
            }
        }

        /// <summary>
        /// Opening raises a remembered size below the smallest one to it (the footer and the body with four list rows), and
        /// a size never remembered is 760 × 680 epx; at most 4000 × 3000 epx comes back.
        /// </summary>
        [Fact]
        public void The_remembered_size_is_clamped_to_the_smallest_and_largest()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                var ctx = new UiContext(env, BlockLists.LoadEmbedded(), @"C:\Apps\QuietHosts\QuietHosts.exe", RunLocationTips.None, false, preview: false);
                HostsStatus status = UiScenarios.Status(ctx.Lists, ProtectionState.On);
                Sta.Run(() =>
                {
                    UiScale s = UiScale.Current;
                    using (var d = new FixDialog(new AppController(ctx, status)))
                    {
                        Assert.Equal(new Size(s.Px(760), s.Px(680)), d.ClientSize);
                    }
                    env.User.SetFixSize(100, 100);
                    using (var d = new FixDialog(new AppController(ctx, status)))
                    {
                        Assert.Equal(new Size(s.Px(640), s.Px(480)), d.ClientSize);
                        ControlTree.CreateHandles(d);
                        d.PreviewWorkingArea = Sta.LargeScreen;
                        d.PreviewFit();
                        // Raised to what four list rows need, and the body doesn't scroll at that size.
                        Assert.True(d.ClientSize.Height > s.Px(480), "the smallest height wasn't raised: " + d.ClientSize.Height);
                        Assert.Equal(d.MinimumSize, d.Size);
                        Assert.False(d.ContentScrolls);
                        Assert.True(d.PreviewListCard.Height >= d.PreviewMinListCardHeight);
                    }
                });
            }
            // In epx, before the DPI: a value from the store outside 640..4000 x 480..3000 comes back inside it.
            Assert.Equal(new Size(4000, 3000), FixDialog.RememberedSizeEpx(9000, 9000));
            Assert.Equal(new Size(640, 480), FixDialog.RememberedSizeEpx(-5, 0));
            Assert.Equal(new Size(760, 680), FixDialog.RememberedSizeEpx(900, null));
            Assert.Equal(new Size(900, 700), FixDialog.RememberedSizeEpx(900, 700));
        }
    }
}
