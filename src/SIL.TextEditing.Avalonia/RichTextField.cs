using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>
    /// A rich text editing surface: one <see cref="SIL.TextEditing.Document"/>, paragraphs laid out with
    /// TextFormatter, and its own caret, selection, hit testing and IME client. Every run carries its
    /// writing system. No Avalonia type leaks into the model project.
    /// </summary>
    public class RichTextField : Control
    {
        readonly List<ParagraphLayout?> _layouts = new List<ParagraphLayout?>();
        readonly InputMethodClient _ime;
        double _layoutWidth = -1;
        string? _preedit;
        string? _heldHigh;
        int _preeditCursor;
        double _goalX = -1;
        CharacterHit? _hit;
        TextPosition _hitPosition;
        bool _dragging;
        bool _moving;
        DragUnit _dragUnit;
        TextPosition _unitStart, _unitEnd;

        enum DragUnit { Character, Word, Paragraph }

        static RichTextField()
        {
            FocusableProperty.OverrideDefaultValue<RichTextField>(true);
            ClipToBoundsProperty.OverrideDefaultValue<RichTextField>(true);
        }

        public RichTextField(Document document)
        {
            Editor = new DocumentEditor(document);
            for (int i = 0; i < document.Paragraphs.Count; i++) _layouts.Add(null);
            Editor.DocumentChanged += OnDocumentChanged;
            Editor.DocumentChanged += (a, b, c) => RaiseAutomationValueChanged();
            Editor.ActiveWsChanged += (o, n) => ActiveWsChanged?.Invoke(o, n);
            Editor.CaretMoved += OnCaretMoved;
            _ime = new InputMethodClient(this);
            TextInputMethodClientRequested += (o, e) => e.Client = _ime;
            GotFocus += (o, e) => InvalidateVisual();
            LostFocus += (o, e) => InvalidateVisual();
            ActualThemeVariantChanged += (o, e) => InvalidateVisual();
        }

        /// <summary>The editing session: caret, selection, active writing system and edit events.</summary>
        public DocumentEditor Editor { get; }
        public WritingSystemRegistry WritingSystems { get; set; } = new WritingSystemRegistry();
        /// <summary>Resolves the object data of an object run to a picture, or null.</summary>
        public Func<string, IImage?> ObjectImage { get; set; } = d => null;
        /// <summary>Lay objects out as an EM SPACE and paint the picture over it: the workaround for Avalonia issue 21792
        /// (inline objects in right-to-left text are ordered wrongly in 11.3 and 12.1).</summary>
        public bool ObjectAsGlyph { get; set; }
        /// <summary>The Unicode form the host stores (FormD for FieldWorks); null keeps text as it arrives. Forwards to
        /// <see cref="SIL.TextEditing.Document.Normalization"/>.</summary>
        public System.Text.NormalizationForm? Normalization
        {
            get => Editor.Document.Normalization;
            set => Editor.Document.Normalization = value;
        }
        public IBrush Foreground { get; set; } = Brushes.Black;
        public IBrush Background { get; set; } = Brushes.White;
        public bool ShowCaretAlways { get; set; }
        /// <summary>A field-level read-only flag (forwards to <see cref="DocumentEditor.IsReadOnly"/>): selection and copy
        /// work, nothing changes the text. Runs can also be read-only one by one (ADR 0019).</summary>
        public bool IsReadOnly
        {
            get => Editor.IsReadOnly;
            set
            {
                if (Editor.IsReadOnly == value) return;
                Editor.IsReadOnly = value;
                _peer?.RaiseReadOnlyChanged(!value, value);
            }
        }
        /// <summary>The wrap width used when the available width is unbounded.</summary>
        public double PreferredWidth { get; set; } = 400;
        /// <summary>The number of paragraph layouts built so far, for measuring relayout cost.</summary>
        public long LayoutCount { get; private set; }
        public string? Preedit => _preedit;
        public TextInputMethodClient ImeClient => _ime;

        /// <summary>Raised synchronously when the active writing system changes: old, new.</summary>
        public event Action<string, string>? ActiveWsChanged;
        /// <summary>The editor asks the host to undo (Ctrl+Z); the host owns the undo stack.</summary>
        public event Action? UndoRequested;
        public event Action? RedoRequested;
        /// <summary>The writing system of the selection: "ws", or "mixed:a,b".</summary>
        public event Action<string>? SelectionWsState;

        void OnDocumentChanged(int start, int removed, int added)
        {
            for (int i = 0; i < removed && start < _layouts.Count; i++)
            {
                _layouts[start]?.Dispose();
                _layouts.RemoveAt(start);
            }
            for (int i = 0; i < added; i++) _layouts.Insert(start, null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        // ------------------------------------------------ layout

        public ParagraphLayout ParagraphLayoutFor(int index)
        {
            var existing = _layouts[index];
            if (existing != null) return existing;
            int preeditAt = -1;
            string? preedit = null;
            Run? template = null;
            if (_preedit != null && index == Editor.Caret.Paragraph)
            {
                preeditAt = Editor.Caret.Offset;
                preedit = _preedit;
                template = new Run { Ws = Editor.ActiveWs };
            }
            var layout = ParagraphLayout.Build(Editor.Document.Paragraphs[index], _layoutWidth > 0 ? _layoutWidth : PreferredWidth, WritingSystems, ObjectImage, Foreground, ObjectAsGlyph, preeditAt, preedit, template);
            _layouts[index] = layout;
            LayoutCount++;
            return layout;
        }

        public double ParagraphTop(int index)
        {
            double y = 0;
            for (int k = 0; k < index; k++) y += ParagraphLayoutFor(k).Height;
            return y;
        }

        public void InvalidateAllLayouts()
        {
            for (int i = 0; i < _layouts.Count; i++)
            {
                _layouts[i]?.Dispose();
                _layouts[i] = null;
            }
            InvalidateMeasure();
            InvalidateVisual();
        }

        /// <summary>Call after the host changed the Document directly (not through the editor): drops every layout and
        /// resizes the layout list to the paragraph count.</summary>
        public void RefreshFromDocument()
        {
            foreach (var l in _layouts) l?.Dispose();
            _layouts.Clear();
            for (int i = 0; i < Editor.Document.Paragraphs.Count; i++) _layouts.Add(null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double w = double.IsInfinity(availableSize.Width) ? PreferredWidth : availableSize.Width;
            if (Math.Abs(w - _layoutWidth) > 0.5)
            {
                _layoutWidth = w;
                for (int i = 0; i < _layouts.Count; i++)
                {
                    _layouts[i]?.Dispose();
                    _layouts[i] = null;
                }
            }
            double h = 0;
            for (int i = 0; i < _layouts.Count; i++) h += ParagraphLayoutFor(i).Height;
            return new Size(w, Math.Max(h + 2, 24));
        }

        /// <summary>Sets the caret affinity (which visual stop a bidi boundary offset means) for the current caret
        /// offset. Arrow-key movement sets it itself; a host restoring a caret may need it.</summary>
        public void SetCaretHit(CharacterHit hit)
        {
            _hit = hit;
            _hitPosition = Editor.Caret;
        }

        /// <summary>The caret rectangle in the coordinates of this control.</summary>
        public Rect CaretRect()
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            var r = pl.CaretRect(c.Offset, false, CurrentHit());
            if (_preedit != null) r = pl.CaretRect(c.Offset + _preeditCursor, false);
            return new Rect(r.X, r.Y + ParagraphTop(c.Paragraph), 1.5, r.Height);
        }

        // ------------------------------------------------ rendering

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Background, null, new Rect(Bounds.Size));
            if (_layouts.Count == 0) return;
            Editor.Ordered(out var a, out var b);
            var selectionBrush = Editor.HasSelection ? ResolveSelectionBrush() : null;
            double y = 0;
            var clip = new Rect(Bounds.Size);
            for (int i = 0; i < _layouts.Count; i++)
            {
                var pl = ParagraphLayoutFor(i);
                if (y + pl.Height >= 0 && y <= clip.Bottom + 1)
                {
                    if (Editor.HasSelection && i >= a.Paragraph && i <= b.Paragraph)
                    {
                        int s = i == a.Paragraph ? a.Offset : 0;
                        int e = i == b.Paragraph ? b.Offset : pl.Paragraph.Length;
                        foreach (var r in pl.SelectionRects(s, e))
                            context.DrawRectangle(selectionBrush, null, new Rect(r.X, r.Y + y, r.Width, r.Height));
                    }
                    foreach (var li in pl.Lines) li.Line.Draw(context, new Point(0, y + li.Y));
                    if (ObjectAsGlyph)
                    {
                        foreach (var o in pl.ObjectRects())
                        {
                            var img = ObjectImage(o.Value);
                            var rr = new Rect(o.Key.X, y + o.Key.Y + (o.Key.Height - 24) / 2, 32, 24);
                            if (img != null) context.DrawImage(img, rr);
                        }
                    }
                }
                y += pl.Height;
            }
            if (IsFocused || ShowCaretAlways) context.DrawRectangle(ResolveCaretBrush(), null, CaretRect());
        }

        // ------------------------------------------------ caret movement

        void OnCaretMoved()
        {
            if (!_moving) { _goalX = -1; _hit = null; }
            InvalidateVisual();
            _ime.NotifyCaret();
            if (Editor.HasSelection)
            {
                var set = Editor.SelectionWsSet();
                SelectionWsState?.Invoke(set.Count > 1 ? "mixed:" + string.Join(",", set) : set[0]);
            }
            else SelectionWsState?.Invoke(Editor.ActiveWs);
        }

        /// <summary>The caret hit (affinity) that applies to the current caret, or null when it has none.</summary>
        CharacterHit? CurrentHit() => _hitPosition.Equals(Editor.Caret) ? _hit : null;

        void Move(TextPosition p, bool extend, bool keepGoal = false, CharacterHit? hit = null)
        {
            if (!keepGoal) _goalX = -1;
            _hit = hit;
            _hitPosition = p;
            _moving = true;
            try { Editor.SetCaret(p, extend, p.CompareTo(Editor.Caret)); }
            finally { _moving = false; }
        }

        /// <summary>Left or Right arrow, logical as in FieldWorks: Left goes backwards in a left-to-right paragraph and
        /// forwards in a right-to-left one, Right the reverse, one cluster at a time, even where that moves the caret the
        /// other way on screen inside mixed text. At a paragraph edge it crosses into the neighbour. The caret is drawn at
        /// the visual position of its logical offset: after a forward step on the trailing edge of the cluster passed,
        /// after a backward step on the leading edge of the cluster now after the caret.</summary>
        public bool MoveVisualLeftRight(bool left, bool extend)
        {
            var c = Editor.Caret;
            var para = Editor.Document.Paragraphs[c.Paragraph];
            bool forward = para.Rtl ? left : !left;   // the direction of the paragraph at the selection's focus (the caret)
            if (!extend && Editor.HasSelection)
            {
                Editor.Ordered(out var a, out var b);
                Move(forward ? b : a, false);
                return true;
            }
            if (forward)
            {
                if (c.Offset < para.Length)
                {
                    int n = GraphemeClusters.Next(para.Text, c.Offset);
                    Move(new TextPosition(c.Paragraph, n), extend, false, ParagraphLayoutFor(c.Paragraph).EndHit(n));
                }
                else if (c.Paragraph + 1 < Editor.Document.Paragraphs.Count) Move(new TextPosition(c.Paragraph + 1, 0), extend);
            }
            else if (c.Offset > 0)
            {
                int n = GraphemeClusters.Previous(para.Text, c.Offset);
                Move(new TextPosition(c.Paragraph, n), extend, false, new CharacterHit(ParagraphLayoutFor(c.Paragraph).ToEffective(n)));
            }
            else if (c.Paragraph > 0)
            {
                int pi = c.Paragraph - 1, len = Editor.Document.Paragraphs[pi].Length;
                Move(new TextPosition(pi, len), extend, false, ParagraphLayoutFor(pi).EndHit(len));
            }
            return true;
        }

        /// <summary>Ctrl+Left or Ctrl+Right: to the previous or next word start (see <see cref="WordBreaks"/>). The word
        /// order is logical, so in a right-to-left paragraph Ctrl+Left goes to the next word. At a paragraph edge it
        /// crosses into the neighbouring paragraph.</summary>
        public void MoveByWord(bool left, bool extend)
        {
            var c = Editor.Caret;
            var para = Editor.Document.Paragraphs[c.Paragraph];
            bool backward = para.Rtl ? !left : left;
            if (backward)
            {
                if (c.Offset > 0) Move(new TextPosition(c.Paragraph, WordBreaks.PreviousWordStart(para.Text, c.Offset)), extend);
                else if (c.Paragraph > 0) Move(new TextPosition(c.Paragraph - 1, Editor.Document.Paragraphs[c.Paragraph - 1].Length), extend);
            }
            else
            {
                if (c.Offset < para.Length) Move(new TextPosition(c.Paragraph, WordBreaks.NextWordStart(para.Text, c.Offset)), extend);
                else if (c.Paragraph + 1 < Editor.Document.Paragraphs.Count) Move(new TextPosition(c.Paragraph + 1, 0), extend);
            }
        }

        void MoveVertical(bool down, bool extend)
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            var hit = CurrentHit();
            var r = pl.CaretRect(c.Offset, false, hit);
            double gx = _goalX >= 0 ? _goalX : r.X;
            int li = pl.LineIndexOf(c.Offset, hit);
            double top = ParagraphTop(c.Paragraph);
            double y;
            if (down)
            {
                if (pl.Lines.Count > 0 && li + 1 < pl.Lines.Count) y = top + pl.Lines[li + 1].Y + 1;
                else if (c.Paragraph + 1 < _layouts.Count) y = top + pl.Height + 1;
                else { Move(new TextPosition(c.Paragraph, pl.Paragraph.Length), extend, true); return; }
            }
            else
            {
                if (li > 0) y = top + pl.Lines[li - 1].Y + 1;
                else if (c.Paragraph > 0) y = top - 1;
                else { Move(new TextPosition(c.Paragraph, 0), extend, true); return; }
            }
            var target = Locate(new Point(gx, y));
            _goalX = gx;
            Move(target.Position, extend, true, target.Hit);
        }

        struct Located
        {
            public TextPosition Position;
            public CharacterHit Hit;
            public TextPosition Character;
        }

        Located Locate(Point point)
        {
            double y = 0;
            for (int i = 0; i < _layouts.Count; i++)
            {
                var pl = ParagraphLayoutFor(i);
                if (point.Y < y + pl.Height || i == _layouts.Count - 1)
                {
                    var h = pl.HitTestDetailed(new Point(point.X, point.Y - y));
                    return new Located { Position = new TextPosition(i, h.Offset), Hit = h.CaretHit, Character = new TextPosition(i, h.Character) };
                }
                y += pl.Height;
            }
            return default;
        }

        /// <summary>The document position at a point in the coordinates of this control.</summary>
        public TextPosition PointToPosition(Point point) => Locate(point).Position;

        // ------------------------------------------------ input

        /// <summary>Handles an editing key; returns false for keys it leaves to the host (Tab and the like).</summary>
        public bool HandleKey(Key key, KeyModifiers modifiers)
        {
            bool shift = (modifiers & KeyModifiers.Shift) != 0, ctrl = (modifiers & KeyModifiers.Control) != 0;
            var lastPara = _layouts.Count - 1;
            switch (key)
            {
                case Key.Left: if (ctrl) MoveByWord(true, shift); else MoveVisualLeftRight(true, shift); return true;
                case Key.Right: if (ctrl) MoveByWord(false, shift); else MoveVisualLeftRight(false, shift); return true;
                case Key.Up: MoveVertical(false, shift); return true;
                case Key.Down: MoveVertical(true, shift); return true;
                case Key.Home:
                {
                    var c = Editor.Caret;
                    if (ctrl) { Move(new TextPosition(0, 0), shift); return true; }
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    int o = pl.Lines.Count > 0 ? pl.ToModel(pl.Lines[pl.LineIndexOf(c.Offset, CurrentHit())].Start) : 0;
                    Move(new TextPosition(c.Paragraph, o), shift);
                    return true;
                }
                case Key.End:
                {
                    var c = Editor.Caret;
                    if (ctrl) { Move(new TextPosition(lastPara, Editor.Document.Paragraphs[lastPara].Length), shift); return true; }
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    if (pl.Lines.Count == 0) { Move(new TextPosition(c.Paragraph, pl.Paragraph.Length), shift); return true; }
                    // At a soft wrap the end of the line and the start of the next are one offset: keep the caret on this line.
                    int o = pl.LineEnd(pl.LineIndexOf(c.Offset, CurrentHit()), out var hit);
                    Move(new TextPosition(c.Paragraph, o), shift, false, hit);
                    return true;
                }
                case Key.Back: Editor.Backspace(); return true;
                case Key.Delete: Editor.DeleteForward(); return true;
                case Key.Enter: Editor.Enter(); return true;
                case Key.A when ctrl:
                    Editor.Anchor = new TextPosition(0, 0);
                    Move(new TextPosition(lastPara, Editor.Document.Paragraphs[lastPara].Length), true);
                    return true;
                case Key.Z when ctrl: UndoRequested?.Invoke(); return true;
                case Key.Y when ctrl: RedoRequested?.Invoke(); return true;
                case Key.C when ctrl: _ = CopyAsync(false); return true;
                case Key.X when ctrl: _ = CopyAsync(true); return true;
                case Key.V when ctrl: _ = PasteAsync(); return true;
            }
            return false;
        }

        /// <summary>Inserts a held lone high surrogate as it is (it never got its low half).</summary>
        void FlushHeld()
        {
            if (_heldHigh == null) return;
            var h = _heldHigh;
            _heldHigh = null;
            Editor.TypeText(h);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            FlushHeld();
            if (HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            var text = e.Text!;
            // A platform may deliver one emoji as two events, one per UTF-16 half. A lone high surrogate is held until the
            // next text arrives so the pair is normalised and inserted whole; any other input flushes it unchanged first.
            if (_heldHigh != null)
            {
                if (char.IsLowSurrogate(text[0])) text = _heldHigh + text;
                else FlushHeld();
                _heldHigh = null;
            }
            if (TextNormalizer.EndsWithLoneHighSurrogate(text))
            {
                _heldHigh = text.Substring(text.Length - 1);
                text = text.Substring(0, text.Length - 1);
                e.Handled = true;
                if (text.Length == 0) return;
            }
            if (_preedit != null)
            {
                _preedit = null;
                _layouts[Editor.Caret.Paragraph]?.Dispose();
                _layouts[Editor.Caret.Paragraph] = null;
            }
            Editor.TypeText(text);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            FlushHeld();
            var props = e.GetCurrentPoint(this).Properties;
            if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed) return;
            Focus();
            var loc = Locate(e.GetPosition(this));
            if (!props.IsLeftButtonPressed)
            {
                // Right click (context menu): keep a selection that was clicked inside, otherwise move the caret there.
                Editor.Ordered(out var sa, out var sb);
                bool inside = Editor.HasSelection && loc.Position.CompareTo(sa) >= 0 && loc.Position.CompareTo(sb) <= 0;
                if (!inside) Move(loc.Position, false, false, loc.Hit);
                e.Handled = true;
                return;
            }
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            _dragUnit = DragUnit.Character;
            if (shift) Move(loc.Position, true, false, loc.Hit);
            else if (e.ClickCount == 2) { _dragUnit = DragUnit.Word; SelectUnit(loc, true); }
            else if (e.ClickCount >= 3) { _dragUnit = DragUnit.Paragraph; SelectUnit(loc, true); }
            else Move(loc.Position, false, false, loc.Hit);
            _dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        /// <summary>The word or paragraph under the pointer as a pair of positions.</summary>
        void UnitAt(Located loc, out TextPosition start, out TextPosition end)
        {
            var para = Editor.Document.Paragraphs[loc.Character.Paragraph];
            if (_dragUnit == DragUnit.Paragraph) { start = new TextPosition(loc.Character.Paragraph, 0); end = new TextPosition(loc.Character.Paragraph, para.Length); return; }
            WordBreaks.WordAt(para.Text, loc.Character.Offset, out int s, out int en);
            start = new TextPosition(loc.Character.Paragraph, s);
            end = new TextPosition(loc.Character.Paragraph, en);
        }

        /// <summary>Selects the word or paragraph under the pointer; while dragging, keeps the first unit selected and
        /// extends to the unit under the pointer.</summary>
        void SelectUnit(Located loc, bool first)
        {
            UnitAt(loc, out var start, out var end);
            if (first) { _unitStart = start; _unitEnd = end; }
            bool before = start.CompareTo(_unitStart) < 0;
            var caret = before ? start : end;
            Editor.Anchor = before ? _unitEnd : _unitStart;
            var hit = caret.Offset > 0 ? ParagraphLayoutFor(caret.Paragraph).EndHit(caret.Offset) : (CharacterHit?)null;
            Move(caret, true, false, before ? null : hit);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging) return;
            var loc = Locate(e.GetPosition(this));
            if (_dragUnit == DragUnit.Character) Move(loc.Position, true, false, loc.Hit);
            else SelectUnit(loc, false);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _dragging = false;
        }

        // ------------------------------------------------ clipboard

        public async Task CopyAsync(bool cut)
        {
            if (!Editor.HasSelection) return;
            var fragment = Editor.GetFragment();
            var xml = Interchange.ToXml(fragment);
            var plain = TextNormalizer.Normalize(string.Join("\n", fragment.Select(p => p.Text)), System.Text.NormalizationForm.FormC);
            await ClipboardCompat.SetAsync(TopLevel.GetTopLevel(this), plain, xml);
            if (cut) Editor.DeleteSelection("cut");
        }

        public async Task PasteAsync()
        {
            var topLevel = TopLevel.GetTopLevel(this);
            var xml = await ClipboardCompat.GetXmlAsync(topLevel);
            if (!string.IsNullOrEmpty(xml))
            {
                Editor.Paste(Interchange.FromXml(xml!));
                return;
            }
            var text = await ClipboardCompat.GetTextAsync(topLevel);
            if (!string.IsNullOrEmpty(text)) Editor.TypeText(text ?? string.Empty);
        }

        // ------------------------------------------------ IME

        /// <summary>Shows an uncommitted composition string inline at the caret; it never enters the model.</summary>
        public void SetPreedit(string? text, int? cursor)
        {
            if (text != null && !Editor.CanEdit) text = null;   // an input method cannot compose into text that may not be edited
            _preedit = string.IsNullOrEmpty(text) ? null : text;
            _preeditCursor = cursor ?? (_preedit?.Length ?? 0);
            int pi = Editor.Caret.Paragraph;
            _layouts[pi]?.Dispose();
            _layouts[pi] = null;
            InvalidateMeasure();
            InvalidateVisual();
            _ime.NotifyCaret();
        }

        sealed class InputMethodClient : TextInputMethodClient
        {
            readonly RichTextField _field;

            public InputMethodClient(RichTextField field) { _field = field; }

            public override Visual TextViewVisual => _field;
            public override bool SupportsPreedit => true;
            public override bool SupportsSurroundingText => true;
            public override string SurroundingText => _field.Editor.Document.Paragraphs[_field.Editor.Caret.Paragraph].Text;
            public override Rect CursorRectangle => _field.CaretRect();

            public override TextSelection Selection
            {
                get
                {
                    var c = _field.Editor.Caret;
                    var a = _field.Editor.Anchor;
                    return new TextSelection(a.Paragraph == c.Paragraph ? a.Offset : c.Offset, c.Offset);
                }
                set
                {
                    _field.Editor.Anchor = new TextPosition(_field.Editor.Caret.Paragraph, value.Start);
                    _field.Editor.SetCaret(new TextPosition(_field.Editor.Caret.Paragraph, value.End), true);
                }
            }

            public override void SetPreeditText(string? preeditText) => _field.SetPreedit(preeditText, null);
            public override void SetPreeditText(string? preeditText, int? cursorPos) => _field.SetPreedit(preeditText, cursorPos);

            public void NotifyCaret()
            {
                RaiseCursorRectangleChanged();
                RaiseSelectionChanged();
                RaiseSurroundingTextChanged();
            }
        }

        // ------------------------------------------------ automation

        protected override AutomationPeer OnCreateAutomationPeer() => _peer = new FieldPeer(this);

        /// <summary>The text a screen reader reads: paragraphs joined by CR LF, each embedded object as U+FFFC.</summary>
        internal string AutomationValue => string.Join("\r\n", Editor.Document.Paragraphs.Select(p => p.Text));

        FieldPeer? _peer;

        sealed class FieldPeer : ControlAutomationPeer, global::Avalonia.Automation.Provider.IValueProvider
        {
            readonly RichTextField _field;
            string _lastValue;

            public FieldPeer(RichTextField field) : base(field) { _field = field; _lastValue = field.AutomationValue; }

            // True when the whole field is read-only (a field-level flag, ADR 0019); read-only runs inside an editable field do not count.
            public bool IsReadOnly => _field.IsReadOnly;
            public string? Value => _field.AutomationValue;
            public void SetValue(string? value) { }

            public void RaiseReadOnlyChanged(bool old, bool now) =>
                RaisePropertyChangedEvent(global::Avalonia.Automation.ValuePatternIdentifiers.IsReadOnlyProperty, old, now);

            public void RaiseValueChanged()
            {
                var now = _field.AutomationValue;
                if (now == _lastValue) return;
                var old = _lastValue;
                _lastValue = now;
                RaisePropertyChangedEvent(global::Avalonia.Automation.ValuePatternIdentifiers.ValueProperty, old, now);
            }

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
            protected override bool IsKeyboardFocusableCore() => true;
            // Avalonia raises the focus-changed event itself for a focusable control; there is no public API to raise it.
            protected override bool HasKeyboardFocusCore() => _field.IsFocused;
        }

        void RaiseAutomationValueChanged() => _peer?.RaiseValueChanged();

        // ------------------------------------------------ system colours

        /// <summary>The selection fill: the theme or system highlight resource, else light sky blue.</summary>
        public IBrush ResolveSelectionBrush() =>
            ThemeBrush(Brushes.LightSkyBlue, "TextControlSelectionHighlightColor", "SystemControlHighlightAccentBrush", "SystemColorHighlightColor");

        /// <summary>The caret fill: the theme or system text-colour resource, else black.</summary>
        public IBrush ResolveCaretBrush() =>
            ThemeBrush(Brushes.Black, "TextControlForeground", "SystemControlForegroundBaseHighBrush", "SystemColorWindowTextColor");

        /// <summary>The first of the named theme or system resources that is a colour or brush; else the fallback look.</summary>
        IBrush ThemeBrush(IBrush fallback, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!this.TryFindResource(key, ActualThemeVariant, out var value)) continue;
                if (value is IBrush brush) return brush;
                if (value is Color color) return new SolidColorBrush(color);
            }
            return fallback;
        }
    }
}
