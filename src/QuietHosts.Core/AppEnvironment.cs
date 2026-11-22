using System;
using System.IO;

namespace QuietHosts.Core
{
    /// <summary>
    /// Everything that touches the system, bundled: where the hosts file is, the per-PC and per-user
    /// stores, file operations, the DNS flusher, the clock and the write mutex. <see cref="Real"/> is the
    /// app on this PC; <see cref="Sandbox"/> (Debug only) works under %TEMP%; tests build their own.
    /// </summary>
    public sealed class AppEnvironment
    {
        /// <summary>The mutex every elevated run holds while it changes the hosts file.</summary>
        public const string RealWriteMutexName = @"Global\QuietHosts.Write";
#if DEBUG
        public const string SandboxWriteMutexName = @"Local\QuietHosts.Sandbox.Write";
#endif
        public static readonly TimeSpan DefaultMutexWait = TimeSpan.FromSeconds(5);

        public AppEnvironment(HostsLocation location, IMachineStore machine, IUserStore user, IFileOps files, IDnsFlusher flusher,
            IClock clock, IHostsDatabasePath databasePath, string writeMutexName, TimeSpan mutexWait, bool isReal, bool requiresElevation)
        {
            Location = location ?? throw new ArgumentNullException(nameof(location));
            Machine = machine ?? throw new ArgumentNullException(nameof(machine));
            User = user ?? throw new ArgumentNullException(nameof(user));
            Files = files ?? throw new ArgumentNullException(nameof(files));
            Flusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            DatabasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
            if (string.IsNullOrWhiteSpace(writeMutexName)) throw new ArgumentException("A mutex name is needed.", nameof(writeMutexName));
            if (isReal != location.IsSystem) throw new ArgumentException("Only the real environment uses the real hosts file.", nameof(isReal));
            WriteMutexName = writeMutexName;
            MutexWait = mutexWait;
            IsReal = isReal;
            RequiresElevation = requiresElevation;
        }

        public HostsLocation Location { get; }
        public IMachineStore Machine { get; }
        public IUserStore User { get; }
        public IFileOps Files { get; }
        public IDnsFlusher Flusher { get; }
        public IClock Clock { get; }
        public IHostsDatabasePath DatabasePath { get; }
        public string WriteMutexName { get; }
        /// <summary>How long an elevated run waits for the write mutex before returning Busy.</summary>
        public TimeSpan MutexWait { get; }
        /// <summary>True only for <see cref="Real"/>: the real hosts file, HKLM and HKCU.</summary>
        public bool IsReal { get; }
        /// <summary>The Debug sandbox: the window adds "(sandbox)" to its title and starts children without runas.</summary>
        public bool IsSandbox => Location.Kind == LocationKind.Sandbox;
        /// <summary>Elevated runs exit 15 unless the process is elevated (true for the real environment).</summary>
        public bool RequiresElevation { get; }

        /// <summary>
        /// The real environment: the hosts file in Environment.SystemDirectory\drivers\etc, HKLM\SOFTWARE\QuietHosts
        /// (64-bit view), HKCU\Software\QuietHosts, ipconfig /flushdns. Throws outside QuietHosts.exe.
        /// </summary>
        public static AppEnvironment Real() => new AppEnvironment(
            HostsLocation.System(),
            RegistryMachineStore.LocalMachine(),
            RegistryUserStore.CurrentUser(),
            RealFileOps.Instance,
            new IpconfigDnsFlusher(),
            SystemClock.Instance,
            new RegistryHostsDatabasePath(),
            RealWriteMutexName,
            DefaultMutexWait,
            isReal: true,
            requiresElevation: true);

        /// <summary>
        /// A test environment at root\etc\hosts under %TEMP% (the etc folder is created): in-memory stores,
        /// real file operations, no DNS flush, no elevation check, a unique Local\ mutex.
        /// </summary>
        public static AppEnvironment ForTesting(string root) => new AppEnvironment(
            CreateFolder(HostsLocation.ForTesting(root)),
            new InMemoryMachineStore(),
            new InMemoryUserStore(),
            RealFileOps.Instance,
            NoDnsFlusher.Instance,
            SystemClock.Instance,
            new FixedHostsDatabasePath(null),
            @"Local\QuietHosts.Test." + Guid.NewGuid().ToString("N"),
            DefaultMutexWait,
            isReal: false,
            requiresElevation: false);

        private static HostsLocation CreateFolder(HostsLocation location)
        {
            Directory.CreateDirectory(location.Folder);
            return location;
        }

#if DEBUG
        /// <summary>
        /// The Debug-only sandbox: %TEMP%\QuietHosts-sandbox\etc\hosts (seeded once as a byte copy of the
        /// real hosts file), machine.ini and user.ini in the root, no DNS flush, no elevation check.
        /// </summary>
        public static AppEnvironment Sandbox() => SandboxAt(HostsLocation.SandboxRoot, HostsLocation.RealHostsPath);

        /// <summary>The sandbox at a root under %TEMP%; seeds etc\hosts once from <paramref name="seedFrom"/> (read-only) if given.</summary>
        internal static AppEnvironment SandboxAt(string root, string? seedFrom)
        {
            HostsLocation location = HostsLocation.SandboxAt(root);
            Directory.CreateDirectory(location.Folder);
            if (seedFrom != null && !File.Exists(location.HostsPath))
            {
                byte[] bytes = RealFileOps.Instance.ReadFile(seedFrom, Limits.MaxHostsFileBytes) ?? Array.Empty<byte>();
                RealFileOps.Instance.CreateNew(location.HostsPath, bytes);
            }
            string full = Path.GetFullPath(root);
            return new AppEnvironment(
                location,
                new FileMachineStore(Path.Combine(full, "machine.ini")),
                new FileUserStore(Path.Combine(full, "user.ini")),
                RealFileOps.Instance,
                NoDnsFlusher.Instance,
                SystemClock.Instance,
                new RegistryHostsDatabasePath(),
                SandboxWriteMutexName,
                DefaultMutexWait,
                isReal: false,
                requiresElevation: false);
        }
#endif
    }
}
