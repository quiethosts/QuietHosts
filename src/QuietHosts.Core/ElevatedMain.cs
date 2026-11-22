using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace QuietHosts.Core
{
    /// <summary>
    /// The elevated run. Program.Main calls <see cref="Run(string[])"/> for "--elevated ..." before
    /// any WinForms initialization and returns its exit code. No UI, no network; it trusts nothing from the
    /// window and re-derives everything from the embedded lists, the per-PC store and its arguments.
    /// </summary>
    public static class ElevatedMain
    {
        /// <summary>Runs one elevated change and returns its exit code (<see cref="ExitCodes"/>).</summary>
        public static int Run(string[] args)
        {
            SecureDllSearch();
            bool parsed = ElevatedCommand.TryParse(args, out ElevatedCommand? command, out string error) && command != null;
            if (!parsed && !IsRecordable(args)) return ExitCodes.Unexpected;
            AppEnvironment env;
            try
            {
#if DEBUG
                bool sandbox = parsed ? command!.Sandbox
                    : args != null && args.Length > 0 && string.Equals(args[0], ElevatedCommand.SandboxOption, StringComparison.Ordinal);
                env = sandbox ? AppEnvironment.Sandbox() : AppEnvironment.Real();
#else
                env = AppEnvironment.Real();
#endif
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return ExitCodes.Unexpected;
            }
            if (!parsed) return Reject(args, error, env, IsProcessElevated);
            return Run(command!, env, IsProcessElevated);
        }

        /// <summary>For tests: parse and run against a given environment, with an injected elevation check.</summary>
        internal static int Run(string[] args, AppEnvironment env, Func<bool> isElevated)
        {
            if (!ElevatedCommand.TryParse(args, out ElevatedCommand? command, out string error) || command == null)
                return Reject(args, error, env, isElevated);
            return Run(command, env, isElevated);
        }

        internal static int Run(ElevatedCommand command, AppEnvironment env, Func<bool> isElevated)
        {
            if (command.Sandbox != env.IsSandbox) return ExitCodes.Unexpected;
            if (env.RequiresElevation && !SafeIsElevated(isElevated)) return ExitCodes.Unexpected;
            return WithWriteMutex(env, () => Execute(command, env));
        }

        /// <summary>
        /// A command line that doesn't parse changes nothing and exits 15. An elevated run still records why in
        /// LastRun (the parser's reason and the argument count and length, never the arguments), so a command line
        /// cut short on its way through the Windows prompt shows up in Copy details.
        /// </summary>
        private static int Reject(string[]? args, string error, AppEnvironment env, Func<bool> isElevated)
        {
            if (!IsRecordable(args)) return ExitCodes.Unexpected;
            if (env.RequiresElevation && !SafeIsElevated(isElevated)) return ExitCodes.Unexpected;
            int count = args?.Length ?? 0;
            int chars = args == null ? 0 : string.Join(" ", args.Select(a => a ?? string.Empty)).Length;
            WithWriteMutex(env, () =>
            {
                var sb = new StringBuilder();
                sb.Append("time: ").Append(env.Clock.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("app: ").Append(AppInfo.Version).Append('\n');
                sb.Append("command: rejected: ").Append(error).Append(" (").Append(Inv.Num(count)).Append(" arguments, ")
                  .Append(Inv.Num(chars)).Append(" characters)\n");
                sb.Append("result: Unexpected (exit ").Append(Inv.Num(ExitCodes.Unexpected)).Append(")\n");
                try { env.Machine.SetLastRun(sb.ToString()); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { }
                return ExitCodes.Unexpected;
            });
            return ExitCodes.Unexpected;
        }

        // A rejected line is recorded only if it was meant for this build's elevated run: "--elevated ...", and in
        // Debug builds also "--sandbox --elevated ...". Release builds never record a "--sandbox" line.
        private static bool IsRecordable(string[]? args)
        {
            if (args == null || args.Length == 0) return false;
            if (string.Equals(args[0], ElevatedCommand.ElevatedOption, StringComparison.Ordinal)) return true;
#if DEBUG
            return ElevatedCommand.IsElevatedInvocation(args);
#else
            return false;
#endif
        }

        // Holds the write mutex around one run: waits up to env.MutexWait, else Busy.
        private static int WithWriteMutex(AppEnvironment env, Func<int> body)
        {
            Mutex mutex;
            try
            {
                mutex = new Mutex(false, env.WriteMutexName);
            }
            catch (UnauthorizedAccessException)
            {
                return ExitCodes.Busy;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return ExitCodes.Unexpected;
            }
            using (mutex)
            {
                bool acquired;
                try
                {
                    acquired = mutex.WaitOne(env.MutexWait);
                }
                catch (AbandonedMutexException)
                {
                    acquired = true; // A previous run died holding it; recovery handles its pending marker.
                }
                if (!acquired) return ExitCodes.Busy;
                try
                {
                    return body();
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        /// <summary>True if the process runs elevated (member of Administrators in its current token).</summary>
        public static bool IsProcessElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool SafeIsElevated(Func<bool> isElevated)
        {
            try { return isElevated(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return false; }
        }

        private static void SecureDllSearch()
        {
            try { Native.SetDefaultDllDirectories(Native.LOAD_LIBRARY_SEARCH_SYSTEM32); }
            catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException) { }
        }

        private static int Execute(ElevatedCommand command, AppEnvironment env)
        {
            var run = new RunRecord(env, command);
            HostsWriter? writer = null;
            try
            {
                BlockLists lists = BlockLists.LoadEmbedded();
                run.ListVersion = lists.Version;
                writer = new HostsWriter(env);
                writer.Recover();
                MachineState state = env.Machine.Read();
                ResultKind result = Dispatch(command, env, lists, state, writer, run);
                run.Result = result;
                run.Log.AddRange(writer.Log);
                if (command.Verb == Verb.Undo && result == ResultKind.Success)
                {
                    env.Machine.DeleteAll();
                    return ExitCodes.Success;
                }
                env.Machine.SetLastRun(run.Format());
                return ExitCodes.FromResult(result);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                run.Result = ResultKind.Unexpected;
                run.Error = ex;
                if (writer != null) run.Log.AddRange(writer.Log.Except(run.Log));
                try { env.Machine.SetLastRun(run.Format()); }
                catch (Exception inner) when (!(inner is OutOfMemoryException)) { }
                return ExitCodes.Unexpected;
            }
        }

        private static ResultKind Dispatch(ElevatedCommand command, AppEnvironment env, BlockLists lists, MachineState state, HostsWriter writer, RunRecord run)
        {
            switch (command.Verb)
            {
                case Verb.On:
                    return WriteBlock(env, lists, state, command.Level ?? Level.Standard, Array.Empty<string>(), Array.Empty<string>(), writer, run);

                case Verb.Off:
                {
                    List<string> allow = Allowlist.Prune(state.Allowlist, lists);
                    WriteOutcome outcome = writer.Write(doc =>
                    {
                        allow = Allowlist.Prune(state.Allowlist.Concat(doc.HandDisabledHosts), lists);
                        return doc.Remove();
                    });
                    run.Note(outcome);
                    if (outcome.Kind == ResultKind.Success)
                    {
                        env.Machine.SetEnabled(false);
                        env.Machine.SetAllowlist(allow);
                        run.AllowlistCount = allow.Count;
                    }
                    return outcome.Kind;
                }

                case Verb.Allow:
                {
                    foreach (string h in command.Hosts)
                    {
                        if (!lists.Contains(h)) { run.Log.Add("refused: an address isn't in the lists"); return ResultKind.Unexpected; }
                    }
                    return ChangeAllowlist(env, lists, state, command.Hosts, Array.Empty<string>(), writer, run);
                }

                case Verb.Block:
                {
                    var allowed = new HashSet<string>(state.Allowlist, StringComparer.Ordinal);
                    foreach (string h in command.Hosts)
                    {
                        if (!lists.Contains(h) && !allowed.Contains(h)) { run.Log.Add("refused: an address is neither in the lists nor allowed"); return ResultKind.Unexpected; }
                    }
                    return ChangeAllowlist(env, lists, state, Array.Empty<string>(), command.Hosts, writer, run);
                }

                case Verb.Undo:
                {
                    WriteOutcome outcome = writer.Write(doc => doc.Remove());
                    run.Note(outcome);
                    return outcome.Kind;
                }

                default:
                    return ResultKind.Unexpected;
            }
        }

        private static ResultKind ChangeAllowlist(AppEnvironment env, BlockLists lists, MachineState state, IReadOnlyList<string> add,
            IReadOnlyList<string> remove, HostsWriter writer, RunRecord run)
        {
            List<string> allow = Merge(state.Allowlist, Array.Empty<string>(), add, remove, lists);
            if (allow.Count > Limits.MaxAllowlist) { run.Log.Add("refused: more than 2,000 allowed addresses"); return ResultKind.Unexpected; }
            return WriteBlock(env, lists, state, null, add, remove, writer, run);
        }

        /// <summary>
        /// Writes the block and saves Enabled=1, the level and the allowlist. For On, <paramref name="onLevel"/> is the
        /// level. For Allow and Block (null), the file decides, as it does for the window, which shows On and offers
        /// them whenever our block is in the file: the block is rewritten at its own level if it is in the file (or
        /// at the saved level if Enabled=1 but the block is gone); otherwise only the allowlist changes. So a
        /// restored backup, or a run that died before saving Enabled, can't turn Allow into a silent no-op.
        /// </summary>
        private static ResultKind WriteBlock(AppEnvironment env, BlockLists lists, MachineState state, Level? onLevel,
            IReadOnlyList<string> add, IReadOnlyList<string> remove, HostsWriter writer, RunRecord run)
        {
            Level saved = state.HasLevel ? state.Level : Level.Standard;
            List<string> allow = Merge(state.Allowlist, Array.Empty<string>(), add, remove, lists);
            bool rewrite = onLevel.HasValue;
            Level level = onLevel ?? saved;
            int written = 0;
            WriteOutcome outcome = writer.Write(doc =>
            {
                allow = Merge(state.Allowlist, doc.HandDisabledHosts, add, remove, lists);
                if (!onLevel.HasValue)
                {
                    rewrite = doc.HasBlock || state.Enabled;
                    level = (doc.HasBlock ? doc.Header?.Level : null) ?? saved;
                    if (!rewrite) return doc.Bytes;
                }
                BlockPlan plan = BlockPlanner.Plan(lists, level, allow, doc.NamesMappedOutside);
                written = plan.Hosts.Count;
                return doc.Apply(plan.ToContent(AppInfo.Version), lists.Guard);
            });
            run.Note(outcome);
            if (rewrite) run.BlockCount = written;
            if (outcome.Kind != ResultKind.Success) return outcome.Kind;
            if (rewrite)
            {
                env.Machine.SetEnabled(true);
                env.Machine.SetLevel(level);
            }
            else
            {
                run.Log.Add("protection is off: only the allowlist changed");
            }
            env.Machine.SetAllowlist(allow);
            run.AllowlistCount = allow.Count;
            return ResultKind.Success;
        }

        private static List<string> Merge(IEnumerable<string> saved, IEnumerable<string> handDisabled, IEnumerable<string> add, IEnumerable<string> remove, BlockLists lists)
        {
            var removeSet = new HashSet<string>(remove, StringComparer.Ordinal);
            return Allowlist.Prune(saved.Concat(handDisabled).Concat(add).Where(h => !removeSet.Contains(h)), lists);
        }

        /// <summary>LastRun: time, app version, verb and arguments (a host count, not names), result, counts, exception.</summary>
        private sealed class RunRecord
        {
            private readonly ElevatedCommand _command;
            private readonly DateTime _started;

            public RunRecord(AppEnvironment env, ElevatedCommand command)
            {
                _command = command;
                _started = env.Clock.UtcNow;
            }

            public string ListVersion { get; set; } = string.Empty;
            public ResultKind Result { get; set; } = ResultKind.Unexpected;
            public int? BlockCount { get; set; }
            public int? AllowlistCount { get; set; }
            public string? BackupPath { get; set; }
            public Exception? Error { get; set; }
            public List<string> Log { get; } = new List<string>();

            public void Note(WriteOutcome outcome)
            {
                if (outcome.BackupPath != null) BackupPath = outcome.BackupPath;
                if (outcome.Refusal != RefusalReason.None) Log.Add("refusal: " + outcome.Refusal);
                if (outcome.Error != null && Error == null) Error = outcome.Error;
            }

            public string Format()
            {
                var sb = new StringBuilder();
                sb.Append("time: ").Append(_started.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("app: ").Append(AppInfo.Version).Append('\n');
                sb.Append("command: ").Append(_command.Describe());
#if DEBUG
                if (_command.Sandbox) sb.Append(" (sandbox)");
#endif
                sb.Append('\n');
                sb.Append("result: ").Append(Result).Append(" (exit ").Append(Inv.Num(ExitCodes.FromResult(Result))).Append(")\n");
                if (ListVersion.Length > 0) sb.Append("lists: ").Append(ListVersion).Append('\n');
                if (BlockCount.HasValue) sb.Append("addresses in block: ").Append(Inv.Num(BlockCount.Value)).Append('\n');
                if (AllowlistCount.HasValue) sb.Append("allowed addresses: ").Append(Inv.Num(AllowlistCount.Value)).Append('\n');
                if (BackupPath != null) sb.Append("backup: ").Append(BackupPath).Append('\n');
                foreach (string line in Log.Distinct()) sb.Append("log: ").Append(line).Append('\n');
                if (Error != null) sb.Append("error: ").Append(Error.GetType().FullName).Append(": ").Append(Error.Message).Append('\n');
                return LastRunText.Cap(sb.ToString());
            }
        }
    }
}
