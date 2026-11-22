using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using QuietHosts.Core;

namespace QuietHosts
{
    /// <summary>What every window needs: the environment, the embedded lists and how the window was started.</summary>
    internal sealed class UiContext
    {
        /// <param name="signature">
        /// The exe's signature as previews show it (render scenarios, tests); null reads the real exe's signature on first
        /// use, offline (in a preview: Unsigned, without reading anything).
        /// </param>
        public UiContext(AppEnvironment env, BlockLists lists, string exePath, RunLocationTips tips, bool windowIsElevated, bool preview,
            SignatureState? signature = null)
        {
            Env = env ?? throw new ArgumentNullException(nameof(env));
            Lists = lists ?? throw new ArgumentNullException(nameof(lists));
            ExePath = exePath;
            Tips = tips;
            WindowIsElevated = windowIsElevated;
            Preview = preview;
            if (signature != null) Signature = new Lazy<SignatureState>(() => signature);
            else if (preview) Signature = new Lazy<SignatureState>(() => new SignatureState(SignatureKind.Unsigned, null));
            else Signature = new Lazy<SignatureState>(() => SignatureCheck.Read(exePath));
        }

        /// <summary>Whether this exe is signed (read offline on first use: the first-run panel or About).</summary>
        public Lazy<SignatureState> Signature { get; }

        public AppEnvironment Env { get; }
        public BlockLists Lists { get; }
        public string ExePath { get; }
        /// <summary>The run-location tips, checked once at start.</summary>
        public RunLocationTips Tips { get; }
        /// <summary>The user chose "Run as administrator" for the window itself (links then open through explorer.exe).</summary>
        public bool WindowIsElevated { get; }
        /// <summary>--render-ui and tests: fake statuses; nothing is started, opened or saved.</summary>
        public bool Preview { get; }

        /// <summary>The main window's title ("(sandbox)" is added in the Debug sandbox).</summary>
        public string MainTitle
        {
            get
            {
#if DEBUG
                if (Env.IsSandbox) return Strings.AppName + Strings.SandboxTitleSuffix;
#endif
                return Strings.AppName;
            }
        }
    }

    /// <summary>
    /// Opens links and folders. Nothing here goes online: the browser does. When opening fails, QuietHosts' own dialog shows
    /// the address or folder with a Copy button (spec 6.5), not a message box.
    /// </summary>
    internal static class Links
    {
        private static string ExplorerPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

        /// <summary>
        /// Opens a web page in the user's browser. When the window itself runs elevated, explorer.exe opens it,
        /// so the browser doesn't run as administrator.
        /// </summary>
        public static void OpenUrl(UiContext ctx, IWin32Window? owner, string url)
        {
            if (!MayStart(ctx)) return;
            ProcessStartInfo psi = ctx.WindowIsElevated
                ? new ProcessStartInfo(ExplorerPath, Quote(url)) { UseShellExecute = false }
                : new ProcessStartInfo(url) { UseShellExecute = true };
            if (!TryStart(psi)) ShowFailed(ctx, owner, url, isFolder: false);
        }

        /// <summary>Opens a folder in Explorer ("Open backup folder").</summary>
        public static void OpenFolder(UiContext ctx, IWin32Window? owner, string folder)
        {
            if (!MayStart(ctx) || string.IsNullOrEmpty(folder)) return;
            if (!TryStart(new ProcessStartInfo(ExplorerPath, Quote(folder)) { UseShellExecute = false })) ShowFailed(ctx, owner, folder, isFolder: true);
        }

        /// <summary>A preview starts nothing (tests can put a fake start in its place).</summary>
        private static bool MayStart(UiContext ctx)
        {
#if DEBUG || QUIETHOSTS_UI_TESTS
            if (PreviewStart != null) return true;
#endif
            return !ctx.Preview;
        }

        private static bool TryStart(ProcessStartInfo psi)
        {
            try
            {
#if DEBUG || QUIETHOSTS_UI_TESTS
                if (PreviewStart != null)
                {
                    PreviewStart(psi);
                    return true;
                }
#endif
                Process.Start(psi)?.Dispose();
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                return false;
            }
        }

        private static void ShowFailed(UiContext ctx, IWin32Window? owner, string target, bool isFolder)
        {
            using (var d = new OpenFailedDialog(ctx, target, isFolder)) d.ShowDialog(owner);
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", string.Empty) + "\"";

#if DEBUG || QUIETHOSTS_UI_TESTS
        /// <summary>Tests: starts in place of Process.Start (throw to make opening fail), also for a preview context.</summary>
        internal static Action<ProcessStartInfo>? PreviewStart { get; set; }
#endif
    }

    /// <summary>
    /// The text "Copy details" puts on the clipboard: LastRun, the versions, the Windows build and
    /// the result. Never file contents or address names; the profile path shows as %USERPROFILE%.
    /// </summary>
    internal static class DetailsReport
    {
        public static string Build(UiContext? ctx, ElevatedCommand? command, LaunchResult? result, HostsStatus? status, Exception? windowError,
            SelfCheckResult? selfCheck = null)
        {
            var sb = new StringBuilder();
            Line(sb, Strings.DetailsApp, Strings.AppName + " " + AppInfo.Version);
            if (ctx != null) Line(sb, Strings.DetailsLists, ctx.Lists.Version.Length > 0 ? ctx.Lists.Version : Strings.DetailsHostsUnknown);
            Line(sb, Strings.DetailsWindows, Diagnostics.WindowsBuild());
            Line(sb, Strings.DetailsTextSize, TextSizeValue());
            if (ctx != null) Line(sb, Strings.DetailsElevatedWindow, ctx.WindowIsElevated ? Strings.Yes : Strings.No);
            if (command != null) Line(sb, Strings.DetailsAction, command.Describe());
            if (result != null)
            {
                Line(sb, Strings.DetailsResult, result.Kind.ToString());
                Line(sb, Strings.DetailsExitCode, result.ExitCode.HasValue ? result.ExitCode.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : Strings.DetailsNoExitCode);
                bool? changed = result.HostsFileChanged;
                Line(sb, Strings.DetailsHostsFile, changed == null ? Strings.DetailsHostsUnknown : changed.Value ? Strings.DetailsHostsChanged : Strings.DetailsHostsUnchanged);
                if (result.Error != null) Line(sb, Strings.DetailsError, result.Error.GetType().FullName + ": " + result.Error.Message);
            }
            if (selfCheck != null) Line(sb, Strings.DetailsSelfCheck, (selfCheck.Passed ? Strings.DetailsPassed : Strings.DetailsFailed) + ": " + selfCheck.Detail);
            if (windowError != null) Line(sb, Strings.DetailsError, windowError.GetType().FullName + ": " + windowError.Message);
            if (status != null)
            {
                Line(sb, Strings.DetailsState, StateText(status));
                if (status.ReadError != null) Line(sb, Strings.DetailsError, status.ReadError.GetType().FullName + ": " + status.ReadError.Message);
            }
            if (windowError != null && windowError.StackTrace != null)
            {
                // The frames without file names and line numbers: a locally built exe's paths stay out of a public report.
                string frames = new StackTrace(windowError, false).ToString();
                sb.Append("\r\n").Append(frames.Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd()).Append("\r\n");
            }
            sb.Append("\r\n").Append(Strings.DetailsLastRun).Append(":\r\n");
            string? lastRun = status?.LastRun;
            if (string.IsNullOrEmpty(lastRun)) sb.Append(Strings.DetailsNoLastRun).Append("\r\n");
            else sb.Append(lastRun!.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            return Diagnostics.MaskProfilePath(sb.ToString());
        }

        /// <summary>
        /// The Fix dialog's "Copy details" (spec 5.12), for a broken-site report: the versions, the Windows build, the state, the
        /// level, how many addresses are allowed and the text size. Never an address's name and no LastRun.
        /// </summary>
        public static string BuildSiteReport(UiContext ctx, HostsStatus status)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (status == null) throw new ArgumentNullException(nameof(status));
            var sb = new StringBuilder();
            Line(sb, Strings.DetailsApp, Strings.AppName + " " + AppInfo.Version);
            Line(sb, Strings.DetailsLists, ctx.Lists.Version.Length > 0 ? ctx.Lists.Version : Strings.DetailsHostsUnknown);
            Line(sb, Strings.DetailsWindows, Diagnostics.WindowsBuild());
            Line(sb, Strings.DetailsState, StateText(status));
            Line(sb, Strings.DetailsLevel, Strings.LevelName(status.BlockLevel ?? status.SelectedLevel));
            Line(sb, Strings.DetailsAllowedCount, Strings.Number(status.AllowedCount));
            Line(sb, Strings.DetailsTextSize, TextSizeValue());
            return Diagnostics.MaskProfilePath(sb.ToString());
        }

        /// <summary>"On", "Refused (TooLarge)", "On, EditedBlock, LargeFile": the state, the refusal and the warnings.</summary>
        private static string StateText(HostsStatus status)
        {
            string state = status.State.ToString();
            if (status.State == ProtectionState.Refused) state += " (" + status.Refusal + ")";
            if (status.Warnings != HostsWarnings.None) state += ", " + status.Warnings;
            return state;
        }

        private static void Line(StringBuilder sb, string label, string value) => sb.Append(label).Append(": ").Append(value).Append("\r\n");

        /// <summary>
        /// "150% (message font 13.5 pt)": Windows' text size and the message font's size, so a report shows whether the font
        /// follows the text-size slider (2.6). Read-only.
        /// </summary>
        internal static string TextSizeValue()
        {
            float points;
            try
            {
                points = TypeRamp.MessageBoxPoints();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is System.Runtime.InteropServices.ExternalException)
            {
                points = 0f;
            }
            return Strings.DetailsTextSizeValue(TypeRamp.TextScalePercent(TypeRamp.ReadTextScaleFactor()), points);
        }

        /// <summary>Puts text on the clipboard, retrying briefly if another program holds it.</summary>
        public static bool TryCopy(string text)
        {
            try
            {
                Clipboard.SetDataObject(text, true, 5, 100);
                return true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException || ex is ThreadStateException)
            {
                return false;
            }
        }
    }
}
