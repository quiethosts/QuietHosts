using System;
using System.Linq;
using System.Reflection;

namespace QuietHosts.Core
{
    /// <summary>Version and project links, read from the assembly's build metadata.</summary>
    public static class AppInfo
    {
        private static readonly Assembly ThisAssembly = typeof(AppInfo).Assembly;

        /// <summary>The app version, for example "1.0.0" or "1.0.0-beta.1" (no commit suffix).</summary>
        public static string Version
        {
            get
            {
                var info = ThisAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = info.IndexOf('+');
                return plus >= 0 ? info.Substring(0, plus) : info;
            }
        }

        /// <summary>The project's GitHub page, for example "https://github.com/quiethosts/QuietHosts".</summary>
        public static string RepositoryUrl =>
            ThisAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "RepositoryUrl", StringComparison.Ordinal))?.Value
            ?? "https://github.com/quiethosts/QuietHosts";
    }
}
