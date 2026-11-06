using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Playground
{
    // The selection bar: a small toolbar that floats above the selection while there is one, the way word processors show their mini toolbar.
    // The first row is the simple set (bold, italic, underline, writing system, character style); "More" opens the second row with the rest
    // (paragraph style, a picture). It is the host's control, built on Avalonia's Popup: the field only says where the caret is (CaretRect).
    public sealed partial class PlaygroundView
    {
        Popup _selectionBar = null!;
        StackPanel _selectionMore = null!;
        ComboBox _selectionWs = null!;
        bool _selectionBarShown;

        /// <summary>True while the selection bar is shown over the active field's selection.</summary>
        public bool SelectionBarOpen => _selectionBarShown;

        /// <summary>Whether the bar appears over a selection (default true).</summary>
        public bool SelectionBarEnabled
        {
            get => _selectionBarEnabled;
            set
            {
                _selectionBarEnabled = value;
                UpdateSelectionBar();
            }
        }

        bool _selectionBarEnabled = true;

        /// <summary>True while the bar's second row (paragraph style, picture) is open.</summary>
        public bool SelectionBarExpanded => _selectionMore.IsVisible;

        Popup BuildSelectionBar()
        {
            var simple = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            simple.Children.Add(BarTool(Names.BarBold, new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, "Bold (Ctrl+B)", () => ToggleBold()));
            simple.Children.Add(BarTool(Names.BarItalic, new TextBlock { Text = "I", FontStyle = FontStyle.Italic }, "Italic (Ctrl+I)", () => ToggleItalic()));
            simple.Children.Add(BarTool(Names.BarUnderline, new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, "Underline (Ctrl+U)", () => ToggleUnderline()));
            _selectionWs = new ComboBox
            {
                Name = Names.BarWritingSystem,
                MinWidth = 120,
                IsTabStop = false,
                ItemsSource = SampleWritingSystems.All.Select(w => w.Tag + "  " + w.Name).ToList(),
            };
            ToolTip.SetTip(_selectionWs, "Writing system of the selection");
            _selectionWs.SelectionChanged += (s, e) =>
            {
                if (_syncing || _selectionWs.SelectedIndex < 0) return;
                SetWritingSystem(SampleWritingSystems.All[_selectionWs.SelectedIndex].Tag);
                _active.Field.Focus();
            };
            simple.Children.Add(_selectionWs);
            simple.Children.Add(StyleBox(Names.BarCharStyle, SampleStyles.CharacterStyles, "Character style", name => ApplyCharacterStyle(name), "Style"));
            var more = new ToggleButton { Name = Names.BarMore, Content = "More", Focusable = false, Padding = new Thickness(8, 3) };
            ToolTip.SetTip(more, "More: paragraph style and pictures");
            simple.Children.Add(more);

            _selectionMore = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
            _selectionMore.Children.Add(StyleBox(Names.BarParaStyle, SampleStyles.ParagraphStyles, "Paragraph style of the selected paragraphs", name => ApplyParagraphStyle(name), "Paragraph style"));
            _selectionMore.Children.Add(BarTool(Names.BarPicture, "Picture", "Put a picture in place of the selection", () => InsertPicture()));
            more.IsCheckedChanged += (s, e) => _selectionMore.IsVisible = more.IsChecked == true;

            var rows = new StackPanel();
            rows.Children.Add(simple);
            rows.Children.Add(_selectionMore);
            var frame = new Border
            {
                Child = rows,
                Background = Brushes.White,
                BorderBrush = Brushes.Silver,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 2, Blur = 8, Color = Color.FromArgb(60, 0, 0, 0) }),
            };
            global::Avalonia.Automation.AutomationProperties.SetName(frame, "Formatting");
            var popup = new Popup
            {
                Name = Names.SelectionBar,
                // Above the caret's line, growing up and to the right: the bar's bottom-left corner sits on the top-left of the caret.
                Placement = PlacementMode.AnchorAndGravity,
                PlacementAnchor = PopupAnchor.TopLeft,
                PlacementGravity = PopupGravity.TopRight,
                IsLightDismissEnabled = false,
                Child = frame,
                VerticalOffset = -4,
            };
            ((ISetLogicalParent)popup).SetParent(this);
            LogicalChildren.Add(popup);
            return popup;
        }

        Button BarTool(string name, object content, string tip, System.Action click)
        {
            var button = Tool(name, content, tip, click);
            button.Margin = new Thickness(0);
            return button;
        }

        /// <summary>Hides the bar until the selection changes again: a context menu replaces it.</summary>
        void HideSelectionBar()
        {
            _selectionBar.IsOpen = false;
            _selectionBarShown = false;
        }

        /// <summary>Shows the bar over the active field's caret while it has a selection, and hides it otherwise. Called when the caret moves, the
        /// document changes or another field becomes active; it waits for layout so the caret's place is current.</summary>
        void UpdateSelectionBar()
        {
            var field = _active?.Field;
            bool show = _selectionBarEnabled && field != null && field.Editor.HasSelection && !field.IsReadOnly && TopLevel.GetTopLevel(this) != null;
            if (!show)
            {
                if (_selectionBarShown) _selectionBar.IsOpen = false;
                _selectionBarShown = false;
                return;
            }
            _syncing = true;
            int ws = SampleWritingSystems.All.ToList().FindIndex(w => w.Tag == field!.Editor.SelectionWs);
            _selectionWs.SelectedIndex = ws;
            _syncing = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_selectionBarEnabled || _active?.Field != field || !field!.Editor.HasSelection) return;
                _selectionBar.PlacementTarget = field;
                _selectionBar.PlacementRect = field.CaretRect();
                try { _selectionBar.IsOpen = true; }
                catch (System.InvalidOperationException) { return; }   // no overlay layer (a window with no theme): the bar is not shown
                _selectionBarShown = true;
            }, DispatcherPriority.Background);
            _selectionBarShown = true;
        }
    }
}
