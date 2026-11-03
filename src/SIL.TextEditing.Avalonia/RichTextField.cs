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
        int _preeditCursor;
        double _goalX = -1;
        CharacterHit? _hit;
        TextPosition _hitPosition;
        bool _dragging;

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
            Editor.ActiveWsChanged += (o, n) => ActiveWsChanged?.Invoke(o, n);
            Editor.CaretMoved += OnCaretMoved;
            _ime = new InputMethodClient(this);
            TextInputMethodClientRequested += (o, e) => e.Client = _ime;
            GotFocus += (o, e) => InvalidateVisual();
            LostFocus += (o, e) => InvalidateVisual();
        }

        /// <summary>The editing session: caret, selection, active writing system and edit events.</summary>
        public DocumentEditor Editor { get; }
        public WritingSystemRegistry WritingSystems { get; set; } = WritingSystemRegistry.CreateDefault();
        /// <summary>Resolves the object data of an object run to a picture, or null.</summary>
        public Func<string, IImage?> ObjectImage { get; set; } = d => null;
        /// <summary>Lay objects out as an EM SPACE and paint the picture over it: the workaround for Avalonia issue 21792
        /// (inline objects in right-to-left text are ordered wrongly in 11.3 and 12.1).</summary>
        public bool ObjectAsGlyph { get; set; }
        public IBrush Foreground { get; set; } = Brushes.Black;
        public IBrush Background { get; set; } = Brushes.White;
        public bool ShowCaretAlways { get; set; }
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
            var r = pl.CaretRect(c.Offset, false, _hitPosition.Equals(c) ? _hit : null);
            if (_preedit != null) r = pl.CaretRect(c.Offset + _preeditCursor, false);
            return new Rect(r.X, r.Y + ParagraphTop(c.Paragraph), 1.5, r.Height);
        }

        // ------------------------------------------------ rendering

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Background, null, new Rect(Bounds.Size));
            if (_layouts.Count == 0) return;
            Editor.Ordered(out var a, out var b);
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
                            context.DrawRectangle(Brushes.LightSkyBlue, null, new Rect(r.X, r.Y + y, r.Width, r.Height));
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
            if (IsFocused || ShowCaretAlways) context.DrawRectangle(Brushes.Black, null, CaretRect());
        }

        // ------------------------------------------------ caret movement

        void OnCaretMoved()
        {
            InvalidateVisual();
            _ime.NotifyCaret();
            if (Editor.HasSelection)
            {
                var set = Editor.SelectionWsSet();
                SelectionWsState?.Invoke(set.Count > 1 ? "mixed:" + string.Join(",", set) : set[0]);
            }
            else SelectionWsState?.Invoke(Editor.ActiveWs);
        }

        void Move(TextPosition p, bool extend, bool keepGoal = false)
        {
            if (!keepGoal) _goalX = -1;
            Editor.SetCaret(p, extend);
        }

        /// <summary>Left or Right arrow: moves to the next visual caret stop, crossing paragraphs at the edges.</summary>
        public bool MoveVisualLeftRight(bool left, bool extend)
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            if (!extend && Editor.HasSelection)
            {
                Editor.Ordered(out var a, out var b);
                var ra = ParagraphLayoutFor(a.Paragraph).CaretRect(a.Offset);
                var rb = ParagraphLayoutFor(b.Paragraph).CaretRect(b.Offset);
                Move(((ra.X <= rb.X) == left) && a.Paragraph == b.Paragraph ? a : (a.Paragraph == b.Paragraph ? b : (left ? a : b)), false);
                return true;
            }
            if (pl.MoveVisual(c.Offset, _hitPosition.Equals(c) ? _hit : null, left, out var n, out var nh))
            {
                Move(new TextPosition(c.Paragraph, n), extend);
                _hit = nh;
                _hitPosition = Editor.Caret;
                return true;
            }
            bool forward = pl.Rtl ? left : !left;
            if (forward)
            {
                if (c.Paragraph + 1 < Editor.Document.Paragraphs.Count) Move(new TextPosition(c.Paragraph + 1, 0), extend);
            }
            else if (c.Paragraph > 0) Move(new TextPosition(c.Paragraph - 1, Editor.Document.Paragraphs[c.Paragraph - 1].Length), extend);
            return true;
        }

        void MoveVertical(bool down, bool extend)
        {
            var c = Editor.Caret;
            var pl = ParagraphLayoutFor(c.Paragraph);
            var r = pl.CaretRect(c.Offset);
            double gx = _goalX >= 0 ? _goalX : r.X;
            int li = pl.LineIndexOf(c.Offset);
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
            var np = PointToPosition(new Point(gx, y));
            _goalX = gx;
            Move(np, extend, true);
        }

        /// <summary>The document position at a point in the coordinates of this control.</summary>
        public TextPosition PointToPosition(Point point)
        {
            double y = 0;
            for (int i = 0; i < _layouts.Count; i++)
            {
                var pl = ParagraphLayoutFor(i);
                if (point.Y < y + pl.Height || i == _layouts.Count - 1) return new TextPosition(i, pl.HitTest(new Point(point.X, point.Y - y)));
                y += pl.Height;
            }
            return new TextPosition(0, 0);
        }

        // ------------------------------------------------ input

        /// <summary>Handles an editing key; returns false for keys it leaves to the host (Tab and the like).</summary>
        public bool HandleKey(Key key, KeyModifiers modifiers)
        {
            bool shift = (modifiers & KeyModifiers.Shift) != 0, ctrl = (modifiers & KeyModifiers.Control) != 0;
            var lastPara = _layouts.Count - 1;
            switch (key)
            {
                case Key.Left: MoveVisualLeftRight(true, shift); return true;
                case Key.Right: MoveVisualLeftRight(false, shift); return true;
                case Key.Up: MoveVertical(false, shift); return true;
                case Key.Down: MoveVertical(true, shift); return true;
                case Key.Home:
                {
                    var c = Editor.Caret;
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    int o = 0;
                    if (!ctrl && pl.Lines.Count > 0) o = pl.ToModel(pl.Lines[pl.LineIndexOf(c.Offset)].Start);
                    Move(ctrl ? new TextPosition(0, 0) : new TextPosition(c.Paragraph, o), shift);
                    return true;
                }
                case Key.End:
                {
                    var c = Editor.Caret;
                    var pl = ParagraphLayoutFor(c.Paragraph);
                    int o = pl.Paragraph.Length;
                    if (!ctrl && pl.Lines.Count > 0) o = pl.ToModel(pl.TextEnd(pl.Lines[pl.LineIndexOf(c.Offset)]));
                    if (ctrl) Move(new TextPosition(lastPara, Editor.Document.Paragraphs[lastPara].Length), shift);
                    else Move(new TextPosition(c.Paragraph, o), shift);
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

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (HandleKey(e.Key, e.KeyModifiers)) e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            if (_preedit != null)
            {
                _preedit = null;
                _layouts[Editor.Caret.Paragraph]?.Dispose();
                _layouts[Editor.Caret.Paragraph] = null;
            }
            Editor.TypeText(e.Text!);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            Focus();
            var pos = PointToPosition(e.GetPosition(this));
            Move(pos, (e.KeyModifiers & KeyModifiers.Shift) != 0);
            _dragging = true;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_dragging) Move(PointToPosition(e.GetPosition(this)), true);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            _dragging = false;
            e.Pointer.Capture(null);
        }

        // ------------------------------------------------ clipboard

        public async Task CopyAsync(bool cut)
        {
            if (!Editor.HasSelection) return;
            var fragment = Editor.GetFragment();
            var xml = Interchange.ToXml(fragment);
            var plain = string.Join("\n", fragment.Select(p => p.Text));
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

        protected override AutomationPeer OnCreateAutomationPeer() => new FieldPeer(this);

        sealed class FieldPeer : ControlAutomationPeer, global::Avalonia.Automation.Provider.IValueProvider
        {
            readonly RichTextField _field;

            public FieldPeer(RichTextField field) : base(field) { _field = field; }

            public bool IsReadOnly => true;
            public string? Value => string.Join("\n", _field.Editor.Document.Paragraphs.Select(p => p.Text));
            public void SetValue(string? value) { }
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
            protected override string? GetNameCore() => "Rich text field";
            protected override bool IsContentElementCore() => true;
            protected override bool IsControlElementCore() => true;
        }
    }
}
