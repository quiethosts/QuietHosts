using System;
using System.Collections.Generic;
using System.Text;

namespace QuietHosts.Core
{
    public sealed partial class HostsDocument
    {
        /// <summary>
        /// The file's bytes with QuietHosts' block set to <paramref name="content"/>: the block
        /// is replaced in place, or takes the block-ads.ps1 block's place, or is appended after one blank
        /// line. The legacy block is removed, markers are repaired, hand-disabled lines are dropped (the
        /// caller moves their hosts into the allowlist) and other lines from inside the blocks move,
        /// unchanged, to just below the block. Every other line keeps its exact bytes.
        /// </summary>
        public byte[] Apply(BlockContent content, Guard guard)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            ThrowIfRefused();
            IReadOnlyList<string> block = content.RenderLines(guard);
            var output = new List<Line>(_lines.Count + block.Count + 2);
            var emitted = new List<int>(_lines.Count);
            bool placed = false;

            for (int i = 0; i < _lines.Count; i++)
            {
                if (i == _begin || (i == _legacyBegin && _begin < 0))
                {
                    int last = i == _begin ? _end : _legacyEnd;
                    AddBlock(output, emitted, block);
                    AddForeign(output, emitted, i, last);
                    placed = true;
                    i = last;
                }
                else if (i == _legacyBegin)
                {
                    DropBlankBefore(output, emitted, i);
                    AddForeign(output, emitted, i, _legacyEnd);
                    i = _legacyEnd;
                }
                else if (_roles[i] == Role.Outside)
                {
                    output.Add(_lines[i]);
                    emitted.Add(i);
                }
            }

            if (!placed)
            {
                if (output.Count > 0)
                {
                    Line lastLine = output[output.Count - 1];
                    if (lastLine.Eol.Length == 0) output[output.Count - 1] = new Line(lastLine.Text, LineEnding);
                    output.Add(new Line(string.Empty, LineEnding));
                    emitted.Add(-1);
                }
                AddBlock(output, emitted, block);
            }
            return Render(output);
        }

        /// <summary>
        /// The file's bytes without QuietHosts' block and the block-ads.ps1 block (each with the one blank
        /// line directly before it), with markers repaired. Lines from inside the blocks that QuietHosts
        /// didn't generate stay, unchanged, where the block was.
        /// </summary>
        public byte[] Remove()
        {
            ThrowIfRefused();
            var output = new List<Line>(_lines.Count);
            var emitted = new List<int>(_lines.Count);
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i == _begin || i == _legacyBegin)
                {
                    int last = i == _begin ? _end : _legacyEnd;
                    DropBlankBefore(output, emitted, i);
                    AddForeign(output, emitted, i, last);
                    i = last;
                }
                else if (_roles[i] == Role.Outside)
                {
                    output.Add(_lines[i]);
                    emitted.Add(i);
                }
            }
            return Render(output);
        }

        /// <summary>True if a write would change nothing: Apply(content) returns the same bytes.</summary>
        public bool IsCurrent(BlockContent content, Guard guard) => !IsRefused && Hashing.BytesEqual(Apply(content, guard), _bytes);

        private void AddBlock(List<Line> output, List<int> emitted, IReadOnlyList<string> block)
        {
            foreach (string line in block)
            {
                output.Add(new Line(line, LineEnding));
                emitted.Add(-1);
            }
        }

        private void AddForeign(List<Line> output, List<int> emitted, int first, int last)
        {
            for (int j = first + 1; j < last; j++)
            {
                if (_roles[j] != Role.Foreign) continue;
                output.Add(_lines[j]);
                emitted.Add(j);
            }
        }

        // "Removing a block also removes the one blank line directly before it."
        private void DropBlankBefore(List<Line> output, List<int> emitted, int blockStart)
        {
            int n = output.Count;
            if (n == 0 || blockStart == 0) return;
            if (emitted[n - 1] != blockStart - 1) return;
            if (output[n - 1].Text.Length != 0) return;
            output.RemoveAt(n - 1);
            emitted.RemoveAt(n - 1);
        }

        private byte[] Render(List<Line> output)
        {
            var sb = new StringBuilder(_bytes.Length + 64);
            sb.Append(_prefix);
            foreach (Line line in output)
            {
                sb.Append(line.Text);
                sb.Append(line.Eol);
            }
            return Latin1.Encode(sb.ToString());
        }

        private void ThrowIfRefused()
        {
            if (IsRefused) throw new InvalidOperationException("The hosts file can't safely be changed: " + Refusal + ".");
        }
    }
}
