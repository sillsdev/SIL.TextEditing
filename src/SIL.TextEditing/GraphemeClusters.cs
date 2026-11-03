using System;
using System.Collections.Generic;
using System.Globalization;

namespace SIL.TextEditing
{
    /// <summary>
    /// Extended grapheme-cluster boundaries (UAX #29) over UTF-16 text. On net10.0 this is
    /// <see cref="StringInfo"/> plus Indic conjuncts (GB9c), which .NET 10 does not implement. On netstandard2.0
    /// the runtime may be .NET Framework, whose StringInfo predates the rules, so its clusters are also fused
    /// wherever UAX #29 forbids a break (see <c>ClusterFuser</c>): Extend and ZWJ (GB9), SpacingMark (GB9a),
    /// emoji ZWJ sequences (GB11) and regional-indicator pairs (GB12, GB13). Both targets give the same answers
    /// on the pinned tests.
    /// </summary>
    public static class GraphemeClusters
    {
        /// <summary>UTF-16 offsets at which a cluster starts, ascending; empty for empty text.</summary>
        public static IReadOnlyList<int> Starts(string text)
        {
            var starts = new List<int>();
            if (string.IsNullOrEmpty(text)) return starts;
            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext()) starts.Add(e.ElementIndex);
ClusterFuser.Fuse(text, starts);
            return starts;
        }

        /// <summary>The first cluster boundary after <paramref name="index"/>, or the text length.</summary>
        public static int Next(string text, int index)
        {
            if (index >= text.Length) return text.Length;
            foreach (var s in Starts(text))
                if (s > index) return s;
            return text.Length;
        }

        /// <summary>The last cluster boundary before <paramref name="index"/>, or 0.</summary>
        public static int Previous(string text, int index)
        {
            if (index <= 0) return 0;
            var starts = Starts(text);
            for (int k = starts.Count - 1; k >= 0; k--)
                if (starts[k] < index) return starts[k];
            return 0;
        }

        /// <summary>
        /// Where Backspace deletes back to: one code point, never half a surrogate pair, so a combining mark goes
        /// before its base. (Delete removes a whole cluster; see <see cref="Next"/>.)
        /// </summary>
        public static int BackspaceTarget(string text, int index)
        {
            if (index <= 0) return 0;
            int j = index - 1;
            if (j > 0 && char.IsLowSurrogate(text[j]) && char.IsHighSurrogate(text[j - 1])) j--;
            return j;
        }
    }
}
