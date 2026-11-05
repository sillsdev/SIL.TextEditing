using System;
using System.Collections.Generic;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Constructs of Pandoc Markdown that the notation does not have and Markdig reads as plain text: math, citations, inline notes, strikeout, and every line that starts
    /// like a list, a definition, a line block, a grid table or a div fence. Pandoc reads each of them as something else, so a hand-written text that holds one would mean
    /// one thing to us and another to Pandoc (spec 3 and 7): it is a finding, and the escape the writer would have written is the fix. The writer escapes all of them, so
    /// canonical text never holds one.</summary>
    internal static class PandocConstructs
    {
        public delegate void Report(int start, int end, string message, string fix);

        /// <summary>Looks at the paragraph text in <paramref name="text"/> [<paramref name="start"/>, <paramref name="end"/>); offsets in the reports are those of <paramref name="text"/>.</summary>
        public static void Find(string text, int start, int end, bool lines, Report report)
        {
            if (lines) Lines(text, start, end, report);
            Inline(text, start, end, report);
        }

        static void Lines(string text, int start, int end, Report report)
        {
            int pos = start;
            while (pos < end)
            {
                int lineEnd = text.IndexOf('\n', pos);
                if (lineEnd < 0 || lineEnd > end) lineEnd = end;
                int from = pos;
                while (from < lineEnd && (text[from] == ' ' || text[from] == '\t')) from++;
                string line = text.Substring(from, lineEnd - from).TrimEnd('\r');
                int at = LineStart.EscapeAt(line);
                if (at >= 0)
                    report(from, from + line.Length,
                        "Pandoc reads a line that starts like this as a list, a definition, a line block, a table or a div, which FieldWorks text has none of. Escape the first marker.",
                        line.Substring(0, at) + "\\" + line.Substring(at));
                pos = lineEnd + 1;
            }
        }

        static void Inline(string text, int start, int end, Report report)
        {
            var masked = Mask(text, start, end);
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                if (c == '\\') { i++; continue; }
                if (masked[i - start]) continue;
                if (c == '$' && i + 1 < end && !IsSpace(text[i + 1]))
                {
                    int close = FindMathClose(text, i + 1, end);
                    if (close > 0)
                    {
                        report(i, close + 1, "Pandoc reads this as math. Escape the dollar signs.", Escape(text, i, close + 1, '$'));
                        i = close;
                    }
                }
                else if (c == '@' && i + 1 < end && (char.IsLetterOrDigit(text[i + 1]) || text[i + 1] == '_' || text[i + 1] == '{') && (i == start || !char.IsLetterOrDigit(text[i - 1])))
                    report(i, i + 1, "Pandoc reads this as a citation. Escape the at sign.", "\\@");
                else if (c == '^' && i + 1 < end && text[i + 1] == '[')
                    report(i, i + 2, "Pandoc reads this as an inline note. Escape the caret.", "\\^[");
                else if (c == '~' && i + 2 < end && text[i + 1] == '~' && !IsSpace(text[i + 2]))
                {
                    int close = text.IndexOf("~~", i + 2, end - i - 2, StringComparison.Ordinal);
                    if (close > 0 && !IsSpace(text[close - 1]))
                    {
                        report(i, close + 2, "Pandoc reads this as strikeout. Escape the tildes.", Escape(text, i, close + 2, '~'));
                        i = close + 1;
                    }
                }
            }
        }

        static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r';

        /// <summary>The offset of the <c>$</c> that closes math opened before <paramref name="from"/>: after a character that is not a space, and not followed by a digit.</summary>
        static int FindMathClose(string text, int from, int end)
        {
            for (int j = from; j < end; j++)
            {
                if (text[j] == '\\') { j++; continue; }
                if (text[j] == '$' && !IsSpace(text[j - 1]) && !(j + 1 < end && char.IsDigit(text[j + 1]))) return j;
            }
            return -1;
        }

        static string Escape(string text, int from, int to, char mark)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = from; i < to; i++)
            {
                if (text[i] == '\\' && i + 1 < to) { sb.Append(text[i]).Append(text[i + 1]); i++; continue; }
                if (text[i] == mark) sb.Append('\\');
                sb.Append(text[i]);
            }
            return sb.ToString();
        }

        /// <summary>The attribute blocks and destinations of the notation: Pandoc does not read a citation or a note inside them, and neither do we.</summary>
        static bool[] Mask(string text, int start, int end)
        {
            var mask = new bool[end - start];
            for (int i = start + 1; i < end; i++)
            {
                if (text[i] == '{' && (text[i - 1] == ']' || text[i - 1] == ')'))
                {
                    if (AttributeScanner.TryScan(text, i, end, out var block, out _, out _) == ScanStatus.Ok)
                    {
                        for (int k = i; k < block!.End && k < end; k++) mask[k - start] = true;
                        i = block.End - 1;
                    }
                }
                else if (text[i] == '(' && text[i - 1] == ']')
                {
                    int depth = 0, k = i;
                    for (; k < end; k++)
                    {
                        if (text[k] == '\\') { k++; continue; }
                        if (text[k] == '(') depth++;
                        else if (text[k] == ')' && --depth == 0) break;
                    }
                    for (int m = i; m <= k && m < end; m++) mask[m - start] = true;
                    i = k;
                }
            }
            return mask;
        }

        /// <summary>Pandoc's heading attributes: <c>## Title {#id .class key=value}</c>. Returns the offset of the <c>{</c> when the line ends in such a group.</summary>
        public static int HeadingAttributes(string line)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0 || trimmed[trimmed.Length - 1] != '}') return -1;
            int open = trimmed.LastIndexOf('{');
            if (open < 0) return -1;
            int slashes = 0;
            for (int b = open - 1; b >= 0 && trimmed[b] == '\\'; b--) slashes++;
            if (slashes % 2 == 1) return -1;
            if (open > 0 && (trimmed[open - 1] == ']' || trimmed[open - 1] == ')')) return -1;
            string inner = trimmed.Substring(open + 1, trimmed.Length - open - 2).Trim();
            if (inner.Length == 0) return -1;
            foreach (string token in inner.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool idOrClass = (token[0] == '#' || token[0] == '.') && token.Length > 1;
                bool pair = token.IndexOf('=') > 0;
                if (!idOrClass && !pair) return -1;
            }
            return open;
        }
    }
}
