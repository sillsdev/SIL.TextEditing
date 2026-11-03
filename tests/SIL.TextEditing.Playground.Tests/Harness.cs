using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.TextEditing.Avalonia;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SIL.TextEditing.Playground.Tests
{
    public class TestApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    /// <summary>One headless Avalonia session for the whole test assembly. Every test body runs on its UI thread.</summary>
    public static class Headless
    {
        static readonly Lazy<HeadlessUnitTestSession> Session = new Lazy<HeadlessUnitTestSession>(() => HeadlessUnitTestSession.StartNew(typeof(TestApp)));

        public static int AvaloniaMajor => typeof(Application).Assembly.GetName().Version!.Major;

        public static void Run(Action body) => Session.Value.Dispatch(() => { body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Run(Func<Task> body) => Session.Value.Dispatch(async () => { await body(); return 0; }, CancellationToken.None).GetAwaiter().GetResult();

        public static void Pump() => Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The playground in a headless window, driven the way a person drives it: keys and text input go to the
    /// focused field, buttons are clicked by name, the labels and the inspector are read back as text.</summary>
    public sealed class Session : IDisposable
    {
        readonly Window _window;

        public Session(string scenario = Scenarios.MixedBidi, double width = 1100, double height = 700)
        {
            View = new PlaygroundView(scenario);
            _window = new Window { Width = width, Height = height, Content = View };
            _window.Show();
            Headless.Pump();
            Field.Focus();
            Headless.Pump();
        }

        public PlaygroundView View { get; }
        public RichTextField Field => View.Field;
        public DocumentEditor Editor => Field.Editor;
        public string WsLabel => View.WsLabelText;
        public string Inspector => View.InspectorText;
        public string Log => string.Join("\n", View.EventLog);

        public void Dispose() => _window.Close();

        public T Find<T>(string name) where T : Control =>
            _window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException("No " + typeof(T).Name + " named " + name);

        public void Click(string buttonName)
        {
            Find<Button>(buttonName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Headless.Pump();
        }

        public void Pick(string scenario)
        {
            var picker = Find<ComboBox>(PlaygroundView.Names.ScenarioPicker);
            picker.SelectedItem = scenario;
            Headless.Pump();
            Field.Focus();
            Headless.Pump();
        }

        public void Type(string text)
        {
            Field.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = Field });
            Headless.Pump();
        }

        public void Press(Key key, KeyModifiers modifiers = KeyModifiers.None, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = Field });
                Field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers, Source = Field });
            }
            Headless.Pump();
        }

        public void Caret(int paragraph, int offset)
        {
            Editor.SetCaret(new TextPosition(paragraph, offset));
            Headless.Pump();
        }

        public void Select(int paragraph, int from, int to)
        {
            Editor.SetCaret(new TextPosition(paragraph, from));
            Editor.SetCaret(new TextPosition(paragraph, to), true);
            Headless.Pump();
        }

        public async Task WaitClipboard()
        {
            await View.ClipboardIdle;
            Headless.Pump();
        }

        public string ParagraphText(int index) => Editor.Document.Paragraphs[index].Text;
        public Guid ParagraphId(int index) => Editor.Document.Paragraphs[index].Id;
    }
}
