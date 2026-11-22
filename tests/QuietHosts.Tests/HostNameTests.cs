using System.Globalization;
using System.Threading;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    public class HostNameTests
    {
        [Theory]
        [InlineData("pagead2.googlesyndication.com", "pagead2.googlesyndication.com")]
        [InlineData("PageAd2.GoogleSyndication.COM", "pagead2.googlesyndication.com")]
        [InlineData("example.com.", "example.com")]
        [InlineData("a.co", "a.co")]
        [InlineData("1x1.a-mo.net", "1x1.a-mo.net")]
        [InlineData("xn--80ak6aa92e.com", "xn--80ak6aa92e.com")]
        [InlineData("ads.xn--p1ai", "ads.xn--p1ai")]
        [InlineData("123.example.com", "123.example.com")]
        [InlineData("a-b.c-d.e-f.gh", "a-b.c-d.e-f.gh")]
        public void Valid_names_normalize(string raw, string expected)
        {
            Assert.True(HostName.TryNormalize(raw, out string host));
            Assert.Equal(expected, host);
            Assert.True(HostName.IsValid(raw));
            Assert.Equal(expected, HostName.Normalize(raw));
        }

        [Theory]
        [InlineData("")]
        [InlineData("localhost")]          // single label
        [InlineData("com")]
        [InlineData("1.2.3.4")]            // IPv4 literal: the last label starts with a digit
        [InlineData("10.0.0.1.")]
        [InlineData("::1")]
        [InlineData("[::1]")]
        [InlineData("a.b")]                // shorter than 4 characters
        [InlineData("example.c")]          // one-letter TLD
        [InlineData("example.com..")]      // only one trailing dot is stripped
        [InlineData(".example.com")]
        [InlineData("example..com")]
        [InlineData("-ads.example.com")]
        [InlineData("ads-.example.com")]
        [InlineData("ads.example.com-")]
        [InlineData("ads.example.1com")]
        [InlineData("*.example.com")]
        [InlineData("ads.example.com:443")]
        [InlineData("ads_x.example.com")]
        [InlineData("ads example.com")]
        [InlineData(" ads.example.com")]   // whitespace is rejected before trimming
        [InlineData("ads.example.com\t")]
        [InlineData("http://ads.example.com")]
        [InlineData("ads.example.com/path")]
        [InlineData(@"C:\hosts")]
        [InlineData(@"\\server\share")]
        public void Invalid_names_are_rejected(string raw)
        {
            Assert.False(HostName.TryNormalize(raw, out string host));
            Assert.Equal(string.Empty, host);
            Assert.Null(HostName.Normalize(raw));
        }

        [Fact]
        public void Null_is_rejected() => Assert.False(HostName.IsValid(null));

        [Fact]
        public void Kelvin_sign_is_rejected_before_lowercasing()
        {
            // Some runtimes' ToLowerInvariant turn U+212A KELVIN SIGN into an ASCII "k" (.NET Framework's
            // tables don't), so the raw check must reject it before lowercasing either way.
            string kelvin = ((char)0x212A).ToString();
            Assert.False(HostName.IsValid("ads." + kelvin + "ing.com"));
            Assert.False(HostName.IsValid(kelvin + "elvin.com"));
        }

        [Fact]
        public void Dotted_capital_I_and_dotless_i_are_rejected()
        {
            string dotted = ((char)0x0130).ToString();   // capital I with dot above
            string dotless = ((char)0x0131).ToString();  // small dotless i
            Assert.False(HostName.IsValid("l" + dotted + "nk.example.com"));
            Assert.False(HostName.IsValid("l" + dotless + "nk.example.com"));
        }

        [Fact]
        public void Uppercase_I_lowercases_to_ascii_i_under_any_culture()
        {
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");   // a culture whose lowercase "I" is a dotless i
                Assert.True(HostName.TryNormalize("ADS.INFO.IT", out string host));
                Assert.Equal("ads.info.it", host);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
        }

        [Fact]
        public void Non_ascii_and_control_characters_are_rejected()
        {
            Assert.False(HostName.IsValid("caf" + (char)0x00E9 + ".com"));
            Assert.False(HostName.IsValid("ads" + (char)0x0000 + ".com"));
            Assert.False(HostName.IsValid("ads.example.com" + (char)0x007F));
            Assert.False(HostName.IsValid("ads" + (char)0x00A0 + ".example.com"));
        }

        [Fact]
        public void Label_and_name_length_limits()
        {
            string label63 = new string('a', 63);
            string label64 = new string('a', 64);
            Assert.True(HostName.IsValid(label63 + ".com"));
            Assert.False(HostName.IsValid(label64 + ".com"));
            Assert.True(HostName.IsValid("ads." + new string('b', 63)));
            Assert.False(HostName.IsValid("ads." + new string('b', 64)));

            // 253 characters is the longest valid name; 254 is too long.
            string name253 = string.Join(".", new string('a', 63), new string('b', 63), new string('c', 63), new string('d', 61));
            Assert.Equal(253, name253.Length);
            Assert.True(HostName.IsValid(name253));
            Assert.True(HostName.IsValid(name253 + "."));
            Assert.False(HostName.IsValid(name253 + "d"));
        }

        [Fact]
        public void IsNormalized_requires_the_exact_normal_form()
        {
            Assert.True(HostName.IsNormalized("ads.example.com"));
            Assert.False(HostName.IsNormalized("Ads.example.com"));
            Assert.False(HostName.IsNormalized("ads.example.com."));
            Assert.False(HostName.IsNormalized(null));
        }
    }
}
