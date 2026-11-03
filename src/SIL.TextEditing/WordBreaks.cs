using System;
using System.Collections.Generic;
using System.Globalization;

namespace SIL.TextEditing
{
    /// <summary>
    /// A deliberately simple word rule for double-click and Ctrl+Left/Right, built on <see cref="GraphemeClusters"/>.
    /// Every grapheme cluster has a class taken from its first code point: Word (letter, digit, mark, connector
    /// punctuation such as underscore), Space (white space), Object (U+FFFC, an inline object) or Other (any other
    /// punctuation or symbol). A word is a maximal run of Word clusters; an apostrophe (U+0027 or U+2019) with a Word
    /// cluster on both sides is part of the word. A run of Space or of Other clusters is its own "word" for double-click,
    /// and an Object is always alone. There is no dictionary, so Thai, Khmer and Chinese text without spaces is one
    /// word per run of letters; a host that needs better can replace the rule. This is not UAX #29 word breaking.
    /// </summary>
    public static class WordBreaks
    {
        enum Kind { Word, Space, Other, Object }

        static Kind KindOf(string text, int index)
        {
            char c = text[index];
            if (c == '￼') return Kind.Object;
            if (char.IsWhiteSpace(c)) return Kind.Space;
            switch (CharUnicodeInfo.GetUnicodeCategory(text, index))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.Format:
                    return Kind.Word;
                default:
                    return Kind.Other;
            }
        }

        /// <summary>Classes of the clusters of the text, with apostrophes between words folded into the word.</summary>
        static Kind[] Classify(string text, IReadOnlyList<int> starts)
        {
            var kinds = new Kind[starts.Count];
            for (int i = 0; i < starts.Count; i++) kinds[i] = KindOf(text, starts[i]);
            for (int i = 1; i + 1 < starts.Count; i++)
            {
                char c = text[starts[i]];
                bool apostrophe = (c == '\'' || c == '’') && starts[i + 1] - starts[i] == 1;
                if (apostrophe && kinds[i - 1] == Kind.Word && kinds[i + 1] == Kind.Word) kinds[i] = Kind.Word;
            }
            return kinds;
        }

        static int End(string text, IReadOnlyList<int> starts, int cluster) => cluster + 1 < starts.Count ? starts[cluster + 1] : text.Length;

        static int ClusterAt(IReadOnlyList<int> starts, int offset)
        {
            int k = 0;
            while (k + 1 < starts.Count && starts[k + 1] <= offset) k++;
            return k;
        }

        /// <summary>The run containing the character at <paramref name="charIndex"/> (clamped into the text): the word,
        /// white-space run or punctuation run a double-click selects. Empty text gives (0, 0).</summary>
        public static void WordAt(string text, int charIndex, out int start, out int end)
        {
            start = end = 0;
            if (string.IsNullOrEmpty(text)) return;
            var starts = GraphemeClusters.Starts(text);
            var kinds = Classify(text, starts);
            int k = ClusterAt(starts, Math.Max(0, Math.Min(text.Length - 1, charIndex)));
            int a = k, b = k;
            if (kinds[k] != Kind.Object)
            {
                while (a > 0 && kinds[a - 1] == kinds[k]) a--;
                while (b + 1 < starts.Count && kinds[b + 1] == kinds[k]) b++;
            }
            start = starts[a];
            end = End(text, starts, b);
        }

        /// <summary>Ctrl+Right: the start of the next word after <paramref name="index"/> (skipping the rest of the current
        /// word or punctuation run and then white space), or the text length.</summary>
        public static int NextWordStart(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index >= text.Length) return text?.Length ?? 0;
            var starts = GraphemeClusters.Starts(text);
            var kinds = Classify(text, starts);
            int k = ClusterAt(starts, Math.Max(0, index));
            var kind = kinds[k];
            if (kind != Kind.Space)
            {
                k++;
                if (kind != Kind.Object) while (k < starts.Count && kinds[k] == kind) k++;
            }
            while (k < starts.Count && kinds[k] == Kind.Space) k++;
            return k < starts.Count ? starts[k] : text.Length;
        }

        /// <summary>Ctrl+Left: the start of the word before <paramref name="index"/> (skipping white space first), or 0.</summary>
        public static int PreviousWordStart(string text, int index)
        {
            if (string.IsNullOrEmpty(text) || index <= 0) return 0;
            var starts = GraphemeClusters.Starts(text);
            var kinds = Classify(text, starts);
            int k = ClusterAt(starts, Math.Min(index, text.Length) - 1);
            while (k >= 0 && kinds[k] == Kind.Space) k--;
            if (k < 0) return 0;
            var kind = kinds[k];
            if (kind != Kind.Object) while (k > 0 && kinds[k - 1] == kind) k--;
            return starts[k];
        }
    }
}
