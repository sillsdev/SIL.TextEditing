using System;
using System.Linq;

namespace SIL.TextEditing
{
    // Context-menu information (ADR 0021): the editor describes a place; the field raises the event and the host builds the menu.
    public sealed partial class DocumentEditor
    {
        /// <summary>Finds the word at a character of a paragraph for <see cref="DescribeContext"/>: the paragraph, the index, and the word's range, or
        /// null when the character is not part of a word. Null (the default) uses <see cref="WordBreaks"/>, whose rule agrees with the spell checker's
        /// defaults. A field with spell checking sets it to the spell checker's rule, so the word of a context request is the squiggled word; it is the
        /// field's to set, so a host that sets <c>SpellChecking</c> or clears it does not have its own overwritten.</summary>
        internal Func<Paragraph, int, (int Start, int End)?>? WordFinder { get; set; }

        bool TryWordAt(Paragraph paragraph, string text, int index, out int start, out int end)
        {
            var finder = WordFinder;
            if (finder == null) return WordBreaks.TryWordAt(text, index, out start, out end);
            var found = finder(paragraph, index);
            start = found?.Start ?? Math.Max(0, Math.Min(text.Length, index));
            end = found?.End ?? start;
            return found != null;
        }

        /// <summary>Describes the place at <paramref name="position"/> for a context menu. With <paramref name="atCaret"/> the
        /// place is an insertion point (a keyboard request): the character after it counts, and when that is not part of a word or
        /// an object, the one before it. Otherwise it is the character at the position (the one under the pointer).</summary>
        public ContextInfo DescribeContext(TextPosition position, bool atCaret, bool onText = true)
        {
            int pi = Math.Max(0, Math.Min(Document.Paragraphs.Count - 1, position.Paragraph));
            var para = Document.Paragraphs[pi];
            var text = para.Text;
            int offset = Math.Max(0, Math.Min(text.Length, position.Offset));
            var info = new ContextInfo { Position = new TextPosition(pi, offset) };

            // The character the place means: at the offset, or for an insertion point the one before when none follows.
            int ch = !onText ? -1 : offset < text.Length ? offset : text.Length > 0 ? text.Length - 1 : -1;
            int word0 = 0, word1 = 0;
            bool onWord = ch >= 0 && TryWordAt(para, text, ch, out word0, out word1);
            if (!onWord && atCaret && offset > 0 && offset <= text.Length)
            {
                ch = offset - 1;
                onWord = TryWordAt(para, text, ch, out word0, out word1);
            }
            if (onWord)
            {
                info.Word = text.Substring(word0, word1 - word0);
                info.WordRange = new TextRange(pi, word0, word1);
            }
            else info.WordRange = new TextRange(pi, offset, offset);

            // The run: the one that holds the character the place means (the same one as the word starts in, when there is a word).
            int at = onWord ? word0 : ch;
            Run? run = null;
            int runStart = 0;
            if (at >= 0) run = para.RunAfter(at, out runStart);
            if (run != null)
            {
                info.Run = run.Clone();
                info.RunRange = new TextRange(pi, runStart, runStart + run.Text.Length);
                info.WritingSystem = run.Ws;
            }
            else info.WritingSystem = para.DefaultWs;

            // An embedded object: the one the place means, which for an insertion point is the one after it, else the one before.
            int obj = -1;
            if (onText && offset < text.Length && text[offset] == Run.ObjectReplacement) obj = offset;
            else if (atCaret && offset > 0 && text[offset - 1] == Run.ObjectReplacement) obj = offset - 1;
            if (obj >= 0)
            {
                var o = para.RunAfter(obj, out _);
                if (o != null && o.IsObject) info.EmbeddedObject = new EmbeddedObjectInfo(new TextRange(pi, obj, obj + 1), o.ObjData!);
            }

            Ordered(out var a, out var b);
            info.Selection = new TextRange(a, b);
            if (HasSelection) info.SelectedText = string.Join("\n", GetFragment().Select(p => p.Text));
            return info;
        }
    }
}
