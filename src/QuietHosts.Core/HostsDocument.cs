using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace QuietHosts.Core
{
    /// <summary>
    /// A hosts file as bytes. Decodes as Latin-1 so every byte round-trips, finds QuietHosts'
    /// block and the older block-ads.ps1 block, reports what the status needs, and computes the bytes
    /// after a change (<see cref="Apply"/>, <see cref="Remove"/>). Lines outside the managed blocks never change.
    /// </summary>
    public sealed partial class HostsDocument
    {
        public const string BeginMarker = "# >>> QuietHosts >>>";
        public const string EndMarker = "# <<< QuietHosts <<<";
        public const string LegacyBeginMarker = "# >>> block-ads.ps1 >>>";
        public const string LegacyEndMarker = "# <<< block-ads.ps1 <<<";
        public const string ManagedByLinePrefix = "# Managed by QuietHosts: use the app to change these lines.";
        public const string HeaderPrefix = "# QuietHosts ";
        public const string EntryPrefix = "0.0.0.0 ";
        internal const string ManagedByShapePrefix = "# Managed by QuietHosts";
        internal static readonly string Utf8BomChars = new string(new[] { (char)0xEF, (char)0xBB, (char)0xBF });

        // Trailing CRs too: line ends translated twice leave one on every line.
        private static readonly Regex HandDisabled = new Regex(@"^#[ \t]*0\.0\.0\.0[ \t]+([!-~]+)[ \t\r]*$", RegexOptions.CultureInvariant);
        // Our entry as another program may rewrite it inside our block: a tab instead of the space, or trailing
        // spaces, tabs or CRs (line ends translated twice). One separator only: "0.0.0.0  host" stays foreign.
        private static readonly Regex LooseEntry = new Regex(@"^0\.0\.0\.0[ \t]([!-~]+)[ \t\r]*$", RegexOptions.CultureInvariant);
        private static readonly char[] Blanks = { ' ', '\t', '\v', '\f', '\r' };

        private enum Kind { Other, Begin, End, LegacyBegin, LegacyEnd }

        private enum Role
        {
            Outside,
            /// <summary>A marker or generated line of a managed block, or a line the repair removes.</summary>
            Dropped,
            /// <summary>A hand-disabled "#0.0.0.0 host" line inside our block (its host moves to the allowlist).</summary>
            HandDisabled,
            /// <summary>Another line inside a managed block: it moves, unchanged, below the block.</summary>
            Foreign,
        }

        private readonly struct Line
        {
            public Line(string text, string eol) { Text = text; Eol = eol; }
            public string Text { get; }
            public string Eol { get; }
        }

        private readonly byte[] _bytes;
        private readonly string _prefix = string.Empty;
        private readonly List<Line> _lines = new List<Line>();
        private readonly Role[] _roles = Array.Empty<Role>();
        private readonly bool[] _stray = Array.Empty<bool>();
        private readonly int _begin = -1, _end = -1, _legacyBegin = -1, _legacyEnd = -1;
        private readonly List<string> _blockHosts = new List<string>();
        private readonly List<string> _handDisabled = new List<string>();
        private readonly HashSet<string> _mappedOutside = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _microsoftLines = new List<string>();

        /// <summary>At most this many <see cref="MicrosoftLines"/> are kept (the count is not capped).</summary>
        public const int MaxMicrosoftLinesShown = 50;
        /// <summary>Each of the <see cref="MicrosoftLines"/> is cut to this many characters (the hash uses the whole line).</summary>
        public const int MaxMicrosoftLineLength = 200;

        private HostsDocument(byte[] bytes, bool exists)
        {
            _bytes = bytes;
            Exists = exists;
            Refusal = CheckBytes(bytes);
            LineEnding = "\r\n";
            if (Refusal != RefusalReason.None) return;

            string text = Latin1.Decode(bytes);
            if (text.StartsWith(Utf8BomChars, StringComparison.Ordinal))
            {
                _prefix = Utf8BomChars;
                text = text.Substring(Utf8BomChars.Length);
            }
            SplitLines(text, _lines, out bool hasCrlf, out bool hasLoneLf);
            LineEnding = hasLoneLf && !hasCrlf ? "\n" : "\r\n";

            int n = _lines.Count;
            var kinds = new Kind[n];
            for (int i = 0; i < n; i++) kinds[i] = Classify(_lines[i].Text);
            _roles = new Role[n];
            _stray = new bool[n];
            PairMarkers(kinds, _stray, out _begin, out _end, out _legacyBegin, out _legacyEnd);
            MalformedMarkers = Array.IndexOf(_stray, true) >= 0;

            AssignRoles(kinds);
            AnalyzeBlock();
            AnalyzeOutside();
        }

        /// <summary>Parses file bytes; null means the file is missing (it counts as empty).</summary>
        public static HostsDocument Parse(byte[]? bytes) => bytes == null ? new HostsDocument(Array.Empty<byte>(), false) : new HostsDocument(bytes, true);

        /// <summary>The bytes this document was parsed from.</summary>
        public byte[] Bytes => (byte[])_bytes.Clone();
        public long Length => _bytes.LongLength;
        /// <summary>False when the file was missing.</summary>
        public bool Exists { get; }
        /// <summary>Why the file can't safely be changed (UnsupportedEncoding, TooLarge), or None.</summary>
        public RefusalReason Refusal { get; }
        public bool IsRefused => Refusal != RefusalReason.None;
        /// <summary>"\r\n", unless the file has lone LFs and no CRLF.</summary>
        public string LineEnding { get; }

        /// <summary>QuietHosts' block (a well-formed marker pair) is present.</summary>
        public bool HasBlock => _begin >= 0;
        /// <summary>The block's header line, or null if the block has none or it is damaged.</summary>
        public BlockHeader? Header { get; private set; }
        /// <summary>The entry lines of the block don't match the header (or the header is damaged).</summary>
        public bool BlockEdited { get; private set; }
        /// <summary>
        /// The "0.0.0.0 host" entries of QuietHosts' block, in file order, including entries another program rewrote
        /// (a tab for the space, trailing blanks or CRs; the host lowercased).
        /// </summary>
        public IReadOnlyList<string> BlockHosts => _blockHosts;
        /// <summary>Hosts of hand-disabled "#0.0.0.0 host" lines inside QuietHosts' block.</summary>
        public IReadOnlyList<string> HandDisabledHosts => _handDisabled;
        /// <summary>Lines inside the managed blocks that QuietHosts didn't generate; they move below the block at the next write.</summary>
        public int ForeignLinesInBlocks { get; private set; }

        /// <summary>The older block-ads.ps1 block (a well-formed marker pair) is present.</summary>
        public bool HasLegacyBlock => _legacyBegin >= 0;
        /// <summary>"0.0.0.0 host" lines in the block-ads.ps1 block.</summary>
        public int LegacyCount { get; private set; }

        /// <summary>Unpaired, duplicated or nested markers; they are repaired at the next write.</summary>
        public bool MalformedMarkers { get; }
        /// <summary>Bytes outside QuietHosts' block and the block-ads.ps1 block.</summary>
        public long BytesOutsideBlocks { get; private set; }
        /// <summary>
        /// An active line that stays outside the managed blocks maps one of Microsoft's own service names, which may make
        /// Windows Defender reset the file (<see cref="Guard.IsDefenderSensitive"/>; narrower than the Microsoft guard).
        /// </summary>
        public bool MicrosoftLinesOutside { get; private set; }
        /// <summary>Names mapped (any IP) on active lines that stay outside the managed blocks. QuietHosts never writes them.</summary>
        public IReadOnlyCollection<string> NamesMappedOutside => _mappedOutside;

        /// <summary>
        /// SHA-256 hex of the Latin-1 bytes of every line strictly between our markers, joined with "\n"; null without a
        /// well-formed block (see <see cref="HostsStatus.BlockLinesSha256"/>).
        /// </summary>
        public string? BlockLinesSha256 { get; private set; }
        /// <summary>
        /// The lines that raise <see cref="MicrosoftLinesOutside"/> (role outside or foreign, mapping a Defender-sensitive
        /// name), in file order, trailing blanks removed, each cut to <see cref="MaxMicrosoftLineLength"/> characters, at
        /// most <see cref="MaxMicrosoftLinesShown"/>.
        /// </summary>
        public IReadOnlyList<string> MicrosoftLines => _microsoftLines;
        /// <summary>How many lines raise <see cref="MicrosoftLinesOutside"/>.</summary>
        public int MicrosoftLineCount { get; private set; }
        /// <summary>SHA-256 hex of the Latin-1 bytes of all those lines (whole, trailing blanks removed) joined with "\n"; null when none.</summary>
        public string? MicrosoftLinesSha256 { get; private set; }

        /// <summary>The warnings this file raises by itself (the status adds ChangeNotFinished).</summary>
        public HostsWarnings Warnings
        {
            get
            {
                var w = HostsWarnings.None;
                if (IsRefused) return w;
                if (HasBlock && BlockEdited) w |= HostsWarnings.EditedBlock;
                if (MalformedMarkers) w |= HostsWarnings.MalformedMarkers;
                if (BytesOutsideBlocks > Limits.LargeFileBytes) w |= HostsWarnings.LargeFile;
                if (MicrosoftLinesOutside) w |= HostsWarnings.MicrosoftLinesOutside;
                if (HasBlock && HasLegacyBlock) w |= HostsWarnings.LegacyBlockToo;
                return w;
            }
        }

        internal static RefusalReason CheckBytes(byte[] bytes)
        {
            if (bytes.LongLength > Limits.MaxHostsFileBytes) return RefusalReason.TooLarge;
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
                return RefusalReason.UnsupportedEncoding;
            if (Array.IndexOf(bytes, (byte)0) >= 0) return RefusalReason.UnsupportedEncoding;
            return RefusalReason.None;
        }

        private static void SplitLines(string text, List<Line> lines, out bool hasCrlf, out bool hasLoneLf)
        {
            hasCrlf = false;
            hasLoneLf = false;
            int start = 0;
            while (start < text.Length)
            {
                int lf = text.IndexOf('\n', start);
                if (lf < 0)
                {
                    // A last line ending in CR becomes a CRLF as soon as a line break follows it, so the file
                    // already counts as CRLF; otherwise the second write would switch every line ending.
                    string last = text.Substring(start);
                    if (last[last.Length - 1] == '\r') hasCrlf = true;
                    lines.Add(new Line(last, string.Empty));
                    break;
                }
                if (lf > start && text[lf - 1] == '\r')
                {
                    hasCrlf = true;
                    lines.Add(new Line(text.Substring(start, lf - 1 - start), "\r\n"));
                }
                else
                {
                    hasLoneLf = true;
                    lines.Add(new Line(text.Substring(start, lf - start), "\n"));
                }
                start = lf + 1;
            }
        }

        private static Kind Classify(string text)
        {
            if (text.Length < 10 || text.IndexOf('#') < 0) return Kind.Other;
            string t = text.Trim();
            if (string.Equals(t, BeginMarker, StringComparison.Ordinal)) return Kind.Begin;
            if (string.Equals(t, EndMarker, StringComparison.Ordinal)) return Kind.End;
            if (string.Equals(t, LegacyBeginMarker, StringComparison.OrdinalIgnoreCase)) return Kind.LegacyBegin;
            if (string.Equals(t, LegacyEndMarker, StringComparison.OrdinalIgnoreCase)) return Kind.LegacyEnd;
            return Kind.Other;
        }

        // Markers pair up in order: a begin followed by the end of its own kind, with no other marker
        // between. The first pair of each kind is the block; every other marker is stray.
        private static void PairMarkers(Kind[] kinds, bool[] stray, out int begin, out int end, out int legacyBegin, out int legacyEnd)
        {
            begin = end = legacyBegin = legacyEnd = -1;
            int open = -1;
            for (int i = 0; i < kinds.Length; i++)
            {
                Kind k = kinds[i];
                if (k == Kind.Other) continue;
                if (k == Kind.Begin || k == Kind.LegacyBegin)
                {
                    if (open >= 0) stray[open] = true;
                    open = i;
                    continue;
                }
                Kind wanted = k == Kind.End ? Kind.Begin : Kind.LegacyBegin;
                if (open >= 0 && kinds[open] == wanted)
                {
                    if (k == Kind.End && begin < 0) { begin = open; end = i; }
                    else if (k == Kind.LegacyEnd && legacyBegin < 0) { legacyBegin = open; legacyEnd = i; }
                    else { stray[open] = true; stray[i] = true; }
                    open = -1;
                }
                else
                {
                    if (open >= 0) stray[open] = true;
                    stray[i] = true;
                    open = -1;
                }
            }
            if (open >= 0) stray[open] = true;
        }

        private void AssignRoles(Kind[] kinds)
        {
            int n = _lines.Count;
            for (int i = 0; i < n; i++) _roles[i] = Role.Outside;

            // Repair: drop stray markers and, next to them, lines of our exact generated shapes: after a stray
            // begin up to the next marker, and before a stray end (see StrayEndRepairStart). A damaged block that our
            // Managed-by or header line proves is ours also loses the one blank line directly before it, as a
            // well-formed block does, so Off gives the file back and repairs don't add blank lines.
            for (int i = 0; i < n; i++)
            {
                if (!_stray[i]) continue;
                _roles[i] = Role.Dropped;
                if (kinds[i] == Kind.Begin || kinds[i] == Kind.LegacyBegin)
                {
                    bool anchored = false;
                    for (int j = i + 1; j < n && kinds[j] == Kind.Other; j++)
                    {
                        if (IsGeneratedShape(_lines[j].Text)) _roles[j] = Role.Dropped;
                        if (IsRepairAnchor(_lines[j].Text)) anchored = true;
                    }
                    if (anchored && kinds[i] == Kind.Begin) DropBlankLineBefore(i);
                }
                else
                {
                    int start = StrayEndRepairStart(kinds, i, out bool anchored);
                    for (int j = start; j >= 0 && j < i; j++)
                    {
                        // From our own Managed-by or header line on, the lines are our block's, so an entry another
                        // program rewrote is ours too (as inside a well-formed block). Without that proof, exact shapes only.
                        string t = _lines[j].Text;
                        if (IsGeneratedShape(t) || (anchored && IsLooseEntry(t))) _roles[j] = Role.Dropped;
                    }
                    if (anchored) DropBlankLineBefore(start);
                }
            }

            if (_begin >= 0)
            {
                _roles[_begin] = Role.Dropped;
                _roles[_end] = Role.Dropped;
                for (int i = _begin + 1; i < _end; i++)
                {
                    string t = _lines[i].Text;
                    if (IsGeneratedShape(t) || IsLooseEntry(t)) _roles[i] = Role.Dropped;
                    else if (TryHandDisabled(t, out string host)) { _roles[i] = Role.HandDisabled; _handDisabled.Add(host); }
                    else { _roles[i] = Role.Foreign; ForeignLinesInBlocks++; }
                }
            }
            if (_legacyBegin >= 0)
            {
                _roles[_legacyBegin] = Role.Dropped;
                _roles[_legacyEnd] = Role.Dropped;
                for (int i = _legacyBegin + 1; i < _legacyEnd; i++)
                {
                    string t = _lines[i].Text;
                    if (TryEntry(t, out _)) { _roles[i] = Role.Dropped; LegacyCount++; }
                    else { _roles[i] = Role.Foreign; ForeignLinesInBlocks++; }
                }
            }
        }

        // The first line the repair before a stray end marker covers, or -1 (only the marker goes). If our
        // Managed-by or header line sits between the previous marker and this end (the begin marker was deleted),
        // the repair starts there, so lines above the block stay. Otherwise it starts after the previous marker:
        // the tail of an older block that a torn shorter write left after the new one. With neither, it is -1.
        // <paramref name="anchored"/>: the repair starts at such a line.
        private int StrayEndRepairStart(Kind[] kinds, int end, out bool anchored)
        {
            anchored = false;
            int previous = end - 1;
            while (previous >= 0 && kinds[previous] == Kind.Other) previous--;
            for (int j = end - 1; j > previous; j--)
            {
                if (!IsRepairAnchor(_lines[j].Text)) continue;
                while (j - 1 > previous && IsRepairAnchor(_lines[j - 1].Text)) j--;
                anchored = true;
                return j;
            }
            return previous >= 0 ? previous + 1 : -1;
        }

        // The empty line directly before a damaged block of ours: Apply put it there when it appended the block.
        private void DropBlankLineBefore(int first)
        {
            int j = first - 1;
            if (j >= 0 && _roles[j] == Role.Outside && _lines[j].Text.Length == 0) _roles[j] = Role.Dropped;
        }

        // Lines only QuietHosts writes, at the top of its block: the Managed-by line and a header that parses.
        private static bool IsRepairAnchor(string text) =>
            text.StartsWith(ManagedByShapePrefix, StringComparison.Ordinal) || BlockHeader.TryParse(text) != null;

        private void AnalyzeBlock()
        {
            if (_begin < 0) return;
            BlockHeader? header = null;
            int headers = 0;
            var entryLines = new List<string>();
            var allLines = new List<string>(_end - _begin);
            for (int i = _begin + 1; i < _end; i++)
            {
                string t = _lines[i].Text;
                allLines.Add(t);
                if (TryEntry(t, out string host)) { _blockHosts.Add(host); entryLines.Add(t); }
                // An entry another program rewrote is still ours and still blocks, but only exact lines count for the
                // header's sha256, so EditedBlock shows.
                else if (TryLooseEntry(t, out host)) _blockHosts.Add(host);
                else if (t.StartsWith(HeaderPrefix, StringComparison.Ordinal)) { headers++; header ??= BlockHeader.TryParse(t); }
            }
            Header = headers == 1 ? header : null;
            BlockEdited = Header == null
                || Header.HostCount != entryLines.Count
                || !string.Equals(Header.Sha256, Hashing.Sha256Hex(string.Join("\n", entryLines)), StringComparison.Ordinal);
            // Every line, not only the entries: any change inside the block changes this fingerprint.
            BlockLinesSha256 = Hashing.Sha256Hex(Latin1.Encode(string.Join("\n", allLines)));
        }

        private void AnalyzeOutside()
        {
            long outside = _prefix.Length;
            System.Text.StringBuilder? microsoft = null;
            for (int i = 0; i < _lines.Count; i++)
            {
                bool inBlock = (_begin >= 0 && i >= _begin && i <= _end) || (_legacyBegin >= 0 && i >= _legacyBegin && i <= _legacyEnd);
                if (!inBlock) outside += _lines[i].Text.Length + _lines[i].Eol.Length;
                Role r = _roles[i];
                if (r != Role.Outside && r != Role.Foreign) continue;
                bool sensitive = false;
                foreach (string name in MappedNames(_lines[i].Text))
                {
                    _mappedOutside.Add(name);
                    if (Guard.IsDefenderSensitive(name)) sensitive = true;
                }
                if (!sensitive) continue;
                MicrosoftLinesOutside = true;
                string line = _lines[i].Text.TrimEnd(Blanks);
                if (microsoft == null) microsoft = new System.Text.StringBuilder();
                else microsoft.Append('\n');
                microsoft.Append(line);
                MicrosoftLineCount++;
                if (_microsoftLines.Count < MaxMicrosoftLinesShown)
                    _microsoftLines.Add(line.Length <= MaxMicrosoftLineLength ? line : line.Substring(0, MaxMicrosoftLineLength));
            }
            BytesOutsideBlocks = outside;
            if (microsoft != null) MicrosoftLinesSha256 = Hashing.Sha256Hex(Latin1.Encode(microsoft.ToString()));
        }

        /// <summary>The names an active hosts line maps (any IP), leniently normalized. Empty for comments and blank lines.</summary>
        internal static IEnumerable<string> MappedNames(string text)
        {
            int hash = text.IndexOf('#');
            string active = hash >= 0 ? text.Substring(0, hash) : text;
            string[] tokens = active.Split(Blanks, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || !IPAddress.TryParse(tokens[0], out _)) yield break;
            for (int i = 1; i < tokens.Length; i++)
            {
                string? name = Guard.LenientName(tokens[i]);
                if (name != null) yield return name;
            }
        }

        /// <summary>Our exact generated shapes: the Managed-by line, the header and "0.0.0.0 host" with one space.</summary>
        internal static bool IsGeneratedShape(string text) =>
            text.StartsWith(ManagedByShapePrefix, StringComparison.Ordinal) ||
            text.StartsWith(HeaderPrefix, StringComparison.Ordinal) ||
            TryEntry(text, out _);

        internal static bool TryEntry(string text, out string host)
        {
            host = string.Empty;
            if (!text.StartsWith(EntryPrefix, StringComparison.Ordinal)) return false;
            string h = text.Substring(EntryPrefix.Length);
            if (!HostName.IsNormalized(h)) return false;
            host = h;
            return true;
        }

        /// <summary>Inside our block only: an entry with a tab for the space or trailing whitespace or CRs (see LooseEntry).</summary>
        internal static bool IsLooseEntry(string text) => TryLooseEntry(text, out _);

        internal static bool TryLooseEntry(string text, out string host)
        {
            host = string.Empty;
            Match m = LooseEntry.Match(text);
            return m.Success && HostName.TryNormalize(m.Groups[1].Value, out host);
        }

        internal static bool TryHandDisabled(string text, out string host)
        {
            host = string.Empty;
            Match m = HandDisabled.Match(text);
            return m.Success && HostName.TryNormalize(m.Groups[1].Value, out host);
        }
    }
}
