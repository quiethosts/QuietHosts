using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using QuietHosts.Core;
using QuietHosts.Tests.Infrastructure;
using Xunit;

namespace QuietHosts.Tests
{
    public class EnvironmentTests
    {
        [Fact]
        public void ForTesting_environment_is_under_temp_and_never_real()
        {
            using (var dir = new TempDir())
            {
                AppEnvironment env = AppEnvironment.ForTesting(dir.Path);
                Assert.True(Directory.Exists(env.Location.Folder));
                Assert.Equal(LocationKind.Testing, env.Location.Kind);
                Assert.False(env.IsReal);
                Assert.False(env.IsSandbox);
                Assert.False(env.RequiresElevation);
                Assert.IsType<NoDnsFlusher>(env.Flusher);
                Assert.StartsWith(@"Local\QuietHosts.Test.", env.WriteMutexName);
                Assert.Equal(ProtectionState.Off, StatusReader.Read(env).State);
                Assert.Null(new HostsWriter(env).Log.FirstOrDefault());
            }
        }

        [Fact]
        public void Core_pins_native_library_loading_to_System32()
        {
            var attr = typeof(AppInfo).Assembly.GetCustomAttributes(typeof(DefaultDllImportSearchPathsAttribute), false)
                .Cast<DefaultDllImportSearchPathsAttribute>().Single();
            Assert.Equal(DllImportSearchPath.System32, attr.Paths);
        }

        [Fact]
        public void Real_environment_constants()
        {
            Assert.Equal(@"Global\QuietHosts.Write", AppEnvironment.RealWriteMutexName);
            Assert.Equal(5, AppEnvironment.DefaultMutexWait.TotalSeconds);
            Assert.Equal(@"SOFTWARE\QuietHosts", RegistryMachineStore.MachineSubKey);
            Assert.Equal(@"Software\QuietHosts", RegistryUserStore.UserSubKey);
            Assert.Equal(10, IpconfigDnsFlusher.Timeout.TotalSeconds);
            Assert.Equal(2, HostsWriter.RecheckDelay.TotalSeconds);
        }

        [Fact]
        public void Write_mutex_detection()
        {
            string name = @"Local\QuietHosts.Test." + System.Guid.NewGuid().ToString("N");
            Assert.False(WriteMutex.IsRunInProgress(name));
            using (new System.Threading.Mutex(false, name))
            {
                Assert.True(WriteMutex.IsRunInProgress(name));
            }
            Assert.False(WriteMutex.IsRunInProgress(name));
        }

        [Fact]
        public void App_info_has_a_version_and_repository()
        {
            Assert.False(string.IsNullOrEmpty(AppInfo.Version));
            Assert.StartsWith("https://github.com/", AppInfo.RepositoryUrl);
        }
    }
}
