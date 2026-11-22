using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace QuietHosts
{
    /// <summary>Where a shield bitmap came from (tests check the first path works in the test host).</summary>
    internal enum UacShieldSource
    {
        /// <summary>SHGetStockIconInfo's icon location, extracted at exactly the size (SHDefExtractIconW).</summary>
        Extracted,
        /// <summary>The small stock icon, drawn scaled.</summary>
        StockSmall,
        /// <summary>SystemIcons.Shield, drawn scaled.</summary>
        SystemIcon,
    }

    /// <summary>
    /// The Windows UAC shield as a 32 bpp bitmap at an exact pixel size, cached per size (spec 2.8.10). It marks every
    /// button whose click immediately starts an elevated change, and no other. The same image in every theme: it is the
    /// real system shield, which users must recognize. Everything here is shell32, so it works in every process.
    /// </summary>
    internal static class UacShield
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, Bitmap?> Cache = new Dictionary<int, Bitmap?>();
        private static readonly Dictionary<int, UacShieldSource> Sources = new Dictionary<int, UacShieldSource>();
        private static bool _exitHooked;

        /// <summary>The shield at <paramref name="px"/> x <paramref name="px"/> pixels, or null if Windows has none.</summary>
        public static Bitmap? Get(int px)
        {
            if (px <= 0) return null;
            lock (Gate)
            {
                if (Cache.TryGetValue(px, out Bitmap? cached)) return cached;
                Bitmap? bmp = Create(px, out UacShieldSource source);
                Cache[px] = bmp;
                if (bmp != null) Sources[px] = source;
                if (!_exitHooked)
                {
                    _exitHooked = true;
                    Application.ApplicationExit += (s, e) => Clear();
                }
                return bmp;
            }
        }

        /// <summary>Tests: how the bitmap of that size was made, or null if none was.</summary>
        internal static UacShieldSource? SourceFor(int px)
        {
            lock (Gate) return Sources.TryGetValue(px, out UacShieldSource s) ? s : (UacShieldSource?)null;
        }

        /// <summary>Draws the shield into <paramref name="box"/>; disabled buttons draw it at 40% opacity.</summary>
        public static void Draw(Graphics g, Rectangle box, bool disabled)
        {
            Bitmap? bmp = Get(box.Width);
            if (bmp == null) return;
            if (!disabled)
            {
                g.DrawImage(bmp, box);
                return;
            }
            var matrix = new ColorMatrix { Matrix33 = 0.4f };
            using (var attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(bmp, box, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, attributes);
            }
        }

        private static Bitmap? Create(int px, out UacShieldSource source)
        {
            // 1. The stock icon's file and index, extracted at exactly px (verified 16-48 px in the test host).
            source = UacShieldSource.Extracted;
            (string Path, int Index)? location = NativeTheme.ShieldLocation();
            if (location != null)
            {
                IntPtr h = NativeTheme.ExtractIcon(location.Value.Path, location.Value.Index, px);
                if (h != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon icon = Icon.FromHandle(h))
                        {
                            // ToBitmap keeps the alpha channel.
                            Bitmap bmp = icon.ToBitmap();
                            if (bmp.Width == px && bmp.Height == px) return bmp;
                            Bitmap scaled = Scale(bmp, px);
                            bmp.Dispose();
                            return scaled;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
                    {
                    }
                    finally
                    {
                        NativeTheme.Destroy(h);
                    }
                }
            }

            // 2. The small stock icon, scaled.
            source = UacShieldSource.StockSmall;
            IntPtr small = NativeTheme.SmallShieldIcon();
            if (small != IntPtr.Zero)
            {
                try
                {
                    using (Icon icon = Icon.FromHandle(small))
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        return Scale(bmp, px);
                    }
                }
                catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
                {
                }
                finally
                {
                    NativeTheme.Destroy(small);
                }
            }

            // 3. SystemIcons.Shield, scaled.
            source = UacShieldSource.SystemIcon;
            try
            {
                using (var icon = new Icon(SystemIcons.Shield, px, px))
                using (Bitmap bmp = icon.ToBitmap())
                {
                    return Scale(bmp, px);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
            {
                return null;
            }
        }

        private static Bitmap Scale(Bitmap source, int px)
        {
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, px, px));
            }
            return bmp;
        }

        private static void Clear()
        {
            lock (Gate)
            {
                foreach (Bitmap? b in Cache.Values) b?.Dispose();
                Cache.Clear();
                Sources.Clear();
            }
        }
    }
}
