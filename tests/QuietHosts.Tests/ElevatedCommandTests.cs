using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using QuietHosts.Core;
using Xunit;

namespace QuietHosts.Tests
{
    public class ElevatedCommandTests
    {
        public static IEnumerable<object[]> Commands()
        {
            yield return new object[] { ElevatedCommand.On(Level.Standard), "--elevated on --level standard --end" };
            yield return new object[] { ElevatedCommand.On(Level.Strict), "--elevated on --level strict --end" };
            yield return new object[] { ElevatedCommand.Off(), "--elevated off --end" };
            yield return new object[] { ElevatedCommand.Undo(), "--elevated undo --end" };
            yield return new object[] { ElevatedCommand.Allow(new[] { "a.ads-one.net", "B.ads-two.com" }), "--elevated allow --count 2 a.ads-one.net b.ads-two.com --end" };
            yield return new object[] { ElevatedCommand.Block(new[] { "a.ads-one.net" }), "--elevated block --count 1 a.ads-one.net --end" };
        }

        [Theory]
        [MemberData(nameof(Commands))]
        public void Format_and_parse_round_trip(ElevatedCommand command, string expected)
        {
            Assert.Equal(expected, command.FormatArguments());
            Assert.True(ElevatedCommand.TryParse(command.ToArgs(), out ElevatedCommand? parsed, out string error), error);
            Assert.Equal(command, parsed);
            Assert.True(ElevatedCommand.IsElevatedInvocation(command.ToArgs()));
        }

        [Fact]
        public void Parsed_fields()
        {
            Assert.True(ElevatedCommand.TryParse(new[] { "--elevated", "on", "--level", "strict", "--end" }, out var on, out _));
            Assert.Equal(Verb.On, on!.Verb);
            Assert.Equal(Level.Strict, on.Level);
            Assert.Empty(on.Hosts);
            Assert.False(on.Sandbox);

            Assert.True(ElevatedCommand.TryParse(new[] { "--elevated", "allow", "--count", "3", "A.ads-one.net.", "a.ads-one.net", "b.ads-two.com", "--end" }, out var allow, out _));
            Assert.Equal(Verb.Allow, allow!.Verb);
            Assert.Null(allow.Level);
            Assert.Equal(new[] { "a.ads-one.net", "b.ads-two.com" }, allow.Hosts);
            Assert.Equal("allow (2 addresses)", allow.Describe());
        }

        [Fact]
        public void One_hundred_hosts_are_accepted()
        {
            var args = new[] { "--elevated", "block", "--count", "100" }.Concat(Enumerable.Range(0, 100).Select(i => "h" + i + ".ads-one.net")).Concat(new[] { "--end" }).ToArray();
            Assert.True(ElevatedCommand.TryParse(args, out var cmd, out _));
            Assert.Equal(100, cmd!.Hosts.Count);
        }

        public static IEnumerable<object[]> Rejected()
        {
            string[] R(params string[] a) => a;
            yield return new object[] { R() };
            yield return new object[] { R("on", "--level", "standard", "--end") };                       // missing --elevated
            yield return new object[] { R("--elevated", "--end") };                                      // missing verb
            yield return new object[] { R("--elevated", "enable", "--end") };                            // unknown verb
            yield return new object[] { R("--elevated", "On", "--level", "standard", "--end") };         // verbs are exact
            yield return new object[] { R("--elevated", "update", "--end") };
            yield return new object[] { R("--elevated", "on", "--end") };                                // missing level
            yield return new object[] { R("--elevated", "on", "--level", "--end") };
            yield return new object[] { R("--elevated", "on", "--level", "Standard", "--end") };         // bad level
            yield return new object[] { R("--elevated", "on", "--level", "default", "--end") };
            yield return new object[] { R("--elevated", "on", "--level", "strict", "--level", "strict", "--end") };
            yield return new object[] { R("--elevated", "on", "--tier", "strict", "--end") };            // unknown option
            yield return new object[] { R("--elevated", "on", "--level", "standard", "--staged", @"C:\x", "--end") };
            yield return new object[] { R("--elevated", "off", "extra", "--end") };                      // extra args
            yield return new object[] { R("--elevated", "undo", "--force", "--end") };
            yield return new object[] { R("--elevated", "allow", "--end") };                             // 0 hosts
            yield return new object[] { R("--elevated", "block", "--count", "1", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "not a host", "--end") };   // invalid host
            yield return new object[] { R("--elevated", "allow", "--count", "1", "localhost", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "1.2.3.4", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "3", "a.ads-one.net", "--level", "strict", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", @"C:\Windows\System32\drivers\etc\hosts", "--end") };   // paths
            yield return new object[] { R("--elevated", "allow", "--count", "1", @"..\hosts", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "../hosts.txt", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "/etc/hosts", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", @"\\server\share\x.com", "--end") };     // UNC
            yield return new object[] { R("--elevated", "allow", "--count", "1", @"\\?\C:\x.com", "--end") };
            yield return new object[] { R("--elevated", "block", "--count", "1", "file:x.com", "--end") };
            yield return new object[] { R("--elevated", "block", "--count", "1", "", "--end") };
            yield return new object[] { R("--elevated", "off", "--elevated", "off", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", (char)0x212A + "x.com", "--end") };
            yield return new object[] { R("x", "--elevated", "off", "--end") };
            yield return new object[] { R("--ELEVATED", "off", "--end") };

            // The end marker: last, exactly once, exactly "--end".
            yield return new object[] { R("--elevated", "off") };
            yield return new object[] { R("--elevated", "on", "--level", "standard") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "a.ads-one.net") };
            yield return new object[] { R("--elevated", "off", "--end", "--end") };
            yield return new object[] { R("--elevated", "--end", "off", "--end") };
            yield return new object[] { R("--elevated", "off", "--end", "x") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "a.ads-one.net", "--end", "b.ads-two.com") };
            yield return new object[] { R("--elevated", "off", "--END") };
            yield return new object[] { R("--elevated", "off", "--end ") };

            // The count: right after the verb, 1 to 100 without leading zeros, equal to the addresses given.
            yield return new object[] { R("--elevated", "allow", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "a.ads-one.net", "--count", "1", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "0", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "01", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "007", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "+1", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "one", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1\n", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1 ", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "١", "a.ads-one.net", "--end") };   // Arabic-Indic digit one
            yield return new object[] { R("--elevated", "allow", "--count", "2", "a.ads-one.net", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "1", "a.ads-one.net", "b.ads-two.com", "--end") };
            yield return new object[] { R("--elevated", "allow", "--count", "--end") };
            yield return new object[] { R("--elevated", "block", "--count", "1", "--count", "1", "a.ads-one.net", "--end") };
        }

        [Theory]
        [MemberData(nameof(Rejected))]
        public void Anything_else_is_rejected(string[] args)
        {
            Assert.False(ElevatedCommand.TryParse(args, out ElevatedCommand? cmd, out string error));
            Assert.Null(cmd);
            Assert.NotEqual(string.Empty, error);
        }

        [Fact]
        public void The_count_counts_the_addresses_given_even_if_two_are_the_same()
        {
            Assert.True(ElevatedCommand.TryParse(new[] { "--elevated", "allow", "--count", "2", "a.ads-one.net", "A.ads-one.net", "--end" }, out var cmd, out _));
            Assert.Equal(new[] { "a.ads-one.net" }, cmd!.Hosts);
            Assert.False(ElevatedCommand.TryParse(new[] { "--elevated", "allow", "--count", "1", "a.ads-one.net", "A.ads-one.net", "--end" }, out _, out _));
        }

        [Fact]
        public void More_than_100_hosts_are_rejected()
        {
            var hosts = Enumerable.Range(0, 101).Select(i => "h" + i + ".ads-one.net").ToArray();
            var args = new[] { "--elevated", "allow", "--count", "101" }.Concat(hosts).Concat(new[] { "--end" }).ToArray();
            Assert.False(ElevatedCommand.TryParse(args, out _, out _));
            var counted100 = new[] { "--elevated", "allow", "--count", "100" }.Concat(hosts).Concat(new[] { "--end" }).ToArray();
            Assert.False(ElevatedCommand.TryParse(counted100, out _, out _));
            Assert.Throws<ArgumentException>(() => ElevatedCommand.Allow(hosts));
            Assert.Throws<ArgumentException>(() => ElevatedCommand.Block(new string[0]));
            Assert.Throws<ArgumentException>(() => ElevatedCommand.Block(new[] { "not a host" }));
        }

        [Fact]
        public void Null_args_are_rejected()
        {
            Assert.False(ElevatedCommand.TryParse(null, out _, out _));
            Assert.False(ElevatedCommand.IsElevatedInvocation(null));
            Assert.False(ElevatedCommand.IsElevatedInvocation(new string[0]));
            Assert.False(ElevatedCommand.IsElevatedInvocation(new[] { "--render-ui" }));
        }

        // ------------------------------------------------------------ the command line as Windows passes it

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        /// <summary>How the elevated run's Main sees a command line: Windows' own splitting, without the exe.</summary>
        private static string[] ArgsOf(string commandLine)
        {
            IntPtr argv = CommandLineToArgvW(commandLine, out int count);
            if (argv == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var args = new string[count];
                for (int i = 0; i < count; i++) args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
                return args.Skip(1).ToArray();
            }
            finally
            {
                LocalFree(argv);
            }
        }

        private const string ExePath = "\"C:\\Program Files\\Some Folder\\QuietHosts.exe\" ";
        private static readonly BlockLists Lists = BlockLists.LoadEmbedded();

        private static IEnumerable<ElevatedCommand> ShippedCommands()
        {
            var hosts = Lists.Entries.Select(e => e.Host).ToList();
            for (int i = 0; i < hosts.Count; i += Limits.MaxHostsPerRun)
            {
                var chunk = hosts.Skip(i).Take(Limits.MaxHostsPerRun).ToList();
                yield return ElevatedCommand.Allow(chunk);
                yield return ElevatedCommand.Block(chunk);
            }
            yield return ElevatedCommand.Allow(hosts.OrderByDescending(h => h.Length).ThenBy(h => h, StringComparer.Ordinal).Take(Limits.MaxHostsPerRun));
            yield return ElevatedCommand.On(Level.Strict);
            yield return ElevatedCommand.Off();
            yield return ElevatedCommand.Undo();
        }

        [Fact]
        public void Every_shipped_address_survives_the_command_line_in_chunks_of_100()
        {
            foreach (ElevatedCommand command in ShippedCommands())
            {
                string[] args = ArgsOf(ExePath + command.FormatArguments());
                Assert.Equal(command.ToArgs(), args);
                Assert.True(ElevatedCommand.TryParse(args, out ElevatedCommand? parsed, out string error), error);
                Assert.Equal(command, parsed);
            }
        }

        [Fact]
        public void A_command_line_cut_anywhere_never_parses()
        {
            var longest = ElevatedCommand.Allow(Lists.Entries.Select(e => e.Host).OrderByDescending(h => h.Length).ThenBy(h => h, StringComparer.Ordinal).Take(Limits.MaxHostsPerRun));
            foreach (ElevatedCommand command in new[] { longest, ElevatedCommand.Block(longest.Hosts.Take(3)), ElevatedCommand.On(Level.Standard), ElevatedCommand.Off(), ElevatedCommand.Undo() })
            {
                string full = ExePath + command.FormatArguments();
                for (int length = ExePath.Length; length < full.Length; length++)
                {
                    string[] args = ArgsOf(full.Substring(0, length));
                    Assert.False(ElevatedCommand.TryParse(args, out _, out _), "cut at " + length + ": " + full.Substring(ExePath.Length, length - ExePath.Length));
                }
            }
        }

        [Fact]
        public void Formatted_commands_need_no_quoting()
        {
            foreach (ElevatedCommand command in ShippedCommands())
                Assert.Matches("^[a-z0-9.-]+( [a-z0-9.-]+)*$", command.FormatArguments());
        }

#if DEBUG
        [Fact]
        public void Sandbox_prefix_is_accepted_in_debug_builds_only_at_the_start()
        {
            Assert.True(ElevatedCommand.TryParse(new[] { "--sandbox", "--elevated", "off", "--end" }, out var cmd, out _));
            Assert.True(cmd!.Sandbox);
            Assert.Equal("--sandbox --elevated off --end", cmd.FormatArguments());
            Assert.True(ElevatedCommand.IsElevatedInvocation(new[] { "--sandbox", "--elevated", "off", "--end" }));
            Assert.False(ElevatedCommand.TryParse(new[] { "--elevated", "--sandbox", "off", "--end" }, out _, out _));
            Assert.False(ElevatedCommand.TryParse(new[] { "--sandbox", "--sandbox", "--elevated", "off", "--end" }, out _, out _));
            Assert.False(ElevatedCommand.TryParse(new[] { "--sandbox", @"C:\Temp\x", "--elevated", "off", "--end" }, out _, out _));
            Assert.Equal(ElevatedCommand.Off().WithSandbox(true), cmd);
        }
#else
        [Fact]
        public void Sandbox_prefix_is_rejected_in_release_builds()
        {
            Assert.False(ElevatedCommand.TryParse(new[] { "--sandbox", "--elevated", "off", "--end" }, out _, out _));
        }
#endif
    }
}
