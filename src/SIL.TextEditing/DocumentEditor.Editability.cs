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

        /// <summary>True when every character in [a, b) is in an editable run (a collapsed range asks about the point), and
        /// the text on both sides of every paragraph mark in the range may be edited at the mark, because a paragraph mark
        /// belongs to both of its neighbours.</summary>
        public bool CanEditRange(TextPosition a, TextPosition b)
        {
            if (a.CompareTo(b) > 0) { var t = a; a = b; b = t; }
            if (a.Equals(b)) return IsEditableAt(a);
            for (int pi = a.Paragraph; pi < b.Paragraph; pi++)
                if (!IsEditableAt(new TextPosition(pi, Document.Paragraphs[pi].Length)) || !IsEditableAt(new TextPosition(pi + 1, 0)))
                    return false;
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

        /// <summary>The editable places of the document, in order: each is a stretch of insertion points [Start, End] in one
        /// paragraph that can take typing, with no read-only text between them. A paragraph with no read-only text has one
        /// place; a field-level read-only document has none.</summary>
        List<(TextPosition Start, TextPosition End)> EditablePlaces()
        {
            var places = new List<(TextPosition, TextPosition)>();
            if (IsReadOnly) return places;
            for (int pi = 0; pi < Document.Paragraphs.Count; pi++)
            {
                int len = Document.Paragraphs[pi].Length;
                int start = -1;
                for (int o = 0; o <= len + 1; o++)
                {
                    bool ok = o <= len && IsEditableAt(new TextPosition(pi, o));
                    if (ok && start < 0) start = o;
                    else if (!ok && start >= 0)
                    {
                        places.Add((new TextPosition(pi, start), new TextPosition(pi, o - 1)));
                        start = -1;
                    }
                }
            }
            return places;
        }

        /// <summary>Finds where Tab (<see cref="EditablePlace.Next"/>), Shift+Tab (<see cref="EditablePlace.Previous"/>),
        /// Ctrl+Tab (<see cref="EditablePlace.Last"/>) or Ctrl+Shift+Tab (<see cref="EditablePlace.First"/>) goes, as FieldWorks
        /// moves among the editable places of a view: the start of the next place, the end of the previous one, the end of
        /// the last or the start of the first. False when there is no such place or the caret is already there; the host then
        /// moves focus out of the field.</summary>
        public bool TryGetEditablePlace(EditablePlace which, out TextPosition position)
        {
            position = Caret;
            var places = EditablePlaces();
            if (places.Count == 0) return false;
            TextPosition target;
            switch (which)
            {
                case EditablePlace.First: target = places[0].Start; break;
                case EditablePlace.Last: target = places[places.Count - 1].End; break;
                case EditablePlace.Next:
                {
                    int i = places.FindIndex(pl => pl.Start.CompareTo(Caret) > 0);
                    if (i < 0) return false;
                    target = places[i].Start;
                    break;
                }
                default:
                {
                    int i = places.FindLastIndex(pl => pl.End.CompareTo(Caret) < 0);
                    if (i < 0) return false;
                    target = places[i].End;
                    break;
                }
            }
            if (target.Equals(Caret) && !HasSelection) return false;
            position = target;
            return true;
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

    /// <summary>Where Tab and its variants go among the editable places of a field.</summary>
    public enum EditablePlace { Next, Previous, First, Last }
}
