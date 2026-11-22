using System;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    internal static class Program
    {
        private const string WindowMutexName = @"Local\QuietHosts.Window";
#if DEBUG
        private const string SandboxWindowMutexName = @"Local\QuietHosts.Sandbox.Window";
#endif
        /// <summary>Exit code for a command line the window doesn't accept. Nothing is shown.</summary>
        private const int BadArguments = 2;

        [STAThread]
        private static int Main(string[] args)
        {
            // An elevated run makes one change and has no UI: it goes to Core before any WinForms initialization.
            if (ElevatedCommand.IsElevatedInvocation(args)) return ElevatedMain.Run(args);

            bool sandbox = false;
            if (args.Length > 0)
            {
#if DEBUG
                // Developer options. Release builds accept no options at all.
                if (args.Length == 1 && string.Equals(args[0], RenderUi.Option, StringComparison.Ordinal))
                {
                    InitWinForms();
                    return RenderUi.Run();
                }
                if (args.Length != 1 || !string.Equals(args[0], ElevatedCommand.SandboxOption, StringComparison.Ordinal)) return BadArguments;
                sandbox = true;
#else
                return BadArguments;
#endif
            }

            InitWinForms();
            string mutexName = WindowMutexName;
#if DEBUG
            if (sandbox) mutexName = SandboxWindowMutexName;
#endif
            using (SingleInstance? instance = SingleInstance.Acquire(mutexName))
            {
                // A window is already open: it was asked to come forward.
                if (instance == null) return 0;
                return RunWindow(instance, sandbox);
            }
        }

        private static void InitWinForms()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        private static int RunWindow(SingleInstance instance, bool sandbox)
        {
            UiContext ctx;
            HostsStatus status;
            try
            {
#if DEBUG
                AppEnvironment env = sandbox ? AppEnvironment.Sandbox() : AppEnvironment.Real();
#else
                AppEnvironment env = AppEnvironment.Real();
#endif
                BlockLists lists = BlockLists.LoadEmbedded();
                // The exe's file path, which each change starts elevated. Not Application.ExecutablePath: that one goes
                // through a file URI and decodes "%41"-style sequences in folder names (C:\Tools\50%25 becomes C:\Tools\50%),
                // so every change would fail to start a file that isn't there.
                string exe = typeof(Program).Assembly.Location;
                ctx = new UiContext(env, lists, exe, RunLocation.Check(exe), IsWindowElevated(), preview: false);
                // Read before the window exists, so a locked hosts file can't freeze it.
                status = StatusReader.Read(env, lists);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // No theme exists yet: the one message box after start-up (spec 6.5).
                MessageBox.Show(Strings.WindowErrorTitle + "\r\n\r\n" + Strings.WindowErrorBody + "\r\n\r\n" + ex.GetType().FullName + ": " + ex.Message,
                    Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }

            // Before the first window or control: the theme (light, dark on Windows 11, high contrast) and its live switching.
            ThemeManager.Initialize();
            var controller = new AppController(ctx, status);
            Application.ThreadException += (s, e) => ErrorDialog.ShowWindowError(Form.ActiveForm, ctx, e.Exception);
            // The first-run text: a user's first launch while protection is off.
            bool firstRun = !status.FirstRunDone && (status.State == ProtectionState.Off || status.State == ProtectionState.OlderList);
            using (var form = new MainForm(controller, firstRun))
            {
                instance.Listen(() =>
                {
                    try
                    {
                        if (!form.IsDisposed && form.IsHandleCreated) form.BeginInvoke(new Action(form.ShowFromSecondLaunch));
                    }
                    catch (InvalidOperationException)
                    {
                        // The window is closing.
                    }
                });
                Application.Run(form);
            }
            return 0;
        }

        private static bool IsWindowElevated()
        {
            try { return ElevatedMain.IsProcessElevated(); }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException) { return false; }
        }
    }
}
