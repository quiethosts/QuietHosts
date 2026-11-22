using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests.Ui
{
    /// <summary>
    /// Every test that builds a window runs in this collection: one at a time, after the other tests. Forms built on
    /// several threads at once now and then measure their text wrong (a 540-pixel main window came out 389 to 667 pixels
    /// wide in a probe with six threads, never with one), and the Debug-only text scale is global. The app itself has one
    /// UI thread.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class WindowCollection
    {
        public const string Name = "Windows";
    }

    /// <summary>
    /// Runs WinForms code on a fresh STA thread and rethrows what it threw, or else the first exception a window procedure
    /// threw (WinForms would show its error dialog for that, on the test desktop, where nobody can close it). The thread's
    /// windows live on <see cref="TestDesktop"/>, so whoever uses the PC never sees them and never clicks or types into them.
    /// </summary>
    internal static class Sta
    {
        public static void Run(Action action)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    // First: a thread that has a window can't move to another desktop.
                    TestDesktop.Enter();
                    Application.EnableVisualStyles();
                    // The normal text size (9 pt), whatever this PC's Windows text size is.
                    TypeRamp.PreviewBasePoints = UiGlobals.TestBasePoints;
                    Exception? windowError = null;
                    Application.ThreadException += (s, e) => { if (windowError == null) windowError = e.Exception; };
                    action();
                    if (windowError != null) throw new InvalidOperationException("A window procedure failed.", windowError);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>A screen large enough for every window at the normal text size.</summary>
        public static readonly System.Drawing.Rectangle LargeScreen = new System.Drawing.Rectangle(0, 0, 2560, 1400);

        /// <summary>
        /// Shows a window (on the test desktop, which nobody sees) far off its screen without activating it, as --render-ui
        /// does on its own desktop: some WinForms behavior (a button's click, for one) needs a visible window. Unless the
        /// test gave one, the window fits <see cref="LargeScreen"/>, not this PC's screen, so its size never depends on it.
        /// </summary>
        public static void ShowOffScreen(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-20000, -20000);
            form.ShowInTaskbar = false;
            if (form is IPreviewWindow w)
            {
                w.PreviewNoActivate = true;
                if (w.PreviewWorkingArea == null) w.PreviewWorkingArea = LargeScreen;
            }
            form.Show();
            for (int i = 0; i < 4; i++) Application.DoEvents();
        }
    }

    /// <summary>
    /// A desktop of the test process's own, created once and never switched to (it lives as long as the process). Windows
    /// on it don't show on the screen, get no taskbar button and no mouse or keyboard input, and never take the focus from
    /// whoever uses the PC, even a minimized one, which Windows otherwise draws as a small title bar on the screen, where a
    /// click restores it in front. Window tests never fall back to the user's desktop: if Windows refuses the test desktop,
    /// <see cref="Enter"/> throws and the test fails. Before the desktop is created, the test process stops Windows Error
    /// Reporting from showing a window if it crashes (SEM_NOGPFAULTERRORBOX), as --render-ui does.
    /// </summary>
    internal static class TestDesktop
    {
        private const uint GenericAll = 0x10000000;
        private const int UOI_NAME = 2;
        private const uint SEM_FAILCRITICALERRORS = 0x0001, SEM_NOGPFAULTERRORBOX = 0x0002;
        private static readonly Lazy<(IntPtr Handle, int Error)> Desktop = new Lazy<(IntPtr, int)>(Create);

        private static (IntPtr Handle, int Error) Create()
        {
            // First, before the first window test makes any window: no crash dialog on the user's desktop.
            uint previous = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
            SetErrorMode(previous | SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
            IntPtr handle = CreateDesktop("QuietHosts.Tests." + Guid.NewGuid().ToString("N"), IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
            return (handle, handle == IntPtr.Zero ? Marshal.GetLastWin32Error() : 0);
        }

        /// <summary>The test desktop exists.</summary>
        public static bool Available => Desktop.Value.Handle != IntPtr.Zero;

        /// <summary>
        /// Moves the calling thread to the test desktop; it must not have any window yet. Throws if Windows refused the
        /// desktop or the move: a window test never runs on the user's desktop.
        /// </summary>
        public static void Enter()
        {
            (IntPtr handle, int error) = Desktop.Value;
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("Windows refused the test desktop (CreateDesktop error " + error.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "). Window tests never run on the user's desktop.");
            if (!SetThreadDesktop(handle))
                throw new InvalidOperationException("The test thread couldn't move to the test desktop (SetThreadDesktop error " +
                    Marshal.GetLastWin32Error().ToString(System.Globalization.CultureInfo.InvariantCulture) + ").");
        }

        /// <summary>The name of the calling thread's desktop.</summary>
        public static string CurrentName() => NameOf(GetThreadDesktop(GetCurrentThreadId()));

        /// <summary>The name of the test desktop, or null if Windows refused it.</summary>
        public static string? Name => Available ? NameOf(Desktop.Value.Handle) : null;

        private static string NameOf(IntPtr desktop)
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformation(desktop, UOI_NAME, name, name.Capacity * 2, out _) ? name.ToString() : string.Empty;
        }

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint SetErrorMode(uint mode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr deviceMode, int flags, uint access, IntPtr attributes);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetThreadDesktop(IntPtr desktop);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern IntPtr GetThreadDesktop(int threadId);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int GetCurrentThreadId();

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder info, int length, out int needed);
    }

    /// <summary>
    /// A preview context: a test environment under %TEMP% (in-memory stores) and the embedded lists. Nothing is
    /// started, opened or saved, and nothing reads the real hosts file.
    /// </summary>
    internal sealed class PreviewContext : IDisposable
    {
        private readonly TempDir _dir = new TempDir();

        public PreviewContext(RunLocationTips tips = RunLocationTips.None)
        {
            Env = AppEnvironment.ForTesting(_dir.Path);
            Lists = BlockLists.LoadEmbedded();
            Context = new UiContext(Env, Lists, @"C:\Apps\QuietHosts\QuietHosts.exe", tips, windowIsElevated: false, preview: true);
        }

        public AppEnvironment Env { get; }
        public BlockLists Lists { get; }
        public UiContext Context { get; }

        public void Dispose() => _dir.Dispose();
    }

    /// <summary>
    /// The globals a UI test may change (theme, DPI, text scale, render and test overrides), back to their defaults. Every
    /// test that changes one calls this in a finally block, so no test depends on another's leftovers or on the PC.
    /// </summary>
    internal static class UiGlobals
    {
        /// <summary>
        /// The base font size every window test uses: the message-box font at the normal Windows text size (100%). Tests
        /// that need large text multiply it with UiFonts.TextScale (Debug), so no test depends on this PC's text size.
        /// </summary>
        public const float TestBasePoints = 9f;

        public static void Reset()
        {
            ThemeManager.ResetForTests();
            UiScale.ResetForTests();
            UiOverrides.ForceFocusCues = false;
            GlyphFont.PreviewNoIconFont = false;
            ProgressRing.AnimationsOverride = null;
            TypeRamp.PreviewBasePoints = TestBasePoints;
#if DEBUG
            UiFonts.TextScale = 1f;
#endif
        }
    }

    /// <summary>Pixel captures for checks on owner-drawn components (DrawToBitmap works on the test desktop, Appendix A.2).</summary>
    internal static class Pixels
    {
        /// <summary>The control as it paints itself (with its parent's surface behind transparent parts).</summary>
        public static System.Drawing.Bitmap Of(Control c)
        {
            var bmp = new System.Drawing.Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            c.DrawToBitmap(bmp, new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size));
            return bmp;
        }

        /// <summary>A pixel's RGB as 0xRRGGBB.</summary>
        public static int Rgb(System.Drawing.Bitmap bmp, int x, int y) => bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;

        public static int Rgb(System.Drawing.Color c) => c.ToArgb() & 0xFFFFFF;
    }

    /// <summary>A plain redesigned window for component tests: a page with one transparent column of controls.</summary>
    internal sealed class TestWindow : ThemedWindow
    {
        public TestWindow()
        {
            Text = "Test window";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Column = new ThemedTable(1) { Padding = new Padding(Scale.Px(Metrics.PagePadding)) };
            Controls.Add(Column);
            EndLayout();
        }

        /// <summary>The page's column: add controls with <see cref="Add"/>.</summary>
        public ThemedTable Column { get; }

        public TypeRamp Fonts => Ramp;

        public T Add<T>(T control) where T : Control
        {
            control.Margin = new Padding(0, 0, 0, Scale.Px(Metrics.ButtonGap));
            global::QuietHosts.Ui.AddRow(Column, control);
            return control;
        }
    }

    /// <summary>A small redesigned dialog with as much text as asked for, a multi-line text box and Close in the footer.</summary>
    internal sealed class TestDialog : ThemedDialog
    {
        public TestDialog(int paragraphs) : base("Test dialog")
        {
            for (int i = 0; i < paragraphs; i++)
                global::QuietHosts.Ui.AddRow(Body, new ThemedLabel(Ramp, "Paragraph " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    ": text that has no focusable control next to it, so only the keyboard scrolling reaches it.", TextStyle.Body, TextRole.Primary, 432)
                    { Margin = new Padding(0, 0, 0, Scale.Px(Metrics.HeadingGap)) });
            Notes = new TextBox { Multiline = true, Height = Scale.Px(60), Width = Scale.Px(400), AccessibleName = "Notes", Text = "one\r\ntwo\r\nthree\r\nfour\r\nfive" };
            global::QuietHosts.Ui.AddRow(Body, Notes);
            CloseControl = new ThemedButton(Ramp, Strings.CloseButton, ButtonStyle.Accent);
            Footer.SetRight(CloseControl);
            CancelButton = CloseControl;
            Finish(CloseControl, "Test dialog. Paragraphs.");
        }

        public TextBox Notes { get; }
        public ThemedButton CloseControl { get; }
        public ScrollHost? Host => ContentHost;
        public int BodyLine => Ramp.LineHeight(TextStyle.Body);
    }

    internal static class ControlTree
    {
        public static IEnumerable<Control> All(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (Control child in All(c)) yield return child;
            }
        }

        private static readonly MethodInfo GetState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
            ?? throw new InvalidOperationException("Control.GetState not found.");

        private static readonly MethodInfo CreateControl = typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)
            ?? throw new InvalidOperationException("Control.CreateControl(bool) not found.");

        /// <summary>
        /// Creates the window handles of a form and all its controls without showing it, as Show() would.
        /// A ListView keeps its selection in its handle, so the Fix dialog's tests need this.
        /// </summary>
        public static void CreateHandles(Form form) => CreateControl.Invoke(form, new object[] { true });

        /// <summary>
        /// True if the control and its parents are set to be visible. Control.Visible is false for every control of
        /// a form that hasn't been shown, so this reads the controls' own flags (STATE_VISIBLE).
        /// </summary>
        public static bool IsMeantToShow(Control c, Control root)
        {
            for (Control? x = c; x != null && x != root; x = x.Parent)
            {
                if (!(bool)GetState.Invoke(x, new object[] { 0x00000002 })) return false;
            }
            return true;
        }

        /// <summary>The access key in a control's text ('&amp;' followed by a character), or null.</summary>
        public static char? AccessKey(string text)
        {
            for (int i = 0; i < text.Length - 1; i++)
            {
                if (text[i] != '&') continue;
                if (text[i + 1] == '&') { i++; continue; }
                return char.ToUpperInvariant(text[i + 1]);
            }
            return null;
        }
    }
}
