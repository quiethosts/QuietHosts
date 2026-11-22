using System;
using System.IO;
using System.Reflection;

namespace QuietHosts.Core
{
    public enum LocationKind
    {
        /// <summary>The real hosts file of this PC.</summary>
        System,
        /// <summary>A hosts file under %TEMP% for tests.</summary>
        Testing,
        /// <summary>The Debug-only sandbox under %TEMP%\QuietHosts-sandbox.</summary>
        Sandbox,
    }

    /// <summary>
    /// Where the hosts file and its backups are. Only <see cref="System"/> computes the
    /// real path, and only inside QuietHosts.exe; tests can only use folders under %TEMP%.
    /// </summary>
    public sealed class HostsLocation
    {
#if DEBUG
        public const string SandboxFolderName = "QuietHosts-sandbox";
#endif

        private HostsLocation(string hostsPath, LocationKind kind)
        {
            HostsPath = hostsPath;
            Folder = Path.GetDirectoryName(hostsPath) ?? throw new ArgumentException("The hosts path has no folder.", nameof(hostsPath));
            Kind = kind;
        }

        /// <summary>The hosts file.</summary>
        public string HostsPath { get; }
        /// <summary>The hosts file's folder, where the backups are.</summary>
        public string Folder { get; }
        public LocationKind Kind { get; }
        public bool IsSystem => Kind == LocationKind.System;

        /// <summary>The real hosts file path, for comparisons only: Environment.SystemDirectory\drivers\etc\hosts.</summary>
        public static string RealHostsPath => Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts");

        /// <summary>The real hosts file. Throws unless the entry assembly is QuietHosts (so tests and tools can't get it).</summary>
        public static HostsLocation System()
        {
            if (!InsideQuietHostsExe())
                throw new InvalidOperationException("The real hosts file is only available inside QuietHosts.exe.");
            return new HostsLocation(RealHostsPath, LocationKind.System);
        }

        /// <summary>
        /// True only inside QuietHosts.exe (the entry assembly's name). The guards that keep tests and tools, and
        /// so admin CI runners, away from the real hosts file and HKLM all use this.
        /// </summary>
        internal static bool InsideQuietHostsExe() =>
            string.Equals(Assembly.GetEntryAssembly()?.GetName().Name, "QuietHosts", StringComparison.Ordinal);

        /// <summary>A hosts file at root\etc\hosts. The root must be a folder under %TEMP% (and not under the Windows folder).</summary>
        public static HostsLocation ForTesting(string root)
        {
            string full = CheckTempRoot(root);
            return new HostsLocation(Path.Combine(full, "etc", "hosts"), LocationKind.Testing);
        }

#if DEBUG
        /// <summary>The fixed sandbox root, %TEMP%\QuietHosts-sandbox (Debug builds only).</summary>
        public static string SandboxRoot => Path.Combine(Path.GetFullPath(Path.GetTempPath()), SandboxFolderName);

        /// <summary>The sandbox hosts file, %TEMP%\QuietHosts-sandbox\etc\hosts (Debug builds only).</summary>
        public static HostsLocation Sandbox() => SandboxAt(SandboxRoot);

        internal static HostsLocation SandboxAt(string root)
        {
            string full = CheckTempRoot(root);
            return new HostsLocation(Path.Combine(full, "etc", "hosts"), LocationKind.Sandbox);
        }
#endif

        /// <summary>For guard tests only: a location that skips the temp-folder checks. Never the real hosts file or its folder.</summary>
        internal static HostsLocation Unchecked(string hostsPath, LocationKind kind)
        {
            string full = Path.GetFullPath(hostsPath);
            if (IsRealHostsTarget(full)) throw new ArgumentException("The real hosts file and its folder are never used outside QuietHosts.exe.", nameof(hostsPath));
            return new HostsLocation(full, kind);
        }

        private static string CheckTempRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A root folder is needed.", nameof(root));
            string full = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath());
            if (!PathUtil.IsStrictlyUnder(full, temp))
                throw new ArgumentException("Test and sandbox folders must be under %TEMP%.", nameof(root));
            if (PathUtil.IsUnderWindowsFolder(full))
                throw new ArgumentException("Test and sandbox folders must not be under the Windows folder.", nameof(root));
            // A junction under %TEMP% must not lead somewhere else.
            string? final = Directory.Exists(full) ? Native.FinalPath(full) : null;
            string? finalTemp = Native.FinalPath(temp);
            if (final != null && finalTemp != null && !PathUtil.IsStrictlyUnder(final, finalTemp))
                throw new ArgumentException("The folder resolves to a place outside %TEMP%.", nameof(root));
            return full;
        }

        /// <summary>
        /// True if the path is the real hosts file or in its folder, directly or through a junction or
        /// symbolic link. The writer refuses such targets unless the environment is the real one.
        /// </summary>
        public static bool IsRealHostsTarget(string path)
        {
            string full = Path.GetFullPath(path);
            string real = RealHostsPath;
            string realDir = Path.GetDirectoryName(real)!;
            string? dir = Path.GetDirectoryName(full);
            if (PathUtil.SamePath(full, real) || (dir != null && PathUtil.SamePath(dir, realDir))) return true;
            string finalRealDir = Native.FinalPath(realDir) ?? realDir;
            if (dir != null)
            {
                string? finalDir = Native.FinalPath(dir);
                if (finalDir != null && PathUtil.SamePath(finalDir, finalRealDir)) return true;
            }
            if (File.Exists(full))
            {
                string? finalFile = Native.FinalPath(full);
                string finalReal = Native.FinalPath(real) ?? real;
                if (finalFile != null && PathUtil.SamePath(finalFile, finalReal)) return true;
            }
            return false;
        }
    }

    internal static class PathUtil
    {
        public static bool SamePath(string a, string b) =>
            string.Equals(TrimSeparators(a), TrimSeparators(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>True if path is inside folder (not equal to it). Both should be full paths.</summary>
        public static bool IsStrictlyUnder(string path, string folder)
        {
            string p = TrimSeparators(path);
            string f = TrimSeparators(folder);
            return p.Length > f.Length + 1 &&
                   p.StartsWith(f, StringComparison.OrdinalIgnoreCase) &&
                   p[f.Length] == Path.DirectorySeparatorChar;
        }

        public static bool IsUnderOrSame(string path, string folder) => SamePath(path, folder) || IsStrictlyUnder(path, folder);

        public static bool IsUnderWindowsFolder(string path)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(windows)) return false;
            string full = Path.GetFullPath(path);
            if (IsUnderOrSame(full, windows)) return true;
            string? existing = full;
            while (existing != null && !Directory.Exists(existing) && !File.Exists(existing)) existing = Path.GetDirectoryName(existing);
            string? final = existing != null ? Native.FinalPath(existing) : null;
            string finalWindows = Native.FinalPath(windows) ?? windows;
            return final != null && IsUnderOrSame(final, finalWindows);
        }

        public static string TrimSeparators(string path)
        {
            string p = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            while (p.Length > 3 && p[p.Length - 1] == Path.DirectorySeparatorChar) p = p.Substring(0, p.Length - 1);
            return p;
        }
    }
}
