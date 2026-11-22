using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>
    /// One elevated run's command line: "--elevated on --level standard|strict --end", "--elevated off --end",
    /// "--elevated allow --count N host... --end", "--elevated block --count N host... --end" or
    /// "--elevated undo --end". Debug builds also accept a leading "--sandbox". Parsing is strict: exact words,
    /// 1-100 valid hosts matching the count, nothing else. The final "--end" means a command line cut short on
    /// its way to the elevated run never parses as a shorter command.
    /// </summary>
    public sealed class ElevatedCommand
    {
        public const string ElevatedOption = "--elevated";
        public const string SandboxOption = "--sandbox";
        public const string LevelOption = "--level";
        public const string CountOption = "--count";
        public const string EndOption = "--end";

        // \z, not $: "$" would also match before a final line break.
        private static readonly Regex Token = new Regex(@"^[a-z0-9.-]+\z", RegexOptions.CultureInvariant);
        private static readonly Regex Count = new Regex(@"^[1-9][0-9]{0,2}\z", RegexOptions.CultureInvariant);

        private ElevatedCommand(Verb verb, Level? level, IReadOnlyList<string> hosts, bool sandbox)
        {
            Verb = verb;
            Level = level;
            Hosts = hosts;
            Sandbox = sandbox;
        }

        public Verb Verb { get; }
        /// <summary>The level for On; null for the other verbs.</summary>
        public Level? Level { get; }
        /// <summary>The normalized hosts for Allow and Block (unique, in the given order); empty otherwise.</summary>
        public IReadOnlyList<string> Hosts { get; }
        /// <summary>True for "--sandbox --elevated ..." (Debug builds only).</summary>
        public bool Sandbox { get; }

        public static ElevatedCommand On(Level level) => new ElevatedCommand(Verb.On, level, Array.Empty<string>(), false);
        public static ElevatedCommand Off() => new ElevatedCommand(Verb.Off, null, Array.Empty<string>(), false);
        public static ElevatedCommand Undo() => new ElevatedCommand(Verb.Undo, null, Array.Empty<string>(), false);
        public static ElevatedCommand Allow(IEnumerable<string> hosts) => WithHosts(Verb.Allow, hosts);
        public static ElevatedCommand Block(IEnumerable<string> hosts) => WithHosts(Verb.Block, hosts);

        /// <summary>The same command with the sandbox flag set or cleared.</summary>
        public ElevatedCommand WithSandbox(bool sandbox) => new ElevatedCommand(Verb, Level, Hosts, sandbox);

        private static ElevatedCommand WithHosts(Verb verb, IEnumerable<string> hosts)
        {
            if (hosts == null) throw new ArgumentNullException(nameof(hosts));
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in hosts)
            {
                if (!HostName.TryNormalize(raw, out string host)) throw new ArgumentException("Not a valid address: " + Guard.Shorten(raw ?? string.Empty), nameof(hosts));
                if (seen.Add(host)) list.Add(host);
            }
            if (list.Count == 0 || list.Count > Limits.MaxHostsPerRun)
                throw new ArgumentException("Allow and Block take 1 to 100 addresses.", nameof(hosts));
            return new ElevatedCommand(verb, null, list, false);
        }

        /// <summary>True if the arguments ask for an elevated run (Program.Main sends these to ElevatedMain.Run).</summary>
        public static bool IsElevatedInvocation(string[]? args)
        {
            if (args == null || args.Length == 0) return false;
            if (string.Equals(args[0], ElevatedOption, StringComparison.Ordinal)) return true;
            return args.Length > 1 && string.Equals(args[0], SandboxOption, StringComparison.Ordinal) &&
                   string.Equals(args[1], ElevatedOption, StringComparison.Ordinal);
        }

        /// <summary>Parses the command line strictly. On failure, <paramref name="error"/> says why (for LastRun).</summary>
        public static bool TryParse(string[]? args, out ElevatedCommand? command, out string error)
        {
            command = null;
            error = string.Empty;
            if (args == null || args.Length == 0) { error = "no arguments"; return false; }
            int i = 0;
            bool sandbox = false;
            if (string.Equals(args[0], SandboxOption, StringComparison.Ordinal))
            {
#if DEBUG
                sandbox = true;
                i = 1;
#else
                error = "--sandbox exists only in Debug builds";
                return false;
#endif
            }
            if (args.Length <= i || !string.Equals(args[i], ElevatedOption, StringComparison.Ordinal)) { error = "expected --elevated"; return false; }
            i++;
            if (args.Length <= i) { error = "missing verb"; return false; }
            string verb = args[i++];
            // Exactly one --end, as the last argument.
            int end = args.Length - 1;
            if (end < i || !string.Equals(args[end], EndOption, StringComparison.Ordinal)) { error = "missing --end"; return false; }
            for (int k = 0; k < end; k++)
            {
                if (string.Equals(args[k], EndOption, StringComparison.Ordinal)) { error = "--end must come last, once"; return false; }
            }
            int rest = end - i;
            switch (verb)
            {
                case "on":
                    if (rest != 2 || !string.Equals(args[i], LevelOption, StringComparison.Ordinal)) { error = "on needs exactly --level standard|strict"; return false; }
                    if (!LevelNames.TryParse(args[i + 1], out Level level)) { error = "the level must be standard or strict"; return false; }
                    command = new ElevatedCommand(Verb.On, level, Array.Empty<string>(), sandbox);
                    return true;
                case "off":
                case "undo":
                    if (rest != 0) { error = verb + " takes no arguments"; return false; }
                    command = new ElevatedCommand(verb == "off" ? Verb.Off : Verb.Undo, null, Array.Empty<string>(), sandbox);
                    return true;
                case "allow":
                case "block":
                    if (rest < 2 || !string.Equals(args[i], CountOption, StringComparison.Ordinal)) { error = verb + " needs --count first"; return false; }
                    string countText = args[i + 1] ?? string.Empty;
                    if (!Count.IsMatch(countText)) { error = "the count must be a number from 1 to 100"; return false; }
                    int count = int.Parse(countText, NumberStyles.None, CultureInfo.InvariantCulture);
                    i += 2;
                    int given = end - i;
                    if (count > Limits.MaxHostsPerRun || given < 1 || given > Limits.MaxHostsPerRun) { error = verb + " takes 1 to 100 addresses"; return false; }
                    if (given != count) { error = "the count doesn't match the addresses given"; return false; }
                    var hosts = new List<string>(given);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (; i < end; i++)
                    {
                        string a = args[i] ?? string.Empty;
                        if (a.StartsWith("-", StringComparison.Ordinal)) { error = "unknown option"; return false; }
                        if (LooksLikePath(a)) { error = "paths are not accepted"; return false; }
                        if (!HostName.TryNormalize(a, out string host)) { error = "not a valid address"; return false; }
                        if (seen.Add(host)) hosts.Add(host);
                    }
                    command = new ElevatedCommand(verb == "allow" ? Verb.Allow : Verb.Block, null, hosts, sandbox);
                    return true;
                default:
                    error = "unknown verb";
                    return false;
            }
        }

        private static bool LooksLikePath(string a) =>
            a.IndexOf('\\') >= 0 || a.IndexOf('/') >= 0 || a.IndexOf(':') >= 0 || a.StartsWith(".", StringComparison.Ordinal);

        /// <summary>The arguments, for example ["--elevated", "on", "--level", "strict", "--end"].</summary>
        public string[] ToArgs()
        {
            var args = new List<string>();
            if (Sandbox) args.Add(SandboxOption);
            args.Add(ElevatedOption);
            switch (Verb)
            {
                case Verb.On:
                    args.Add("on");
                    args.Add(LevelOption);
                    args.Add(LevelNames.ToKeyword(Level ?? Core.Level.Standard));
                    break;
                case Verb.Off: args.Add("off"); break;
                case Verb.Undo: args.Add("undo"); break;
                case Verb.Allow:
                case Verb.Block:
                    args.Add(Verb == Verb.Allow ? "allow" : "block");
                    args.Add(CountOption);
                    args.Add(Inv.Num(Hosts.Count));
                    args.AddRange(Hosts);
                    break;
            }
            args.Add(EndOption);
            return args.ToArray();
        }

        /// <summary>
        /// The command line for ProcessStartInfo.Arguments. Every token is [a-z0-9.-], so no quoting is needed;
        /// anything else throws rather than reach the elevated run.
        /// </summary>
        public string FormatArguments()
        {
            string[] args = ToArgs();
            foreach (string a in args)
            {
                if (!Token.IsMatch(a)) throw new InvalidOperationException("An elevated argument needs quoting: " + Guard.Shorten(a));
            }
            return string.Join(" ", args);
        }

        /// <summary>A short description for LastRun: the verb and level or host count, never the names.</summary>
        public string Describe()
        {
            switch (Verb)
            {
                case Verb.On: return "on --level " + LevelNames.ToKeyword(Level ?? Core.Level.Standard);
                case Verb.Allow: return "allow (" + Inv.Num(Hosts.Count) + " addresses)";
                case Verb.Block: return "block (" + Inv.Num(Hosts.Count) + " addresses)";
                default: return Verb.ToString().ToLowerInvariant();
            }
        }

        public override string ToString() => FormatArguments();

        public override bool Equals(object? obj) =>
            obj is ElevatedCommand o && o.Verb == Verb && o.Level == Level && o.Sandbox == Sandbox && o.Hosts.SequenceEqual(Hosts, StringComparer.Ordinal);

        public override int GetHashCode() => ((int)Verb * 397) ^ (Level.HasValue ? (int)Level.Value + 1 : 0) ^ Hosts.Count ^ (Sandbox ? 1 << 20 : 0);
    }
}
