using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;

namespace SIL.TextEditing.Avalonia
{
    // The completion popup of the language service (ADR 0030): Ctrl+Space, or one of the service's trigger characters, asks the service for the items at the caret;
    // Up, Down, PageUp and PageDown move through them, Enter or Tab accepts, Esc closes. The keyboard stays with the field (the popup is never focused); the
    // list is plain panels with names, not a ListBox, so it needs no theme and a screen reader finds its rows by name.
    public partial class RichTextField
    {
        /// <summary>How many rows the popup shows at once.</summary>
        public const int CompletionRows = 8;

        sealed class CompletionState
        {
            public List<CompletionItem> Items = null!;
            public int Index;
            public int Top;
            public Paragraph Paragraph = null!;
            public long Stamp;
            public TextPosition Caret;
        }

        CompletionState? _completion;
        int _complRequest;
        bool _complRefreshPending;
        bool _complAccepting;
        Popup? _complPopup;
        StackPanel? _complRows;

        /// <summary>True while the completion popup is open.</summary>
        public bool IsCompletionOpen => _completion != null;

        /// <summary>The items the popup offers, in the order the service gave them; empty when it is closed.</summary>
        public IReadOnlyList<CompletionItem> CompletionItems => _completion == null ? new CompletionItem[0] : _completion.Items.ToList();

        /// <summary>The index of the highlighted item, or -1 when the popup is closed.</summary>
        public int CompletionIndex => _completion?.Index ?? -1;

        /// <summary>Raised when the popup opens, closes or shows other items or another highlight, so a host (an accessibility bridge, a test) can follow it.</summary>
        public event EventHandler? CompletionChanged;

        /// <summary>Asks the service for the completions at the caret, as Ctrl+Space does, and opens the popup when it has some. The answer is used only if the text and the
        /// caret are as they were when it was asked. Nothing happens with no service, in a read-only field, where the text may not be edited, or while an input method is composing.</summary>
        public Task ShowCompletionAsync() => RequestCompletion(CompletionTriggerKind.Invoked);

        async Task RequestCompletion(CompletionTriggerKind kind)
        {
            var service = _lang;
            if (service == null || !LanguageActive || Editor.IsReadOnly || !Editor.CanEdit || _preedit != null) return;
            var paragraph = Editor.Document.Paragraphs[0];
            long stamp = paragraph.ChangeStamp;
            string text = paragraph.Text;
            var caret = Editor.Caret;
            int request = ++_complRequest;
            int epoch = _langEpoch;
            IReadOnlyList<CompletionItem> items;
            try { items = await service.CompleteAsync(text, LanguageText.PositionOf(text, Math.Min(caret.Offset, text.Length)), kind, CancellationToken.None); }
            catch (Exception e)
            {
                if (request == _complRequest) LanguageFailure(e);
                return;
            }
            if (request != _complRequest || epoch != _langEpoch || !LanguageActive || !ReferenceEquals(Editor.Document.Paragraphs[0], paragraph)
                || paragraph.ChangeStamp != stamp || !Editor.Caret.Equals(caret) || Editor.HasSelection || _preedit != null) return;
            if (items == null || items.Count == 0) { CloseCompletion(); return; }
            _completion = new CompletionState { Items = items.ToList(), Index = 0, Paragraph = paragraph, Stamp = stamp, Caret = caret };
            ShowCompletionPopup();
            CompletionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Moves the highlight by <paramref name="delta"/> items, stopping at the ends. False when the popup is closed.</summary>
        public bool MoveCompletion(int delta)
        {
            var c = _completion;
            if (c == null) return false;
            int index = Math.Max(0, Math.Min(c.Items.Count - 1, c.Index + delta));
            if (index == c.Index) return true;
            c.Index = index;
            if (index < c.Top) c.Top = index;
            else if (index >= c.Top + CompletionRows) c.Top = index - CompletionRows + 1;
            ShowCompletionPopup();
            CompletionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Types the highlighted item in place of what it replaces, as one undo step, puts the caret where the item says (<see cref="CompletionItem.CaretOffset"/>) and closes
        /// the popup. False, and the popup closes, when the text changed since the items were asked for or may no longer be edited.</summary>
        public bool AcceptCompletion()
        {
            var c = _completion;
            if (c == null) return false;
            var item = c.Items[c.Index];
            CloseCompletion();
            if (!LanguageActive || !ReferenceEquals(Editor.Document.Paragraphs[0], c.Paragraph) || c.Paragraph.ChangeStamp != c.Stamp) return false;
            string text = c.Paragraph.Text;
            int start, end;
            if (item.ReplaceRange is LanguageRange range) (start, end) = Offsets(text, range);
            else
            {
                Editor.Ordered(out var a, out var b);
                start = a.Offset;
                end = b.Offset;
            }
            _complAccepting = true;
            try
            {
                if (!ReplaceLanguage(start, end, item.Inserted, "completion")) return false;
                if (item.CaretOffset is int caretOffset) Editor.SetCaret(new TextPosition(0, Math.Min(start + Math.Max(0, caretOffset), Editor.Document.Paragraphs[0].Length)));
            }
            finally { _complAccepting = false; }
            return true;
        }

        /// <summary>Closes the popup; nothing else changes. Does nothing when it is closed.</summary>
        public void CloseCompletion()
        {
            _complRequest++;
            _complRefreshPending = false;
            if (_completion == null) return;
            _completion = null;
            if (_complPopup != null) _complPopup.IsOpen = false;
            CompletionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>After an edit with the popup open the list is asked for again, once the edit's events are over, so it fits what is now typed.</summary>
        void ScheduleCompletionRefresh()
        {
            if (_complRefreshPending) return;
            _complRefreshPending = true;
            Post(() =>
            {
                if (!_complRefreshPending || _completion == null) return;
                _complRefreshPending = false;
                _ = RequestCompletion(CompletionTriggerKind.Refresh);
            });
        }

        /// <summary>The caret moved: a popup that was asked for another place closes (typing moves the caret too, and has its own refresh).</summary>
        void CompletionCaretMoved()
        {
            var c = _completion;
            if (c == null || _complRefreshPending || _complAccepting) return;
            if (Editor.HasSelection || !Editor.Caret.Equals(c.Caret)) CloseCompletion();
        }

        /// <summary>Keys for the language service. Ctrl+Space opens the popup; with it open Up, Down, PageUp and PageDown move, Enter and Tab accept and Esc closes
        /// (Enter does not reach the host then, and Tab does not leave the field). Everything else goes on to the field, and a key that moves the caret closes the popup.</summary>
        bool HandleLanguageKey(Key key, KeyModifiers modifiers)
        {
            if (_lang == null) return false;
            bool ctrl = (modifiers & KeyModifiers.Control) != 0, other = (modifiers & (KeyModifiers.Alt | KeyModifiers.Meta | KeyModifiers.Shift)) != 0;
            if (key == Key.Space && ctrl && !other)
            {
                _ = RequestCompletion(CompletionTriggerKind.Invoked);
                return true;
            }
            if (_completion == null || ctrl || other) return false;
            switch (key)
            {
                case Key.Down: MoveCompletion(1); return true;
                case Key.Up: MoveCompletion(-1); return true;
                case Key.PageDown: MoveCompletion(CompletionRows); return true;
                case Key.PageUp: MoveCompletion(-CompletionRows); return true;
                case Key.Enter:
                case Key.Tab: AcceptCompletion(); return true;
                case Key.Escape: CloseCompletion(); return true;
            }
            return false;
        }

        /// <summary>Typed text that ends in a trigger character of the service opens the popup.</summary>
        void CompletionAfterTyping(string typed)
        {
            var service = _lang;
            if (service == null || typed.Length == 0 || !LanguageActive || _preedit != null) return;
            if (service.CompletionTriggerCharacters.Contains(typed[typed.Length - 1])) _ = RequestCompletion(CompletionTriggerKind.TriggerCharacter);
        }

        // ------------------------------------------------ the popup

        void ShowCompletionPopup()
        {
            var c = _completion;
            if (c == null || TopLevel.GetTopLevel(this) == null) return;
            if (_complPopup == null)
            {
                _complRows = new StackPanel { Orientation = Orientation.Vertical };
                var frame = new Border { Child = _complRows, BorderThickness = new Thickness(1), Padding = new Thickness(2), MinWidth = 160 };
                AutomationProperties.SetName(frame, "Completions");
                _complPopup = new Popup
                {
                    PlacementTarget = this,
                    Placement = PlacementMode.Bottom,
                    IsLightDismissEnabled = false,
                    Child = frame,
                };
                AutomationProperties.SetName(_complPopup, "Completions");
                ((ISetLogicalParent)_complPopup).SetParent(this);
                LogicalChildren.Add(_complPopup);
            }
            var border = (Border)_complPopup.Child!;
            border.Background = Background;
            border.BorderBrush = LayoutForeground;
            var rows = _complRows!;
            rows.Children.Clear();
            var selection = ResolveSelectionBrush();
            for (int i = c.Top; i < c.Items.Count && i < c.Top + CompletionRows; i++)
            {
                var item = c.Items[i];
                int index = i;
                var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                line.Children.Add(new TextBlock { Text = item.Label, Foreground = LayoutForeground });
                if (!string.IsNullOrEmpty(item.Detail)) line.Children.Add(new TextBlock { Text = item.Detail, Foreground = Brushes.Gray });
                var row = new Border { Child = line, Padding = new Thickness(4, 1), Background = i == c.Index ? selection : Brushes.Transparent };
                AutomationProperties.SetName(row, item.Label + (string.IsNullOrEmpty(item.Detail) ? string.Empty : ", " + item.Detail));
                if (i == c.Index) AutomationProperties.SetItemStatus(row, "selected");
                row.PointerPressed += (o, e) =>
                {
                    if (_completion == null) return;
                    _completion.Index = index;
                    AcceptCompletion();
                    e.Handled = true;
                };
                rows.Children.Add(row);
            }
            _complPopup.PlacementRect = CaretRect();
            try { _complPopup.IsOpen = true; }
            catch (InvalidOperationException e)
            {
                // A window with no theme has no overlay layer, and a platform with no popup windows has nowhere else: the state and the events still work, so a
                // host can show the items itself (CompletionItems, CompletionIndex, CompletionChanged).
                Trace("The completion popup could not be shown: " + e.Message);
            }
        }

        /// <summary>For a test: the names of the rows the popup shows, and whether it is open on screen (it is not when the field is in no window).</summary>
        internal IReadOnlyList<string> CompletionRowNames() =>
            _complRows == null || _completion == null ? new string[0] : _complRows.Children.Select(r => AutomationProperties.GetName(r) ?? string.Empty).ToList();

        internal bool CompletionPopupIsOpen => _complPopup != null && _complPopup.IsOpen;
    }
}
