#if DEBUG
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>
    /// Debug-only "--render-ui" (spec 9): draws every scenario of <see cref="UiScenarios.All"/> with fake statuses, in each
    /// theme pass, to %TEMP%\QuietHosts-render\&lt;scenario&gt;-&lt;theme&gt;[-text&lt;N&gt;][-dpi&lt;N&gt;].png, plus the icons and a
    /// log, then exits (0 only if every file rendered). Nothing reads or writes the real system.
    /// <para>
    /// Nothing shows on the desktop of whoever uses the PC: no window, no taskbar button, no change of the keyboard focus.
    /// The whole render loop runs on one STA thread of its own, which first turns off the Windows Error Reporting window for
    /// this process (SetErrorMode with SEM_NOGPFAULTERRORBOX), then creates a desktop of its own
    /// (QuietHosts.Render.&lt;guid&gt;, never switched to) and moves onto it with SetThreadDesktop before it makes any window or
    /// SystemEvents hook. Every window it shows lives on that desktop. If the desktop can't be created or entered, the run
    /// writes a FAILED line and exits 1: it never falls back to the user's desktop. The main thread makes no window; it
    /// waits for the render thread and then closes the desktop.
    /// </para>
    /// <para>
    /// Windows are captured with PrintWindow(PW_RENDERFULLCONTENT), which includes a dark title bar and works on a desktop
    /// nobody sees; DrawToBitmap is the fallback (logged), and draws the gallery's content when the gallery is larger than
    /// the screen (large text, 200% scale). Debug builds read three environment variables (Release builds
    /// never do): QUIETHOSTS_RENDER_TEXT_SCALE (100-225), QUIETHOSTS_RENDER_THEMES (light, dark, hc-black, hc-white) and
    /// QUIETHOSTS_RENDER_DPI (96-288).
    /// </para>
    /// </summary>
    internal static class RenderUi
    {
        public const string Option = "--render-ui";
        public const string FolderName = "QuietHosts-render";
        public const string TextScaleVariable = "QUIETHOSTS_RENDER_TEXT_SCALE";
        public const string ThemesVariable = "QUIETHOSTS_RENDER_THEMES";
        public const string DpiVariable = "QUIETHOSTS_RENDER_DPI";
        /// <summary>The render desktop's name starts with this (then a new GUID each run).</summary>
        public const string DesktopPrefix = "QuietHosts.Render.";
        private const string DefaultThemes = "light,dark,hc-black";

        public static int Run()
        {
            var session = new Session(Path.Combine(Path.GetTempPath(), FolderName), ReadTextScale(), ReadDpi());
            var thread = new Thread(session.RenderThread) { Name = "QuietHosts render", IsBackground = false };
            thread.SetApartmentState(ApartmentState.STA);
            try
            {
                thread.Start();
                thread.Join();
            }
            finally
            {
                // Only now: no thread of this process uses the desktop any more (its windows went with the thread).
                session.CloseDesktop();
            }
            return session.ExitCode;
        }

        private static int ReadTextScale()
        {
            string? value = Environment.GetEnvironmentVariable(TextScaleVariable);
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent) && percent >= 100 && percent <= 225) return percent;
            return 100;
        }

        /// <summary>The DPI QUIETHOSTS_RENDER_DPI asks for (96-288), or null for the real system DPI.</summary>
        private static int? ReadDpi()
        {
            string? value = Environment.GetEnvironmentVariable(DpiVariable);
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int dpi) && dpi >= 96 && dpi <= 288) return dpi;
            return null;
        }

        /// <summary>The theme passes, each with its file suffix; invalid values fall back to light, dark and hc-black.</summary>
        internal static List<(Theme Theme, string Suffix)> ReadThemes(string? value = null)
        {
            var passes = new List<(Theme, string)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in (value ?? Environment.GetEnvironmentVariable(ThemesVariable) ?? string.Empty).Split(','))
            {
                string name = part.Trim().ToLowerInvariant();
                Theme? theme = Theme.FromName(name);
                if (theme == null || !seen.Add(name)) continue;
                passes.Add((theme, SuffixFor(name)));
            }
            if (passes.Count == 0 && value != DefaultThemes) return ReadThemes(DefaultThemes);
            return passes;
        }

        private static string SuffixFor(string name)
        {
            switch (name)
            {
                case "hc-black": return "-hc";
                case "hc-white": return "-hcwhite";
                default: return "-" + name;
            }
        }

        private static string Variant(int percent, int dpi, int realDpi) =>
            (percent == 100 ? string.Empty : "-text" + percent.ToString(CultureInfo.InvariantCulture)) +
            (dpi == realDpi ? string.Empty : "-dpi" + dpi.ToString(CultureInfo.InvariantCulture));

        /// <summary>One --render-ui run: its folder and log, the render thread and the desktop that thread lives on.</summary>
        private sealed class Session
        {
            private readonly string _dir;
            private readonly int _percent;
            private readonly int? _dpi;
            private string? _logPath;
            private IntPtr _desktop;
            private Exception? _windowError;

            public Session(string dir, int percent, int? dpi)
            {
                _dir = dir;
                _percent = percent;
                _dpi = dpi;
            }

            /// <summary>0 only if every file rendered.</summary>
            public int ExitCode { get; private set; } = 1;

            // Written line by line, so even a run that dies (a stack overflow can't be caught) shows how far it got.
            private void Log(string line)
            {
                try
                {
                    if (_logPath == null)
                    {
                        Directory.CreateDirectory(_dir);
                        // A failure before the desktop exists, so before the real DPI is known: the suffixes the variables ask for.
                        string dpi = _dpi.HasValue ? "-dpi" + _dpi.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                        _logPath = Path.Combine(_dir, "render-log" + Variant(_percent, 0, 0) + dpi + ".txt");
                    }
                    File.AppendAllText(_logPath, line + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            /// <summary>The render thread: its own desktop first, then every scenario. Never lets an exception out.</summary>
            public void RenderThread()
            {
                try
                {
                    // 1. Before anything else: a crash of this process shows no Windows Error Reporting window.
                    uint previous = NativeMethods.SetErrorMode(NativeMethods.SEM_FAILCRITICALERRORS | NativeMethods.SEM_NOGPFAULTERRORBOX);
                    NativeMethods.SetErrorMode(previous | NativeMethods.SEM_FAILCRITICALERRORS | NativeMethods.SEM_NOGPFAULTERRORBOX);

                    // 2. A desktop of its own, before this thread makes any window, SystemEvents hook or GDI+ call (a thread
                    //    with a window or hook can't change desktops). Never the user's desktop: no desktop, no render.
                    string name = DesktopPrefix + Guid.NewGuid().ToString("N");
                    IntPtr desktop = NativeMethods.CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0, NativeMethods.GENERIC_ALL, IntPtr.Zero);
                    if (desktop == IntPtr.Zero)
                    {
                        Log("FAILED: Windows refused a desktop for the render (CreateDesktop error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) +
                            "). Nothing was rendered; the renderer never uses the user's desktop.");
                        return;
                    }
                    _desktop = desktop;
                    if (!NativeMethods.SetThreadDesktop(desktop))
                    {
                        Log("FAILED: the render thread couldn't move to its desktop (SetThreadDesktop error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) +
                            "). Nothing was rendered; the renderer never uses the user's desktop.");
                        return;
                    }
                    string current = NativeMethods.CurrentDesktopName();
                    if (!string.Equals(current, name, StringComparison.Ordinal))
                    {
                        Log("FAILED: the render thread is on the desktop \"" + current + "\", not its own. Nothing was rendered.");
                        return;
                    }

                    // 3. A problem inside a window procedure fails that scenario instead of showing WinForms' exception dialog
                    //    (which nobody could close on this desktop).
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                    Application.ThreadException += (s, e) => { if (_windowError == null) _windowError = e.Exception; };

                    ExitCode = RenderAll(name);
                }
                catch (Exception ex)
                {
                    Log("FAILED: " + ex);
                    ExitCode = 1;
                }
            }

            /// <summary>Closes the render desktop once the render thread has ended (main thread).</summary>
            public void CloseDesktop()
            {
                if (_desktop == IntPtr.Zero) return;
                if (!NativeMethods.CloseDesktop(_desktop))
                    Log("note: CloseDesktop failed (error " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture) + "); Windows frees it when this process exits.");
                _desktop = IntPtr.Zero;
            }

            private int RenderAll(string desktopName)
            {
                Directory.CreateDirectory(_dir);
                int realDpi = UiScale.SystemDpi;
                int dpi = _dpi ?? realDpi;
                UiFonts.TextScale = _percent / 100f;
                if (dpi != realDpi) UiScale.Override(dpi);
                string variant = Variant(_percent, dpi, realDpi);
                _logPath = Path.Combine(_dir, "render-log" + variant + ".txt");
                File.WriteAllText(_logPath, string.Empty);
                Log("desktop: " + desktopName + " (its own, never shown)");
                int failures = 0;
                string envRoot = Path.Combine(_dir, "env");
                try
                {
                    // A test environment under %TEMP% with in-memory stores; the windows only get fake statuses.
                    AppEnvironment env = AppEnvironment.ForTesting(envRoot);
                    BlockLists lists = BlockLists.LoadEmbedded();
                    var ctx = new UiContext(env, lists, Application.ExecutablePath, RunLocationTips.None, false, preview: true);
                    foreach ((Theme theme, string suffix) in ReadThemes())
                    {
                        ThemeManager.Override(theme);
                        foreach (UiScenario s in UiScenarios.All(ctx))
                        {
                            string file = s.Name + suffix + variant + ".png";
                            _windowError = null;
                            try
                            {
                                (Size size, string method) = Render(s, Path.Combine(_dir, file));
                                if (_windowError != null) throw new InvalidOperationException("A window procedure failed while rendering.", _windowError);
                                Log(file + " " + Px(size) + " " + method);
                            }
                            catch (Exception ex) when (!(ex is OutOfMemoryException))
                            {
                                failures++;
                                Log("FAILED " + file + ": " + ex);
                            }
                        }
                        string icons = "icons" + suffix + variant + ".png";
                        try
                        {
                            Size size = RenderIcons(Path.Combine(_dir, icons), theme);
                            Log(icons + " " + Px(size) + " drawn");
                        }
                        catch (Exception ex) when (!(ex is OutOfMemoryException))
                        {
                            failures++;
                            Log("FAILED " + icons + ": " + ex);
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    failures++;
                    Log("FAILED: " + ex);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(envRoot)) Directory.Delete(envRoot, true);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return failures == 0 ? 0 : 1;
            }
        }

        private static string Px(Size size) => size.Width.ToString(CultureInfo.InvariantCulture) + "x" + size.Height.ToString(CultureInfo.InvariantCulture);

        private static (Size Size, string Method) Render(UiScenario s, string path)
        {
            using (Form f = s.Create())
            {
                // The render desktop is never shown; these keep a window from taking any focus there too.
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.ShowInTaskbar = false;
                if (f is IPreviewWindow w) w.PreviewNoActivate = true;
                f.Show();
                // A desktop that never had mouse input starts windows with focus cues on; renders show what a mouse user
                // sees (focus only where a scenario forces it, 9.2).
                NativeMethods.HideKeyboardCues(f.Handle);
                Pump();
                if (s.AfterShown != null)
                {
                    s.AfterShown(f);
                    Pump();
                }
                // Every part painted before the capture, even on a busy PC (a pending WM_PAINT would capture as blank).
                NativeMethods.RedrawNow(f.Handle);
                // The gallery can be larger than any screen (large text, 200% scale), and Windows won't make a window larger
                // than the screen: then its whole content is drawn, without the frame.
                Control? content = (f as GalleryForm)?.Root;
                if (content != null && (content.Width > f.ClientSize.Width || content.Height > f.ClientSize.Height))
                {
                    using (var bmp = new Bitmap(content.Width, content.Height, PixelFormat.Format24bppRgb))
                    {
                        content.DrawToBitmap(bmp, new Rectangle(Point.Empty, content.Size));
                        bmp.Save(path, ImageFormat.Png);
                    }
                    f.Close();
                    return (content.Size, "DrawToBitmap of the content (larger than the screen)");
                }
                Size size = f.Size;
                string method;
                using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb))
                {
                    // PrintWindow now and then gets a control that hasn't finished painting (or composing) as a blank block,
                    // while the whole image still has many colors: every capture is checked control by control, and taken
                    // again after the window has painted and composed, up to three times.
                    bool printed = false;
                    List<Control> blank = new List<Control>();
                    int attempt;
                    for (attempt = 1; attempt <= CaptureAttempts; attempt++)
                    {
                        printed = TryPrintWindow(f, bmp);
                        if (!printed) break;
                        blank = BlankControls(f, bmp);
                        if (blank.Count == 0) break;
                        NativeMethods.Flush();
                        Pump();
                        NativeMethods.RedrawNow(f.Handle);
                        NativeMethods.Flush();
                    }
                    if (!printed)
                    {
                        using (Graphics g = Graphics.FromImage(bmp)) g.Clear(Color.Black);
                        f.DrawToBitmap(bmp, new Rectangle(Point.Empty, size));
                        method = "DrawToBitmap (PrintWindow failed or gave one color)";
                    }
                    else if (blank.Count > 0)
                    {
                        // Still blank: each such control is drawn into the capture from its own painting, so the image is right.
                        foreach (Control c in blank) DrawInto(f, bmp, c);
                        method = "PrintWindow, with " + blank.Count.ToString(CultureInfo.InvariantCulture) + " control(s) PrintWindow left blank drawn by DrawToBitmap: " +
                            string.Join(", ", blank.ConvertAll(Describe));
                    }
                    else
                    {
                        method = attempt > 1 ? "PrintWindow (capture " + attempt.ToString(CultureInfo.InvariantCulture) + ": the earlier ones left a control blank)" : "PrintWindow";
                    }
                    bmp.Save(path, ImageFormat.Png);
                }
                f.Close();
                return (size, method);
            }
        }

        private const int CaptureAttempts = 3;

        private static string Describe(Control c) => c.GetType().Name + (string.IsNullOrEmpty(c.Text) ? string.Empty : " \"" + c.Text + "\"");

        /// <summary>
        /// The controls that paint content (buttons, labels with text, text boxes, lists) whose visible part is a single color
        /// in the capture although the control paints more than that there: PrintWindow got them blank.
        /// </summary>
        private static List<Control> BlankControls(Form f, Bitmap capture)
        {
            var blank = new List<Control>();
            foreach (Control c in Descendants(f))
            {
                if (!(c is ButtonBase || c is Label || c is TextBoxBase || c is ListView)) continue;
                if (!c.Visible || c.Width < 4 || c.Height < 4) continue;
                if (c is Label && string.IsNullOrEmpty(c.Text)) continue;
                Rectangle visible = VisibleOnScreen(c);
                if (visible.Width < 4 || visible.Height < 4) continue;
                var inCapture = new Rectangle(visible.X - f.Left, visible.Y - f.Top, visible.Width, visible.Height);
                inCapture.Intersect(new Rectangle(Point.Empty, capture.Size));
                if (inCapture.Width < 4 || inCapture.Height < 4 || !Uniform(capture, inCapture)) continue;
                Rectangle screen = c.RectangleToScreen(new Rectangle(Point.Empty, c.Size));
                using (var own = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppArgb))
                {
                    c.DrawToBitmap(own, new Rectangle(Point.Empty, c.Size));
                    var ownPart = new Rectangle(visible.X - screen.X, visible.Y - screen.Y, visible.Width, visible.Height);
                    ownPart.Intersect(new Rectangle(Point.Empty, own.Size));
                    if (ownPart.Width >= 4 && ownPart.Height >= 4 && !Uniform(own, ownPart)) blank.Add(c);
                }
            }
            return blank;
        }

        /// <summary>The part of a control on the screen that its parents don't clip away.</summary>
        private static Rectangle VisibleOnScreen(Control c)
        {
            Rectangle visible = c.RectangleToScreen(new Rectangle(Point.Empty, c.Size));
            for (Control? p = c.Parent; p != null; p = p.Parent) visible.Intersect(p.RectangleToScreen(p.ClientRectangle));
            return visible;
        }

        /// <summary>Draws a control's own painting into the capture, where it shows.</summary>
        private static void DrawInto(Form f, Bitmap capture, Control c)
        {
            Rectangle visible = VisibleOnScreen(c);
            Rectangle screen = c.RectangleToScreen(new Rectangle(Point.Empty, c.Size));
            using (var own = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(capture))
            {
                c.DrawToBitmap(own, new Rectangle(Point.Empty, c.Size));
                g.DrawImage(own, new Rectangle(visible.X - f.Left, visible.Y - f.Top, visible.Width, visible.Height),
                    new Rectangle(visible.X - screen.X, visible.Y - screen.Y, visible.Width, visible.Height), GraphicsUnit.Pixel);
            }
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control child in Descendants(c)) yield return child;
            }
        }

        /// <summary>True if every pixel of <paramref name="area"/> has the same color.</summary>
        private static bool Uniform(Bitmap bmp, Rectangle area)
        {
            BitmapData data = bmp.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[area.Width];
                int first = 0;
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, area.Width);
                    if (y == 0) first = row[0];
                    foreach (int px in row)
                    {
                        if (px != first) return false;
                    }
                }
                return true;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static bool TryPrintWindow(Form f, Bitmap bmp)
        {
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok;
                try
                {
                    ok = NativeTheme.TryPrintWindow(f.Handle, hdc);
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
                if (!ok) return false;
            }
            return !SingleColor(bmp);
        }

        /// <summary>True if a grid of samples over the image is all one color (a capture that didn't work).</summary>
        private static bool SingleColor(Bitmap bmp)
        {
            int first = bmp.GetPixel(0, 0).ToArgb();
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int px = bmp.GetPixel(Math.Min(bmp.Width - 1, x * bmp.Width / 16 + 1), Math.Min(bmp.Height - 1, y * bmp.Height / 16 + 1)).ToArgb();
                    if (px != first) return false;
                }
            }
            return true;
        }

        private static void Pump()
        {
            for (int i = 0; i < 8; i++)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        /// <summary>
        /// The icons of one theme: the status shield in each kind and size on the card color, the UAC shield at 16, 20, 24
        /// and 32 px, every glyph at 16, 20 and 24 epx, and the app icon's frames.
        /// </summary>
        private static Size RenderIcons(string path, Theme theme)
        {
            int[] sizes = { 16, 24, 32, 40, 48, 64, 96 };
            ShieldKind[] kinds = { ShieldKind.On, ShieldKind.Off, ShieldKind.Attention };
            int[] shieldPx = { 16, 20, 24, 32 };
            int[] glyphEpx = { 16, 20, 24 };
            int[] appSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            Array glyphs = Enum.GetValues(typeof(Glyph));
            UiScale s = UiScale.Current;
            const int cell = 110;
            int glyphCell = s.Px(40);
            int width = Math.Max(Math.Max(sizes.Length * cell, glyphs.Length * glyphCell + 16), 16 + 20 + 24 + 32 + 40 + 48 + 64 + 256 + appSizes.Length * 10);
            int shieldRows = kinds.Length * cell;
            int uacRow = 60;
            int glyphRows = glyphEpx.Length * s.Px(36) + 16;
            int height = shieldRows + uacRow + glyphRows + 280;
            using (var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                using (var back = new SolidBrush(theme.WindowBackground)) g.FillRectangle(back, 0, 0, width, height);
                using (var card = new SolidBrush(theme.CardBackground)) g.FillRectangle(card, 0, 0, width, shieldRows + uacRow + glyphRows);
                int y = 0;
                foreach (ShieldKind kind in kinds)
                {
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        int z = sizes[i];
                        ShieldPainter.Paint(g, new Rectangle(i * cell + (cell - z) / 2, y + (cell - z) / 2, z, z),
                            ShieldPainter.FillFor(kind, theme), ShieldPainter.SymbolFor(kind, theme), kind);
                    }
                    y += cell;
                }

                int x = 8;
                foreach (int px in shieldPx)
                {
                    UacShield.Draw(g, new Rectangle(x, y + (uacRow - px) / 2, px, px), disabled: false);
                    UacShield.Draw(g, new Rectangle(x + px + 8, y + (uacRow - px) / 2, px, px), disabled: true);
                    x += 2 * px + 32;
                }
                y += uacRow;

                foreach (int epx in glyphEpx)
                {
                    int px = s.Px(epx);
                    x = 8;
                    foreach (Glyph glyph in glyphs)
                    {
                        GlyphFont.Draw(g, glyph, new Rectangle(x + (glyphCell - px) / 2, y + (s.Px(36) - px) / 2, px, px), theme.TextPrimary);
                        x += glyphCell;
                    }
                    y += s.Px(36);
                }
                y += 16;

                using (Stream? stream = typeof(RenderUi).Assembly.GetManifestResourceStream(AppIcon.ResourceName))
                {
                    if (stream != null)
                    {
                        var bytes = new MemoryStream();
                        stream.CopyTo(bytes);
                        byte[] ico = bytes.ToArray();
                        x = 8;
                        y += 12;
                        foreach (int z in appSizes)
                        {
                            // System.Drawing.Icon can't read PNG frames (it would scale up a smaller one): draw those as images.
                            byte[]? png = PngFrame(ico, z);
                            if (png != null)
                            {
                                using (var image = Image.FromStream(new MemoryStream(png))) g.DrawImage(image, new Rectangle(x, y, z, z));
                            }
                            else
                            {
                                bytes.Position = 0;
                                using (var icon = new Icon(bytes, new Size(z, z))) g.DrawIcon(icon, new Rectangle(x, y, z, z));
                            }
                            x += z + 10;
                        }
                    }
                }
                bmp.Save(path, ImageFormat.Png);
                return bmp.Size;
            }
        }

        /// <summary>The PNG bytes of the .ico frame of that size, or null if the frame is a bitmap (or missing).</summary>
        private static byte[]? PngFrame(byte[] ico, int size)
        {
            int count = BitConverter.ToUInt16(ico, 4);
            for (int i = 0; i < count; i++)
            {
                int entry = 6 + 16 * i;
                int width = ico[entry] == 0 ? 256 : ico[entry];
                int length = BitConverter.ToInt32(ico, entry + 8);
                int offset = BitConverter.ToInt32(ico, entry + 12);
                if (width != size || offset + length > ico.Length || ico[offset] != 0x89) continue;
                var png = new byte[length];
                Array.Copy(ico, offset, png, 0, length);
                return png;
            }
            return null;
        }

        /// <summary>The desktop and error-mode calls of the renderer (Debug only).</summary>
        private static class NativeMethods
        {
            public const uint SEM_FAILCRITICALERRORS = 0x0001;
            public const uint SEM_NOGPFAULTERRORBOX = 0x0002;
            public const uint GENERIC_ALL = 0x10000000;
            private const int UOI_NAME = 2;

            /// <summary>WM_CHANGEUISTATE with UIS_SET, UISF_HIDEFOCUS and UISF_HIDEACCEL: no focus rectangles, no access-key underlines.</summary>
            public static void HideKeyboardCues(IntPtr window) => SendMessage(window, 0x0127, new IntPtr((0x1 | 0x2) << 16 | 1), IntPtr.Zero);

            [DllImport("user32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

            /// <summary>RedrawWindow with RDW_INVALIDATE, RDW_ERASE, RDW_FRAME, RDW_ALLCHILDREN and RDW_UPDATENOW: paints the window and every child now.</summary>
            public static void RedrawNow(IntPtr window) => RedrawWindow(window, IntPtr.Zero, IntPtr.Zero, 0x1 | 0x4 | 0x400 | 0x80 | 0x100);

            [DllImport("user32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);

            /// <summary>Waits until the desktop window manager has composed what was drawn (DwmFlush); nothing if it fails.</summary>
            public static void Flush()
            {
                try
                {
                    DwmFlush();
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            }

            [DllImport("dwmapi.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern int DwmFlush();

            /// <summary>The name of the calling thread's desktop ("" if Windows doesn't say).</summary>
            public static string CurrentDesktopName()
            {
                IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
                var name = new StringBuilder(256);
                return desktop != IntPtr.Zero && GetUserObjectInformation(desktop, UOI_NAME, name, name.Capacity * 2, out _) ? name.ToString() : string.Empty;
            }

            [DllImport("kernel32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            public static extern uint SetErrorMode(uint mode);

            [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            public static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr deviceMode, int flags, uint access, IntPtr attributes);

            [DllImport("user32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetThreadDesktop(IntPtr desktop);

            [DllImport("user32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseDesktop(IntPtr desktop);

            [DllImport("user32.dll", SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern IntPtr GetThreadDesktop(int threadId);

            [DllImport("kernel32.dll")]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            private static extern int GetCurrentThreadId();

            [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
            [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
            [return: MarshalAs(UnmanagedType.Bool)]
            private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
        }
    }
}
#endif
