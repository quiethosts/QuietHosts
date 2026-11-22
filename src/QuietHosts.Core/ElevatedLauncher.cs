using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace QuietHosts.Core
{
    /// <summary>Where a change is: before the Windows prompt is answered, and after.</summary>
    public enum LaunchStage
    {
        /// <summary>"Waiting for your permission..."</summary>
        WaitingForPermission,
        /// <summary>"Changing the hosts file..."</summary>
        Changing,
        /// <summary>
        /// The elevated run started <see cref="ElevatedLauncher.TakingLongAfter"/> ago and is still running. Reported at
        /// most once, after <see cref="Changing"/>; the launcher keeps waiting for the run (it never stops or abandons it).
        /// </summary>
        TakingLong,
    }

    /// <summary>The result of one change, as the window sees it.</summary>
    public sealed class LaunchResult
    {
        public LaunchResult(ResultKind kind, int? exitCode, Exception? error, string? before, string? after)
        {
            Kind = kind;
            ExitCode = exitCode;
            Error = error;
            HostsSha256Before = before;
            HostsSha256After = after;
        }

        public ResultKind Kind { get; }
        /// <summary>The child's exit code; null if it never ran (Declined, or it couldn't start).</summary>
        public int? ExitCode { get; }
        /// <summary>The exception that stopped the launch, if any (for Copy details).</summary>
        public Exception? Error { get; }
        /// <summary>SHA-256 hex of the hosts file just before the launch (null if unreadable).</summary>
        public string? HostsSha256Before { get; }
        /// <summary>SHA-256 hex of the hosts file just after the child exited (null if unreadable).</summary>
        public string? HostsSha256After { get; }

        /// <summary>
        /// For the two forms of Unexpected: false = "your hosts file is unchanged",
        /// true = "your hosts file may be incomplete", null = unknown (treat as may be incomplete).
        /// </summary>
        public bool? HostsFileChanged =>
            HostsSha256Before == null || HostsSha256After == null ? (bool?)null : !string.Equals(HostsSha256Before, HostsSha256After, StringComparison.Ordinal);
    }

    /// <summary>Starts a process and waits for its exit code. A seam for tests.</summary>
    public interface IProcessRunner
    {
        /// <summary>
        /// Starts the process, calls <paramref name="started"/> once it runs, waits for it to exit and returns
        /// its exit code. Throws Win32Exception 1223 when the user declines the Windows prompt.
        /// </summary>
        int RunAndWait(ProcessStartInfo startInfo, Action started);
    }

    public sealed class ShellProcessRunner : IProcessRunner
    {
        public static ShellProcessRunner Instance { get; } = new ShellProcessRunner();

        public int RunAndWait(ProcessStartInfo startInfo, Action started)
        {
            using (Process? p = Process.Start(startInfo))
            {
                if (p == null) throw new InvalidOperationException("The elevated run did not start.");
                started();
                p.WaitForExit();
                return p.ExitCode;
            }
        }
    }

    /// <summary>
    /// The window side of a change: starts the same exe hidden with "--elevated ..." (runas for the
    /// real environment; without runas and with "--sandbox" in the Debug sandbox), waits off the UI thread,
    /// and maps the exit code. Win32Exception 1223 means Declined. No WinForms here: the window passes the exe's
    /// file path (its assembly's Location, not Application.ExecutablePath, which decodes "%41" in folder names).
    /// </summary>
    public sealed class ElevatedLauncher
    {
        public const int ErrorCancelled = 1223;
        /// <summary>
        /// When a started elevated run reports <see cref="LaunchStage.TakingLong"/>. A normal run takes a few seconds (the
        /// 2-second re-check and the DNS flush); only many retries on a busy file plus a slow flush, or something holding
        /// the run, get it this far.
        /// </summary>
        public static readonly TimeSpan TakingLongAfter = TimeSpan.FromSeconds(30);
        private readonly AppEnvironment _env;
        private readonly string _exePath;
        private readonly IProcessRunner _runner;
        private readonly TimeSpan _takingLongAfter;

        public ElevatedLauncher(AppEnvironment env, string exePath) : this(env, exePath, ShellProcessRunner.Instance) { }

        public ElevatedLauncher(AppEnvironment env, string exePath, IProcessRunner runner) : this(env, exePath, runner, TakingLongAfter) { }

        /// <summary>For tests: a shorter wait before <see cref="LaunchStage.TakingLong"/>.</summary>
        internal ElevatedLauncher(AppEnvironment env, string exePath, IProcessRunner runner, TimeSpan takingLongAfter)
        {
            _env = env ?? throw new ArgumentNullException(nameof(env));
            if (string.IsNullOrWhiteSpace(exePath)) throw new ArgumentException("The exe path is needed.", nameof(exePath));
            _exePath = exePath;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _takingLongAfter = takingLongAfter;
        }

        /// <summary>
        /// The start info for a command: runas and hidden for the real environment; no runas in the sandbox.
        /// <paramref name="owner"/> is the handle of the window whose button started the change (or zero): with it,
        /// Windows shows its prompt as the foreground window's, not as a request that may only flash on the taskbar.
        /// </summary>
        public ProcessStartInfo CreateStartInfo(ElevatedCommand command, IntPtr owner = default)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (_env.IsReal)
            {
                return new ProcessStartInfo
                {
                    FileName = _exePath,
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = command.WithSandbox(false).FormatArguments(),
                    // .NET hands ShellExecuteEx the owner window only with ErrorDialog (it then also leaves out
                    // SEE_MASK_FLAG_NO_UI, so Windows may show its own message if the start fails). Declining the
                    // prompt is still Win32Exception 1223.
                    ErrorDialog = owner != IntPtr.Zero,
                    ErrorDialogParentHandle = owner,
                };
            }
            return new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = command.WithSandbox(_env.IsSandbox).FormatArguments(),
            };
        }

        /// <summary>
        /// Runs a change on a worker thread. Progress is reported through <paramref name="progress"/> (create it on the UI
        /// thread). <paramref name="owner"/>: see <see cref="CreateStartInfo"/>; read the window's Handle on the UI thread.
        /// </summary>
        public Task<LaunchResult> RunAsync(ElevatedCommand command, IProgress<LaunchStage>? progress = null, IntPtr owner = default) =>
            Task.Run(() => Run(command, progress, owner));

        /// <summary>
        /// Runs a change and blocks until it finishes, however long that takes. Don't call it on the UI thread. Reports
        /// WaitingForPermission, then Changing once the elevated run started, then TakingLong if it is still running after
        /// <see cref="TakingLongAfter"/>. No stage is reported after this returns.
        /// </summary>
        public LaunchResult Run(ElevatedCommand command, IProgress<LaunchStage>? progress = null, IntPtr owner = default)
        {
            ProcessStartInfo psi = CreateStartInfo(command, owner);
            string? before = StatusReader.HashHostsFile(_env);
            progress?.Report(LaunchStage.WaitingForPermission);
            int exitCode;
            using (var slow = new TakingLongNotice(progress, _takingLongAfter))
            {
                try
                {
                    exitCode = _runner.RunAndWait(psi, () =>
                    {
                        progress?.Report(LaunchStage.Changing);
                        slow.Start();
                    });
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
                {
                    slow.Dispose();
                    return new LaunchResult(ResultKind.Declined, null, null, before, StatusReader.HashHostsFile(_env));
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    slow.Dispose();
                    return new LaunchResult(ResultKind.Unexpected, null, ex, before, StatusReader.HashHostsFile(_env));
                }
            }
            return new LaunchResult(ExitCodes.ToResult(exitCode), exitCode, null, before, StatusReader.HashHostsFile(_env));
        }

        /// <summary>
        /// Reports TakingLong once if the run is still going when the time is up. Only a report: the run itself is never
        /// touched. Once disposed, nothing more is reported, so TakingLong can't arrive after the result.
        /// </summary>
        private sealed class TakingLongNotice : IDisposable
        {
            private readonly object _gate = new object();
            private readonly IProgress<LaunchStage>? _progress;
            private readonly TimeSpan _after;
            private Timer? _timer;
            private bool _done;

            public TakingLongNotice(IProgress<LaunchStage>? progress, TimeSpan after)
            {
                _progress = progress;
                _after = after;
            }

            public void Start()
            {
                if (_progress == null) return;
                lock (_gate)
                {
                    if (_done || _timer != null) return;
                    _timer = new Timer(_ => Fire(), null, _after, Timeout.InfiniteTimeSpan);
                }
            }

            private void Fire()
            {
                lock (_gate)
                {
                    if (_done) return;
                    _done = true;
                    _progress?.Report(LaunchStage.TakingLong);
                }
            }

            public void Dispose()
            {
                lock (_gate)
                {
                    _done = true;
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }
    }
}
