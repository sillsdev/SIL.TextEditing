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

        /// <summary>True for a character a separator swallows when it sits beside one: any white space except the hard line
        /// break, which is content.</summary>
        static bool IsGap(char c) => c != LineSeparator && char.IsWhiteSpace(c);

        /// <summary>The character just before the selection (or the caret), or '\0' at the start of a paragraph: what a pasted
        /// text that starts with a separator meets.</summary>
        char CharBeforeSelection()
        {
            Ordered(out var a, out _);
            return a.Offset > 0 ? Document.Paragraphs[a.Paragraph].Text[a.Offset - 1] : '\0';
        }

        /// <summary>Whether a separator at the start of the inserted text leaves a space: when it follows text that does not end
        /// in white space, so the words do not fuse.</summary>
        static bool NeedsSpaceAfter(char before) => before != '\0' && !char.IsWhiteSpace(before);

        /// <summary>Text with every stretch of white space that holds a separator turned into one space, none at the end, and one at
        /// the start only when <paramref name="before"/>, the character it will follow, is not white space (or absent).</summary>
        internal static string OneLine(string text, char before = '\0')
        {
            var sb = new System.Text.StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (!IsGap(text[i])) { sb.Append(text[i++]); continue; }
                int j = i;
                bool separator = false;
                while (j < text.Length && IsGap(text[j])) { if (IsParagraphSeparator(text[j])) separator = true; j++; }
                if (!separator) sb.Append(text, i, j - i);
                else if (j < text.Length && (i > 0 || NeedsSpaceAfter(before))) sb.Append(' ');
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>The fragment as one paragraph: the first paragraph's style and properties and the runs of every paragraph in
        /// order, a separator between neighbours that <see cref="CollapseSeparators"/> then turns into one space (in the writing
        /// system of the text before it). Empty paragraphs are separators too, so they add nothing between texts and at the end,
        /// and one space at the start.</summary>
        static List<Paragraph> JoinIntoOne(List<Paragraph> fragment)
        {
            var joined = fragment[0].Clone(false);
            joined.Runs = new List<Run>();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0) joined.Runs.Add(new Run { Text = "\n", Ws = joined.Runs.Count > 0 ? joined.Runs[joined.Runs.Count - 1].Ws : fragment[i].DefaultWs });
                joined.Runs.AddRange(fragment[i].Runs.Select(r => r.Clone()));
            }
            return new List<Paragraph> { joined };
        }

        /// <summary>Turns every stretch of white space that holds a separator, across run boundaries, into one space (a separator
        /// swallows the white space beside it), and drops it at the end; at the start it leaves one space only when
        /// <paramref name="before"/>, the character before the insertion point, is not white space (or absent), so a pasted
        /// "foo, newline, bar, newline" gives "foo bar" and a leading newline after "X" gives "X ". The hard line break and object
        /// runs count as content. The space takes the writing system of the text before it, else of the text after.</summary>
        static void CollapseSeparators(Paragraph paragraph, char before = '\0')
        {
            if (!paragraph.Runs.Any(r => !r.IsObject && r.Text.Any(IsParagraphSeparator))) return;
            var chars = new List<char>();
            var owners = new List<Run>();
            foreach (var r in paragraph.Runs)
                foreach (var c in r.Text) { chars.Add(c); owners.Add(r); }
            var runs = new List<Run>();
            Run? lastOwner = null;   // the source run the last output run copies, null for a synthesised space
            void Emit(char c, Run owner, bool plain)
            {
                if (!plain && runs.Count > 0 && ReferenceEquals(lastOwner, owner)) { runs[runs.Count - 1].Text += c; return; }
                var run = plain ? new Run { Text = c.ToString(), Ws = owner.Ws } : owner.CloneWithText(c.ToString());
                runs.Add(run);
                lastOwner = plain ? null : owner;
            }
            int i = 0, n = chars.Count;
            while (i < n)
            {
                if (!IsGap(chars[i])) { Emit(chars[i], owners[i], false); i++; continue; }
                int j = i;
                bool separator = false;
                while (j < n && IsGap(chars[j])) { if (IsParagraphSeparator(chars[j])) separator = true; j++; }
                if (!separator) for (int k = i; k < j; k++) Emit(chars[k], owners[k], false);
                else if (j < n && (i > 0 || NeedsSpaceAfter(before))) Emit(' ', i > 0 ? owners[i - 1] : owners[j], true);
                i = j;
            }
            paragraph.Runs = runs;
            paragraph.Normalize();
        }
    }
}
