using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Samples
{
    public class SampleApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    /// <summary>The layout both sample hosts show: a TextBox, a writing-system label, the editor and another TextBox.
    /// The Tab order TextBox, editor, TextBox is the child order of the grid.</summary>
    public sealed class SampleUi
    {
        public SampleUi()
        {
            var document = new Document();
            document.Paragraphs.Add(Paragraph("en", "en", "Hello ", "fr", "bonjour", "en", " world"));
            document.Paragraphs.Add(Paragraph("ar", "en", "Mixed: ", "ar", "مرحبا بالعالم", "en", " end"));

            Field = new RichTextField(document) { ObjectImage = Picture, ShowCaretAlways = true };
            Undo.Attach(Field.Editor);
            Field.UndoRequested += () => Undo.Undo();
            Field.RedoRequested += () => Undo.Redo();

            var before = new TextBox { Text = "first Avalonia TextBox", Margin = new Thickness(4) };
            var after = new TextBox { Text = "second Avalonia TextBox", Margin = new Thickness(4) };
            var wsLabel = new TextBlock { Text = "ws: " + Field.Editor.ActiveWs, Margin = new Thickness(6, 2) };
            var stateLabel = new TextBlock { Margin = new Thickness(6, 2), Foreground = Brushes.Gray };
            Field.ActiveWsChanged += (o, n) => wsLabel.Text = "ws: " + n;
            Field.SelectionWsState += s => stateLabel.Text = "selection: " + s;

            var menu = new ContextMenu();
            foreach (var ws in new[] { "en", "fr", "ar", "ta", "hi", "th" })
            {
                var tag = ws;
                var item = new MenuItem { Header = "Writing system: " + tag };
                item.Click += (s, e) => Field.Editor.SetActiveWs(tag);
                menu.Items.Add(item);
            }
            Field.ContextMenu = menu;

            var scroll = new ScrollViewer
            {
                Content = Field,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var top = new StackPanel();
            top.Children.Add(before);
            top.Children.Add(wsLabel);
            top.Children.Add(stateLabel);
            var middle = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = scroll };
            var grid = new Grid { Background = Brushes.WhiteSmoke };
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetRow(top, 0);
            Grid.SetRow(middle, 1);
            Grid.SetRow(after, 2);
            grid.Children.Add(top);
            grid.Children.Add(middle);
            grid.Children.Add(after);
            Root = grid;
        }

        public RichTextField Field { get; }
        public SimpleUndoStack Undo { get; } = new SimpleUndoStack();
        public Control Root { get; }

        static Paragraph Paragraph(string defaultWs, params string[] wsAndText)
        {
            var p = new Paragraph { DefaultWs = defaultWs, Rtl = defaultWs == "ar" };
            for (int i = 0; i < wsAndText.Length; i += 2)
                p.Runs.Add(new Run { Ws = wsAndText[i], Text = wsAndText[i + 1] });
            return p;
        }

        /// <summary>A little bitmap standing in for a picture object.</summary>
        static IImage Picture(string data)
        {
            var bitmap = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = bitmap.Lock())
            {
                var px = new int[32 * 24];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = (x < 2 || y < 2 || x > 29 || y > 21) ? unchecked((int)0xFF202020) : (x + y) % 8 < 4 ? unchecked((int)0xFFD04040) : unchecked((int)0xFF4060D0);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return bitmap;
        }
    }
}
