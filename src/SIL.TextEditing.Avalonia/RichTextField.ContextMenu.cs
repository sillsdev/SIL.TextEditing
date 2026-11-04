using Avalonia;

namespace SIL.TextEditing.Avalonia
{
    // Context-menu information (ADR 0021, ADR 0025). The field rides on Avalonia's own context request (a right click, the Menu
    // key, a long press), so one request gives one event, whatever raised it; Avalonia does not raise one for Shift+F10 on either
    // line, so the field raises that one itself. A host builds its menu from this event or sets Avalonia's ContextMenu on the
    // field, not both.
    public partial class RichTextField
    {
        /// <summary>A context menu was asked for: the host builds it from <see cref="ContextMenuRequestedEventArgs.Info"/> (the word
        /// and its range, its writing system, the run's properties, any link or embedded object, the selection, and a list for
        /// spelling suggestions that nothing fills yet) and shows it at <see cref="ContextMenuRequestedEventArgs.Position"/>. A
        /// right click inside a selection has kept it and one outside has moved the caret there first, as in FieldWorks. A host
        /// builds its menu from this event or sets <c>ContextMenu</c> on the field, not both.</summary>
        public event System.EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

        void HookContextRequests()
        {
            ContextRequested += (sender, e) =>
            {
                bool pointer = e.TryGetPosition(this, out var at);
                if (ContextMenuRequested == null) return;
                e.Handled = true;
                RaiseContextMenuRequested(pointer, at);
            };
        }

        /// <summary>Raises <see cref="ContextMenuRequested"/>: for the character under the pointer, or for the caret.</summary>
        void RaiseContextMenuRequested(bool pointer, Point at)
        {
            var handler = ContextMenuRequested;
            if (handler == null) return;
            if (pointer)
                handler(this, new ContextMenuRequestedEventArgs(ContextMenuTrigger.Pointer, at, Editor.DescribeContext(Locate(at).Character, false)));
            else
            {
                var caret = CaretRect();
                handler(this, new ContextMenuRequestedEventArgs(ContextMenuTrigger.Keyboard, new Point(caret.X, caret.Bottom), Editor.DescribeContext(Editor.Caret, true)));
            }
        }
    }
}
