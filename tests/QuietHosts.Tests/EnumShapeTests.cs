using System;
using System.Linq;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    /// <summary>The window words every value of these enums, so their names and numbers are pinned.</summary>
    public class EnumShapeTests
    {
        private static void AssertShape<T>(params (string Name, int Value)[] expected) where T : struct, Enum
        {
            var actual = Enum.GetValues(typeof(T)).Cast<T>().Select(v => (v.ToString(), Convert.ToInt32(v))).ToArray();
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Level_values() => AssertShape<Level>(("Standard", 0), ("Strict", 1));

        [Fact]
        public void Tier_values() => AssertShape<Tier>(("Default", 0), ("Strict", 1));

        [Fact]
        public void ProtectionState_values() => AssertShape<ProtectionState>(
            ("Off", 0), ("On", 1), ("Removed", 2), ("OlderList", 3), ("CantRead", 4), ("Refused", 5));

        [Fact]
        public void RefusalReason_values() => AssertShape<RefusalReason>(
            ("None", 0), ("HostsPathMoved", 1), ("UnsupportedEncoding", 2), ("TooLarge", 3));

        [Fact]
        public void HostsWarnings_values()
        {
            Assert.NotNull(typeof(HostsWarnings).GetCustomAttributes(typeof(FlagsAttribute), false).SingleOrDefault());
            AssertShape<HostsWarnings>(("None", 0), ("EditedBlock", 1), ("MalformedMarkers", 2), ("LargeFile", 4),
                ("MicrosoftLinesOutside", 8), ("ChangeNotFinished", 16), ("LegacyBlockToo", 32));
        }

        [Fact]
        public void ResultKind_values() => AssertShape<ResultKind>(
            ("Success", 0), ("Declined", 1), ("AccessDenied", 2), ("FileLocked", 3), ("Reverted", 4), ("Busy", 5), ("Refused", 6), ("Unexpected", 7));

        [Fact]
        public void Verb_values() => AssertShape<Verb>(("On", 0), ("Off", 1), ("Allow", 2), ("Block", 3), ("Undo", 4));

        [Theory]
        [InlineData(ResultKind.Success, 0)]
        [InlineData(ResultKind.AccessDenied, 10)]
        [InlineData(ResultKind.FileLocked, 11)]
        [InlineData(ResultKind.Reverted, 12)]
        [InlineData(ResultKind.Busy, 13)]
        [InlineData(ResultKind.Unexpected, 15)]
        [InlineData(ResultKind.Refused, 16)]
        public void Exit_codes_round_trip(ResultKind kind, int code)
        {
            Assert.Equal(code, ExitCodes.FromResult(kind));
            Assert.Equal(kind, ExitCodes.ToResult(code));
        }

        [Theory]
        [InlineData(14)]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(255)]
        public void Unknown_exit_codes_mean_unexpected(int code) => Assert.Equal(ResultKind.Unexpected, ExitCodes.ToResult(code));

        [Fact]
        public void Declined_has_no_exit_code_of_its_own() => Assert.Equal(ExitCodes.Unexpected, ExitCodes.FromResult(ResultKind.Declined));

        [Fact]
        public void Level_keywords_are_exact()
        {
            Assert.Equal("standard", LevelNames.ToKeyword(Level.Standard));
            Assert.Equal("strict", LevelNames.ToKeyword(Level.Strict));
            Assert.True(LevelNames.TryParse("strict", out Level l) && l == Level.Strict);
            Assert.False(LevelNames.TryParse("Strict", out _));
            Assert.False(LevelNames.TryParse(" standard", out _));
            Assert.False(LevelNames.TryParse(null, out _));
        }
    }
}
