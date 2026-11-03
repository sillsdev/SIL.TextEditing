using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    // Single-paragraph mode (ADR 0012): the field never makes a second paragraph.
    public sealed partial class DocumentEditor
    {
        /// <summary>When true the editor never splits a paragraph: <see cref="Enter"/> does nothing (the field hands the key to
        /// the host, as FieldWorks does when a view does not accept Return), a line break in typed text becomes a space, and
        /// pasted paragraphs are joined into one with a space between them. A document that already holds several
        /// paragraphs keeps them; only new splits are refused.</summary>
        public bool SingleParagraph { get; set; }

        /// <summary>True for the characters that separate paragraphs or lines of a paragraph list: CR, LF, NEL (U+0085) and the
        /// paragraph separator (U+2029). The line separator (U+2028) is not one: it is FieldWorks' hard line break inside a
        /// paragraph, so a single paragraph keeps it.</summary>
        internal static bool IsParagraphSeparator(char c) => c == (char)13 || c == (char)10 || c == (char)0x85 || c == (char)0x2029;

        /// <summary>Text with every run of paragraph separators turned into one space, and none at the start or the end.</summary>
        internal static string OneLine(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool pending = false;
            foreach (var c in text)
            {
                if (IsParagraphSeparator(c)) { pending = true; continue; }
                if (pending && sb.Length > 0) sb.Append(' ');
                pending = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>The fragment as one paragraph: the first paragraph's style and properties, the runs of every non-empty
        /// paragraph in order, and one plain space (in the writing system of the text before it) between paragraphs unless
        /// whitespace is already there. Empty paragraphs add nothing, so a trailing newline leaves no trailing space. A
        /// separator inside a run becomes a space.</summary>
        static List<Paragraph> JoinIntoOne(List<Paragraph> fragment)
        {
            var joined = fragment[0].Clone(false);
            joined.Runs = new List<Run>();
            string tail = string.Empty;   // the text so far, to see whether it ends in whitespace
            for (int i = 0; i < fragment.Count; i++)
            {
                if (fragment[i].Length == 0) continue;
                var text = fragment[i].Text;
                if (joined.Runs.Count > 0 && !char.IsWhiteSpace(tail[tail.Length - 1]) && !char.IsWhiteSpace(text[0]))
                    joined.Runs.Add(new Run { Text = " ", Ws = joined.Runs[joined.Runs.Count - 1].Ws });
                joined.Runs.AddRange(fragment[i].Runs.Select(r => r.Clone()));
                tail = text;
            }
            joined.Normalize();
            return new List<Paragraph> { joined };
        }

        /// <summary>Turns every run of separators inside the paragraph's runs, across run boundaries, into one space, and drops
        /// those at the start and the end, so a pasted run "foo, newline, bar, newline" gives "foo bar". Object runs count as content.</summary>
        static void SeparatorsToSpaces(Paragraph paragraph)
        {
            if (!paragraph.Runs.Any(r => !r.IsObject && r.Text.Any(IsParagraphSeparator))) return;
            var runs = new List<Run>();
            bool started = false, pending = false;   // started: some content so far; pending: a separator since the last content
            foreach (var r in paragraph.Runs)
            {
                if (r.IsObject)
                {
                    if (pending) runs.Add(new Run { Text = " ", Ws = runs.Count > 0 ? runs[runs.Count - 1].Ws : r.Ws });
                    pending = false;
                    started = true;
                    runs.Add(r);
                    continue;
                }
                var sb = new System.Text.StringBuilder(r.Text.Length);
                foreach (var c in r.Text)
                {
                    if (IsParagraphSeparator(c)) { if (started) pending = true; continue; }
                    if (pending) sb.Append(' ');
                    pending = false;
                    started = true;
                    sb.Append(c);
                }
                if (sb.Length == 0) continue;
                r.Text = sb.ToString();
                runs.Add(r);
            }
            paragraph.Runs = runs;
            paragraph.Normalize();
        }
    }
}
