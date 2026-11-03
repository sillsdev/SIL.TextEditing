using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    // Which text may be edited (ADR 0019): the field-wide read-only flag and the per-run editable property.
    public sealed partial class DocumentEditor
    {
        /// <summary>When true nothing the user does changes the text: typing, deletion, paste, cut, Enter and input
        /// method commits are ignored. Selection, caret movement and copy still work. Host-driven changes
        /// (<see cref="Apply"/>, <see cref="ExternalReplaceParagraph"/>) are not blocked.</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>True when an insertion point at <paramref name="p"/> can take typing: the paragraph is empty, or the
        /// run on either side is editable (FieldWorks asks for both sides of the point).</summary>
        public bool IsEditableAt(TextPosition p)
        {
            var para = Document.Paragraphs[p.Paragraph];
            if (para.Length == 0) return true;
            var before = para.RunBefore(p.Offset, out _);
            var after = para.RunAfter(p.Offset, out _);
            return (before != null && before.Editable == RunEditable.Editable) || (after != null && after.Editable == RunEditable.Editable);
        }

        /// <summary>True when every character in [a, b) is in an editable run (a collapsed range asks about the point).</summary>
        public bool CanEditRange(TextPosition a, TextPosition b)
        {
            if (a.CompareTo(b) > 0) { var t = a; a = b; b = t; }
            if (a.Equals(b)) return IsEditableAt(a);
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var p = Document.Paragraphs[pi];
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : p.Length;
                int pos = 0;
                foreach (var r in p.Runs)
                {
                    int rs = pos, re = pos + r.Text.Length;
                    pos = re;
                    if (re > s && rs < e && r.Editable != RunEditable.Editable) return false;
                }
            }
            return true;
        }

        /// <summary>True when typing, Enter or paste would change the text now: the field is not read-only and the selection
        /// (or, with none, the caret) lies in editable text.</summary>
        public bool CanEdit => !IsReadOnly && CanEditSelectionOrCaret();

        /// <summary>True when the selection can be formatted: it is not read-only and every run in it is fully editable.
        /// FieldWorks refuses formatting in not-editable and semi-editable text alike.</summary>
        public bool CanFormatSelection => HasSelection && CanEdit;

        bool CanEditSelectionOrCaret()
        {
            if (IsReadOnly) return false;
            if (!HasSelection) return IsEditableAt(Caret);
            Ordered(out var a, out var b);
            return CanEditRange(a, b);
        }

        /// <summary>The closest insertion point to <paramref name="p"/>, in its paragraph, where editing is allowed: p itself
        /// when it already is. A direction (+1 forward, -1 backward) searches that way first, so an arrow key steps over
        /// read-only text instead of bouncing back; with none, the nearer side wins and backward wins a tie. When the
        /// paragraph has no such place, p is kept.</summary>
        public TextPosition NearestEditable(TextPosition p, int direction = 0)
        {
            if (IsEditableAt(p)) return p;
            int len = Document.Paragraphs[p.Paragraph].Length;
            int Find(int from, int step)
            {
                for (int o = from; o >= 0 && o <= len; o += step)
                    if (IsEditableAt(new TextPosition(p.Paragraph, o))) return o;
                return -1;
            }
            int fwd = Find(p.Offset + 1, 1), back = Find(p.Offset - 1, -1);
            int pick;
            if (fwd < 0) pick = back;
            else if (back < 0) pick = fwd;
            else if (direction > 0) pick = fwd;
            else if (direction < 0) pick = back;
            else pick = p.Offset - back <= fwd - p.Offset ? back : fwd;
            return pick < 0 ? p : new TextPosition(p.Paragraph, pick);
        }
    }
}
