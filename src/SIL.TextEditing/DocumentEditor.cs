using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>
    /// A UI-free editing session over a <see cref="Document"/>. Every mutation is reported as an
    /// <see cref="EditDelta"/> through <see cref="Committed"/>; the editor keeps no undo stack, because the host
    /// owns it (see <see cref="SimpleUndoStack"/> for a stand-in). Paragraph identity follows the rules in
    /// <see cref="ReplaceRange"/> and <see cref="Enter"/>.
    /// </summary>
    public sealed partial class DocumentEditor
    {
        bool _wsOverride;

        public DocumentEditor(Document document)
        {
            Document = document;
            ActiveWs = WsAt(Caret);
        }

        public Document Document { get; }
        public TextPosition Caret { get; set; }
        public TextPosition Anchor { get; set; }
        /// <summary>The writing system new text gets: the run at the caret, unless the host or user chose one.</summary>
        public string ActiveWs { get; private set; }

        /// <summary>Raised after each edit with the reversible change.</summary>
        public event Action<EditDelta>? Committed;
        /// <summary>Raised when the active writing system changes: old, new.</summary>
        public event Action<string, string>? ActiveWsChanged;
        public event Action? CaretMoved;
        /// <summary>Raised on any change to the document: start paragraph, removed paragraphs, added paragraphs.</summary>
        public event Action<int, int, int>? DocumentChanged;

        public bool HasSelection => !Caret.Equals(Anchor);

        public void Ordered(out TextPosition start, out TextPosition end)
        {
            if (Anchor.CompareTo(Caret) <= 0) { start = Anchor; end = Caret; }
            else { start = Caret; end = Anchor; }
        }

        public string WsAtCaret() => WsAt(Caret);

        public string WsAt(TextPosition p)
        {
            var para = Document.Paragraphs[p.Paragraph];
            var before = para.RunBefore(p.Offset, out _);
            if (before != null) return before.Ws;
            var after = para.RunAfter(p.Offset, out _);
            if (after != null) return after.Ws;
            return para.DefaultWs;
        }

        /// <summary>Writing systems touched by the selection, or the single caret writing system.</summary>
        public List<string> SelectionWsSet()
        {
            var set = new List<string>();
            if (!HasSelection)
            {
                set.Add(ActiveWs);
                return set;
            }
            Ordered(out var a, out var b);
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
                    if (re > s && rs < e && !set.Contains(r.Ws)) set.Add(r.Ws);
                }
            }
            if (set.Count == 0) set.Add(ActiveWs);
            return set;
        }

        public void SetCaret(TextPosition p, bool extend = false, int direction = 0)
        {
            p.Paragraph = Math.Max(0, Math.Min(Document.Paragraphs.Count - 1, p.Paragraph));
            p.Offset = Math.Max(0, Math.Min(Document.Paragraphs[p.Paragraph].Length, p.Offset));
            if (!extend && !IsReadOnly) p = NearestEditable(p, direction);
            if (!p.Equals(Caret) || (!extend && !p.Equals(Anchor))) CloseUndoGroup();   // a caret move ends a typing step (ADR 0023)
            Caret = p;
            if (!extend) Anchor = p;
            _wsOverride = false;
            RefreshWs();
            CaretMoved?.Invoke();
        }

        void RefreshWs()
        {
            var n = _wsOverride ? ActiveWs : WsAt(Caret);
            if (n == ActiveWs) return;
            var o = ActiveWs;
            ActiveWs = n;
            ActiveWsChanged?.Invoke(o, n);
        }

        /// <summary>The host or user picks a writing system (a keyboard switch, say): typing uses it even when the
        /// preceding run differs.</summary>
        public void SetActiveWs(string ws)
        {
            _wsOverride = true;
            var o = ActiveWs;
            ActiveWs = ws;
            if (o != ws) ActiveWsChanged?.Invoke(o, ws);
        }

        PropertyBag? InheritProps(TextPosition p, string ws)
        {
            var para = Document.Paragraphs[p.Paragraph];
            var b = para.RunBefore(p.Offset, out _);
            if (b != null && !b.IsObject && b.Ws == ws && AllowsEditing(b)) return b.Props;
            var a = para.RunAfter(p.Offset, out _);
            if (a != null && !a.IsObject && a.Ws == ws && AllowsEditing(a)) return a.Props;
            return null;
        }

        EditDelta Begin(int paraStart, int paraEnd, string label)
        {
            var d = new EditDelta { Start = paraStart, CaretBefore = Caret, Label = label };
            for (int i = paraStart; i <= paraEnd; i++) d.Before.Add(Document.Paragraphs[i].Clone());
            return d;
        }

        void Finish(EditDelta d, TextPosition caret)
        {
            AssignUndoGroup(d);
            Caret = Anchor = caret;
            d.CaretAfter = caret;
            _wsOverride = false;
            RefreshWs();
            Committed?.Invoke(d);
            DocumentChanged?.Invoke(d.Start, d.Before.Count, d.After.Count);
            CaretMoved?.Invoke();
            EndSingleEditGroup();
        }

        // ---- editing ----

        string Normalized(string text) => Document.Normalization is NormalizationForm form ? TextNormalizer.Normalize(text, form) : text;

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = Normalized(text);
            if (SingleParagraph) text = OneLine(text);
            else text = text.Replace("\r\n", "\n");
            if (text.Length == 0) return;
            var parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) Enter();
                if (parts[i].Length > 0) InsertOne(parts[i]);
            }
        }

        /// <summary>Inserts text a user pasted (the clipboard had plain text only): the same result as <see cref="TypeText"/>, but
        /// the whole insertion, line breaks and the replaced selection included, is one undo step of its own that neither
        /// joins the typing before it nor the typing after it.</summary>
        public void PasteText(string text)
        {
            if (string.IsNullOrEmpty(text) || !CanEditSelectionOrCaret()) return;
            InOneUndoStep(() => TypeText(text));
        }

        /// <summary>The hard line break FieldWorks inserts for Shift+Enter: U+2028, a line break inside the paragraph.</summary>
        public const char LineSeparator = (char)0x2028;

        /// <summary>Shift+Enter: inserts a <see cref="LineSeparator"/> at the caret (over the selection) without splitting the
        /// paragraph, in single-paragraph mode too. It is one undo step of its own.</summary>
        public void InsertLineBreak()
        {
            if (!CanEditSelectionOrCaret()) return;
            InOneUndoStep(() => InsertOne(LineSeparator.ToString()));
        }

        void InsertOne(string text)
        {
            if (!CanEditSelectionOrCaret()) return;
            // Typing over a selection is one step with the deletion; it never joins an earlier step.
            if (HasSelection && !_batch) CloseUndoGroup();
            _typingWs = ActiveWs;
            _typing = true;
            try
            {
                if (HasSelection) DeleteSelection("type");
                var ws = ActiveWs;
                var d = Begin(Caret.Paragraph, Caret.Paragraph, "type");
                var para = Document.Paragraphs[Caret.Paragraph];
                para.Insert(Caret.Offset, text, ws, InheritProps(Caret, ws));
                d.After.Add(para.Clone());
                bool keepOverride = _wsOverride;
                Finish(d, new TextPosition(Caret.Paragraph, Caret.Offset + text.Length));
                if (keepOverride)
                {
                    _wsOverride = true;
                    ActiveWs = ws;
                }
            }
            finally { _typing = false; }
        }

        public void DeleteSelection(string label = "delete")
        {
            if (!HasSelection) return;
            Ordered(out var a, out var b);
            ReplaceRange(a, b, null, label);
        }

        public void Backspace()
        {
            if (HasSelection) { DeleteSelection("backspace"); return; }
            if (Caret.Offset == 0)
            {
                if (Caret.Paragraph > 0)
                    ReplaceRange(new TextPosition(Caret.Paragraph - 1, Document.Paragraphs[Caret.Paragraph - 1].Length), Caret, null, "join");
                return;
            }
            var t = Document.Paragraphs[Caret.Paragraph].Text;
            int to = GraphemeClusters.BackspaceTarget(t, Caret.Offset);
            ReplaceRange(new TextPosition(Caret.Paragraph, to), Caret, null, "backspace");
        }

        public void DeleteForward()
        {
            if (HasSelection) { DeleteSelection("delete"); return; }
            var p = Document.Paragraphs[Caret.Paragraph];
            if (Caret.Offset >= p.Length)
            {
                if (Caret.Paragraph + 1 < Document.Paragraphs.Count)
                    ReplaceRange(Caret, new TextPosition(Caret.Paragraph + 1, 0), null, "join");
                return;
            }
            ReplaceRange(Caret, new TextPosition(Caret.Paragraph, GraphemeClusters.DeleteTarget(p.Text, Caret.Offset)), null, "delete");
        }

        /// <summary>
        /// Replaces [a, b) with a fragment (null for nothing). When the range spans paragraphs, the surviving
        /// paragraph keeps its identity, style and direction: the non-empty side wins, and the first (left)
        /// paragraph when both sides are non-empty.
        /// </summary>
        public void ReplaceRange(TextPosition a, TextPosition b, List<Paragraph>? fragment, string label)
        {
            if (IsReadOnly || !CanEditRange(a, b)) return;
            if (fragment != null)
            {
                // Pasted text takes the editability of where it lands: a copied read-only run must not become text the user
                // cannot remove from an editable field.
                fragment = fragment.Select(p => p.Clone()).ToList();
                foreach (var p in fragment)
                    foreach (var r in p.Runs)
                        r.Props.IntProps.Remove(TextPropertyNumbers.Editable);
            }
            if (SingleParagraph && fragment != null)
            {
                SeparatorsToSpaces(fragment);
                if (fragment.Count > 1) fragment = JoinIntoOne(fragment);
            }
            // Every insertion passes through here or TypeText, so the host's normalisation form always applies.
            if (fragment != null && Document.Normalization is NormalizationForm form)
            {
                fragment = fragment.Select(p => p.Clone()).ToList();
                foreach (var p in fragment)
                    foreach (var r in p.Runs)
                        if (!r.IsObject) r.Text = TextNormalizer.Normalize(r.Text, form);
            }
            var d = Begin(a.Paragraph, b.Paragraph, label);
            var first = Document.Paragraphs[a.Paragraph];
            var last = Document.Paragraphs[b.Paragraph];
            var head = first.Clone();
            head.Delete(a.Offset, head.Length - a.Offset);
            var tail = last.Clone();
            tail.Delete(0, b.Offset);
            bool headEmpty = head.Length == 0, tailEmpty = tail.Length == 0;
            var survivor = (a.Paragraph != b.Paragraph && headEmpty && !tailEmpty) ? last : first;
            var merged = survivor.Clone();
            merged.Runs = new List<Run>();
            foreach (var r in head.Runs) merged.Runs.Add(r.Clone());
            TextPosition caret;
            var newParas = new List<Paragraph>();
            if (fragment == null || fragment.Count == 0)
            {
                int off = merged.Length;
                foreach (var r in tail.Runs) merged.Runs.Add(r.Clone());
                merged.Normalize();
                newParas.Add(merged);
                caret = new TextPosition(a.Paragraph, off);
            }
            else
            {
                foreach (var r in fragment[0].Runs) merged.Runs.Add(r.Clone());
                if (fragment.Count == 1)
                {
                    int off = merged.Length;
                    foreach (var r in tail.Runs) merged.Runs.Add(r.Clone());
                    merged.Normalize();
                    newParas.Add(merged);
                    caret = new TextPosition(a.Paragraph, off);
                }
                else
                {
                    merged.Normalize();
                    newParas.Add(merged);
                    for (int i = 1; i < fragment.Count - 1; i++) newParas.Add(fragment[i].Clone(false));
                    var lastNew = tail.Clone(false);
                    // The last fragment paragraph holds the tail's new neighbour text, so its Style and Props travel together.
                    lastNew.Style = fragment[fragment.Count - 1].Style;
                    lastNew.Props = fragment[fragment.Count - 1].Props.Clone();
                    var runs = fragment[fragment.Count - 1].Runs.Select(r => r.Clone()).ToList();
                    int off = runs.Sum(r => r.Text.Length);
                    runs.AddRange(tail.Runs.Select(r => r.Clone()));
                    lastNew.Runs = runs;
                    lastNew.Normalize();
                    newParas.Add(lastNew);
                    caret = new TextPosition(a.Paragraph + fragment.Count - 1, off);
                }
            }
            Document.Paragraphs.RemoveRange(a.Paragraph, b.Paragraph - a.Paragraph + 1);
            Document.Paragraphs.InsertRange(a.Paragraph, newParas);
            d.After.AddRange(newParas.Select(p => p.Clone()));
            Finish(d, caret);
        }

        /// <summary>Enter. At offset 0 the new empty paragraph (new identity) goes before and the original identity
        /// stays on the right; anywhere else the left keeps the identity and the right is new.</summary>
        public void Enter()
        {
            if (SingleParagraph || !CanEditSelectionOrCaret()) return;
            if (HasSelection) DeleteSelection("enter-del");
            var d = Begin(Caret.Paragraph, Caret.Paragraph, "enter");
            var orig = Document.Paragraphs[Caret.Paragraph];
            int off = Caret.Offset;
            Paragraph left, right;
            if (off == 0)
            {
                left = new Paragraph { Style = orig.Style, Rtl = orig.Rtl, DefaultWs = orig.DefaultWs, Props = orig.Props.Clone() };
                right = orig.Clone();
            }
            else
            {
                left = orig.Clone();
                left.Delete(off, left.Length - off);
                right = orig.Clone(false);
                right.Delete(0, off);
            }
            Document.Paragraphs[Caret.Paragraph] = left;
            Document.Paragraphs.Insert(Caret.Paragraph + 1, right);
            d.After.Add(left.Clone());
            d.After.Add(right.Clone());
            Finish(d, new TextPosition(Caret.Paragraph + 1, 0));
        }

        // ---- fragments (clipboard) ----

        public List<Paragraph> GetFragment()
        {
            var result = new List<Paragraph>();
            if (!HasSelection) return result;
            Ordered(out var a, out var b);
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var p = Document.Paragraphs[pi];
                int s = pi == a.Paragraph ? a.Offset : 0;
                int e = pi == b.Paragraph ? b.Offset : p.Length;
                result.Add(new Paragraph { Style = p.Style, Rtl = p.Rtl, DefaultWs = p.DefaultWs, Props = p.Props.Clone(), Runs = p.Slice(s, e) });
            }
            return result;
        }

        public void Paste(List<Paragraph>? fragment)
        {
            if (fragment == null || fragment.Count == 0) return;
            Ordered(out var a, out var b);
            ReplaceRange(a, b, fragment, "paste");
        }

        // ---- host-driven ----

        /// <summary>Applies a delta, for undo and redo driven by the host.</summary>
        public void Apply(EditDelta d)
        {
            Document.Paragraphs.RemoveRange(d.Start, d.Before.Count);
            Document.Paragraphs.InsertRange(d.Start, d.After.Select(p => p.Clone()));
            var c = d.CaretAfter;
            c.Paragraph = Math.Min(c.Paragraph, Document.Paragraphs.Count - 1);
            c.Offset = Math.Min(c.Offset, Document.Paragraphs[c.Paragraph].Length);
            CloseUndoGroup();   // a change the host applies (undo, redo, a command) ends any typing step
            Caret = Anchor = c;
            _wsOverride = false;
            RefreshWs();
            DocumentChanged?.Invoke(d.Start, d.Before.Count, d.After.Count);
            CaretMoved?.Invoke();
        }

        /// <summary>An external change to one paragraph (another view edited the same string). The caret moves with
        /// the text: changeStart, removedLength and insertedLength describe the text difference.</summary>
        public void ExternalReplaceParagraph(int paragraphIndex, Paragraph replacement, int changeStart, int removedLength, int insertedLength)
        {
            Document.Paragraphs[paragraphIndex] = replacement;
            TextPosition Map(TextPosition p)
            {
                if (p.Paragraph != paragraphIndex) return p;
                if (p.Offset >= changeStart + removedLength) p.Offset += insertedLength - removedLength;
                else if (p.Offset > changeStart) p.Offset = changeStart + insertedLength;
                p.Offset = Math.Min(p.Offset, replacement.Length);
                return p;
            }
            CloseUndoGroup();
            Caret = Map(Caret);
            Anchor = Map(Anchor);
            RefreshWs();
            DocumentChanged?.Invoke(paragraphIndex, 1, 1);
            CaretMoved?.Invoke();
        }
    }

    /// <summary>A stand-in for the host's single undo stack (LCModel's, in FieldWorks): it receives deltas from the
    /// editor and applies them back. The editor never undoes itself. Deltas that share an
    /// <see cref="EditDelta.UndoGroup"/> are one step: undo and redo move a whole step at a time (ADR 0023).</summary>
    public sealed class SimpleUndoStack
    {
        DocumentEditor? _editor;

        /// <summary>Every delta done, oldest first (a typing step holds several).</summary>
        public List<EditDelta> Done { get; } = new List<EditDelta>();
        public List<EditDelta> Undone { get; } = new List<EditDelta>();

        /// <summary>The number of undo steps: a group of deltas counts once, an ungrouped delta counts as one.</summary>
        public int DoneSteps => Steps(Done);
        public int UndoneSteps => Steps(Undone);

        static int Steps(List<EditDelta> deltas)
        {
            int n = 0;
            for (int i = 0; i < deltas.Count; i++)
                if (i == 0 || deltas[i].UndoGroup == 0 || deltas[i].UndoGroup != deltas[i - 1].UndoGroup) n++;
            return n;
        }

        public void Attach(DocumentEditor editor)
        {
            _editor = editor;
            editor.Committed += d =>
            {
                Done.Add(d);
                Undone.Clear();
            };
        }

        public bool Undo()
        {
            if (Done.Count == 0 || _editor == null) return false;
            long group = Done[Done.Count - 1].UndoGroup;
            do
            {
                var d = Done[Done.Count - 1];
                Done.RemoveAt(Done.Count - 1);
                Undone.Add(d);
                _editor.Apply(d.Inverse());
            }
            while (Done.Count > 0 && group != 0 && Done[Done.Count - 1].UndoGroup == group);
            return true;
        }

        public bool Redo()
        {
            if (Undone.Count == 0 || _editor == null) return false;
            long group = Undone[Undone.Count - 1].UndoGroup;
            do
            {
                var d = Undone[Undone.Count - 1];
                Undone.RemoveAt(Undone.Count - 1);
                Done.Add(d);
                _editor.Apply(d);
            }
            while (Undone.Count > 0 && group != 0 && Undone[Undone.Count - 1].UndoGroup == group);
            return true;
        }
    }
}
