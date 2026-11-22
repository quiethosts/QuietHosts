using System.Linq;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    public class SkeletonTests
    {
        [Fact]
        public void Lists_are_embedded_in_the_engine()
        {
            var names = typeof(AppInfo).Assembly.GetManifestResourceNames();
            Assert.Contains("QuietHosts.Lists.default.txt", names);
            Assert.Contains("QuietHosts.Lists.strict.txt", names);
            Assert.Contains("QuietHosts.Lists.guard.txt", names);
        }

        [Fact]
        public void Repository_url_comes_from_build_metadata()
        {
            Assert.StartsWith("https://github.com/", AppInfo.RepositoryUrl);
            Assert.DoesNotContain("+", AppInfo.Version);
        }
    }
}
