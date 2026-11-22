using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using QuietHosts.Core;

namespace QuietHosts.Tests.Infrastructure
{
    /// <summary>Synthesized hosts files (never a copy of a real PC's file) and small test lists.</summary>
    public static class Fixtures
    {
        /// <summary>The comment header Windows ships in its hosts file, synthesized with CRLF.</summary>
        public static string StockHosts(string eol = "\r\n", bool finalNewline = true)
        {
            string[] lines =
            {
                "# Copyright (c) 1993-2009 Microsoft Corp.",
                "#",
                "# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.",
                "#",
                "# This file contains the mappings of IP addresses to host names. Each",
                "# entry should be kept on an individual line. The IP address should",
                "# be placed in the first column followed by the corresponding host name.",
                "# The IP address and the host name should be separated by at least one",
                "# space.",
                "#",
                "# Additionally, comments (such as these) may be inserted on individual",
                "# lines or following the machine name denoted by a '#' symbol.",
                "#",
                "# For example:",
                "#",
                "#      102.54.94.97     rhino.acme.com          # source server",
                "#       38.25.63.10     x.acme.com              # x client host",
                "",
                "# localhost name resolution is handled within DNS itself.",
                "#\t127.0.0.1       localhost",
                "#\t::1             localhost",
            };
            return string.Join(eol, lines) + (finalNewline ? eol : string.Empty);
        }

        public static byte[] StockHostsBytes() => Latin1.Encode(StockHosts());

        /// <summary>block-ads.ps1's layout: the stock header, one blank line, its block of 0.0.0.0 lines, the end marker.</summary>
        public static string LegacyLayout(params string[] hosts)
        {
            if (hosts.Length == 0) hosts = new[] { "a.ads-one.net", "b.ads-two.com", "ib.adnxs.com" };
            var sb = new StringBuilder(StockHosts());
            sb.Append("\r\n");
            sb.Append("# >>> block-ads.ps1 >>>\r\n");
            foreach (string h in hosts) sb.Append("0.0.0.0 ").Append(h).Append("\r\n");
            sb.Append("# <<< block-ads.ps1 <<<\r\n");
            return sb.ToString();
        }

        /// <summary>
        /// Bytes of a comment saved in a legacy ANSI code page (Windows-1252): "# Cafe 'naive' euro en-dash deja vu" with
        /// e-acute, curly quotes, i-diaeresis, the euro sign, an en dash and a-grave. Not valid UTF-8.
        /// </summary>
        public static byte[] Windows1252Comment() => Bytes.Concat(
            Bytes.Ascii("# Caf"), new byte[] { 0xE9, 0x20, 0x93 }, Bytes.Ascii("na"), new byte[] { 0xEF }, Bytes.Ascii("ve"),
            new byte[] { 0x94, 0x20, 0x80, 0x20, 0x96 }, Bytes.Ascii(" d"), new byte[] { 0xE9 }, Bytes.Ascii("j"), new byte[] { 0xE0 },
            Bytes.Ascii(" vu\r\n"));

        /// <summary>The corpus for the round-trip and idempotence properties. Every file is empty or ends with a line break.</summary>
        public static IEnumerable<object[]> Corpus()
        {
            yield return Case("empty", Array.Empty<byte>());
            yield return Case("stock", StockHostsBytes());
            yield return Case("stock-lf", Latin1.Encode(StockHosts("\n")));
            yield return Case("crlf", Bytes.Ascii("127.0.0.1 localhost\r\n::1 localhost\r\n"));
            yield return Case("lf", Bytes.Ascii("127.0.0.1 localhost\n# comment\n10.0.0.5 nas.home.lan\n"));
            yield return Case("mixed", Bytes.Ascii("# a\r\n127.0.0.1 one.example.org\n# b\r\n\n"));
            yield return Case("single-newline", Bytes.Ascii("\r\n"));
            yield return Case("blank-lines-at-end", Bytes.Ascii("127.0.0.1 localhost\r\n\r\n\r\n"));
            yield return Case("whitespace-lines", Bytes.Ascii("  \r\n\t\r\n127.0.0.1 localhost # x\r\n   \r\n"));
            yield return Case("utf8-bom", Bytes.Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Bytes.Ascii("# h"), new byte[] { 0xC3, 0xA9 }, Bytes.Ascii("llo\r\n127.0.0.1 localhost\r\n")));
            yield return Case("utf8-bom-only", new byte[] { 0xEF, 0xBB, 0xBF });
            yield return Case("windows-1252", Bytes.Concat(Bytes.Ascii("# header\r\n"), Windows1252Comment(), Bytes.Ascii("127.0.0.1 localhost\r\n")));
            yield return Case("lone-cr-inside", Bytes.Ascii("# a\rb\r\n127.0.0.1 localhost\r\n"));
            yield return Case("user-zero-lines", Bytes.Ascii("0.0.0.0 my.own-block.com\r\n# QuietHosts rocks\r\n"));
            yield return Case("high-bytes", Bytes.Concat(Bytes.Ascii("# "), Enumerable.Range(0x80, 0x80).Select(i => (byte)i).ToArray(), Bytes.Ascii("\r\n")));
        }

        /// <summary>Files without a final line break: Remove(Apply(F)) == F plus one line break.</summary>
        public static IEnumerable<object[]> NoFinalNewline()
        {
            yield return Case("stock-no-final", Latin1.Encode(StockHosts(finalNewline: false)));
            yield return Case("one-line", Bytes.Ascii("127.0.0.1 localhost"));
            yield return Case("lf-no-final", Bytes.Ascii("a\nb"));
            yield return Case("bom-no-final", Bytes.Concat(new byte[] { 0xEF, 0xBB, 0xBF }, Bytes.Ascii("# x")));
            // LF line ends, but the last line ends with a CR: adding a line break makes it a CRLF.
            yield return Case("lf-last-line-cr", Bytes.Ascii("127.0.0.1 localhost\n# last line\r"));
            yield return Case("cr-only", Bytes.Ascii("\r"));
        }

        private static object[] Case(string name, byte[] bytes) => new object[] { name, bytes };

        public static readonly string[] Hosts = { "a.ads-one.net", "b.ads-two.com", "pixel.four-ads.org", "track.metrics-three.io" };

        public static BlockContent Content(Level level = Level.Standard, params string[] hosts) =>
            new BlockContent(level, "2026-11-20", hosts.Length == 0 ? Hosts : hosts, "1.0.0");

        /// <summary>A small pair of lists for planner and status tests.</summary>
        public static BlockLists SmallLists(Guard? guard = null) => BlockLists.Parse(
            "# version: 2026-11-20\n[ad-server]\na.ads-one.net | One Ads\nb.ads-two.com | Two Ads | may stall video\n[analytics]\ntrack.metrics-three.io\n",
            "# version: 2026-11-20\n[ad-exchange]\npixel.four-ads.org | Four | links in ads won't open\nx.five-ads.net | Five | some sites break\n",
            guard ?? Guard.BuiltIn);
    }
}
