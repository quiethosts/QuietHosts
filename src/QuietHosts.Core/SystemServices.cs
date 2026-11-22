using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace QuietHosts.Core
{
    /// <summary>Flushes the DNS Client cache after a write. A failure is only logged.</summary>
    public interface IDnsFlusher
    {
        /// <summary>Returns true on success; <paramref name="detail"/> says what happened, for LastRun.</summary>
        bool Flush(out string detail);
    }

    /// <summary>Runs %SystemRoot%\System32\ipconfig.exe /flushdns by full path, hidden, without a shell, with a 10 s timeout.</summary>
    public sealed class IpconfigDnsFlusher : IDnsFlusher
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        public bool Flush(out string detail)
        {
            string exe = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
            try
            {
                var psi = new ProcessStartInfo(exe, "/flushdns")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Environment.SystemDirectory,
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) { detail = "ipconfig did not start"; return false; }
                    p.OutputDataReceived += (s, e) => { };
                    p.ErrorDataReceived += (s, e) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
                    {
                        try { p.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                        detail = "ipconfig timed out";
                        return false;
                    }
                    p.WaitForExit();
                    detail = "ipconfig exit " + Inv.Num(p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException)
            {
                detail = "ipconfig failed: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }

    /// <summary>No flush (sandbox and tests).</summary>
    public sealed class NoDnsFlusher : IDnsFlusher
    {
        public static NoDnsFlusher Instance { get; } = new NoDnsFlusher();
        public bool Flush(out string detail) { detail = "skipped"; return true; }
    }

    /// <summary>Reads where Windows looks for the hosts file (Tcpip\Parameters\DataBasePath).</summary>
    public interface IHostsDatabasePath
    {
        /// <summary>The expanded DataBasePath, or null if the value is missing (the default folder).</summary>
        string? Read();
    }

    /// <summary>
    /// HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\DataBasePath, 64-bit view, read-only, expanded
    /// without the process environment (<see cref="HostsDatabase.Expand"/>).
    /// </summary>
    public sealed class RegistryHostsDatabasePath : IHostsDatabasePath
    {
        public string? Read()
        {
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var key = hklm.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", false))
            {
                object? value = key?.GetValue("DataBasePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is string s && s.Length > 0) return HostsDatabase.Expand(s);
                return null;
            }
        }
    }

    /// <summary>A fixed value, for tests.</summary>
    public sealed class FixedHostsDatabasePath : IHostsDatabasePath
    {
        private readonly string? _value;
        public FixedHostsDatabasePath(string? value) { _value = value; }
        public string? Read() => _value;
    }

    /// <summary>
    /// The moved-hosts-database check. It never uses environment variables, which the elevated run doesn't trust:
    /// the default folder is the folder of the hosts file QuietHosts writes (Environment.SystemDirectory\drivers\etc).
    /// </summary>
    public static class HostsDatabase
    {
        /// <summary>The folder Windows uses by default and QuietHosts writes: Environment.SystemDirectory\drivers\etc.</summary>
        public static string DefaultFolder => Path.Combine(Environment.SystemDirectory, "drivers", "etc");

        /// <summary>
        /// Expands a raw DataBasePath value without the process environment: a leading %SystemRoot% or %windir%
        /// becomes the Windows folder (the parent of Environment.SystemDirectory). Any other variable stays as it
        /// is, and <see cref="IsMoved"/> treats it as moved.
        /// </summary>
        public static string Expand(string raw)
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            string windows = Path.GetDirectoryName(Environment.SystemDirectory) ?? string.Empty;
            if (windows.Length == 0) return raw;
            foreach (string variable in new[] { "%SystemRoot%", "%windir%" })
            {
                if (raw.StartsWith(variable, StringComparison.OrdinalIgnoreCase)) return windows + raw.Substring(variable.Length);
            }
            return raw;
        }

        /// <summary>
        /// True if DataBasePath points anywhere but the default folder (compared ordinally ignoring case,
        /// trailing backslashes trimmed), or still holds an unexpanded variable. A missing value means the default.
        /// </summary>
        public static bool IsMoved(string? expandedValue)
        {
            if (string.IsNullOrEmpty(expandedValue)) return false;
            if (expandedValue!.IndexOf('%') >= 0) return true;
            return !string.Equals(TrimBackslashes(expandedValue), TrimBackslashes(DefaultFolder), StringComparison.OrdinalIgnoreCase);
        }

        private static string TrimBackslashes(string s) => s.TrimEnd('\\');
    }
}
