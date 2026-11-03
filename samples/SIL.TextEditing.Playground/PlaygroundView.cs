using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;

namespace SIL.TextEditing.Playground
{
    /// <summary>
    /// The shared playground: a <see cref="RichTextField"/> (or many), a scenario picker, a writing-system bar, format and
    /// undo buttons, copy and paste, a live inspector of the model and an event log. Both sample hosts and the
    /// integration tests use this one control. Controls are named (see the <c>Names</c> class) so tests and screen
    /// readers can find them.
    /// </summary>
    public sealed class PlaygroundView : UserControl
    {
        /// <summary>The names of the controls a test or a UI automation client looks for.</summary>
        public static class Names
        {
            public const string ScenarioPicker = "ScenarioPicker";
            public const string FieldCountPicker = "FieldCountPicker";
            public const string Bold = "BoldButton";
            public const string Italic = "ItalicButton";
            public const string Underline = "UnderlineButton";
            public const string Undo = "UndoButton";
            public const string Redo = "RedoButton";
            public const string Picture = "PictureButton";
            public const string Cut = "CutButton";
            public const string Copy = "CopyButton";
            public const string Paste = "PasteButton";
            public const string ReadOnly = "ReadOnlyCheck";
            public const string DefaultAnalysis = "DefaultAnalysisButton";
            public const string DefaultVernacular = "DefaultVernacularButton";
            public const string WsLabel = "WsLabel";
            public const string SelectionLabel = "SelectionLabel";
            public const string Inspector = "Inspector";
            public const string EventLog = "EventLog";
            public const string ScenarioDescription = "ScenarioDescription";
            public static string WritingSystem(string tag) => "Ws_" + tag;
        }

        const int MaxLogLines = 300;
        static readonly int[] FieldCounts = { 10, 50, 200, 1000 };

        sealed class Session
        {
            public Session(RichTextField field, int number)
            {
                Field = field;
                Number = number;
            }

            public RichTextField Field { get; }
            public int Number { get; }
            public SimpleUndoStack Undo { get; } = new SimpleUndoStack();
        }

        readonly WritingSystemRegistry _registry = SampleWritingSystems.CreateRegistry();
        readonly SamplePictures _pictures = new SamplePictures();
        readonly List<Session> _sessions = new List<Session>();
        readonly List<string> _log = new List<string>();
        readonly StackPanel _fieldStack = new StackPanel();
        readonly ComboBox _picker;
        readonly ComboBox _countPicker;
        CheckBox _readOnlyCheck = null!;
        readonly TextBlock _description;
        readonly TextBlock _wsLabel;
        readonly TextBlock _selectionLabel;
        readonly TextBox _inspector;
        readonly TextBox _logBox;
        Session _active;
        bool _syncing;
        int _pictureCount;
        string _selectionState = string.Empty;

        public PlaygroundView() : this(Scenarios.MixedBidi) { }

        public PlaygroundView(string scenario)
        {
            _picker = new ComboBox { Name = Names.ScenarioPicker, MinWidth = 220, Margin = new Thickness(2), IsTabStop = false, ItemsSource = Scenarios.All.Select(s => s.Name).ToList() };
            ToolTip.SetTip(_picker, "Load a ready-made document");
            _picker.SelectionChanged += (s, e) =>
            {
                if (!_syncing && _picker.SelectedItem is string name) LoadScenario(name);
            };
            _countPicker = new ComboBox { Name = Names.FieldCountPicker, MinWidth = 80, Margin = new Thickness(2), IsTabStop = false, ItemsSource = FieldCounts, SelectedItem = 50 };
            ToolTip.SetTip(_countPicker, "How many fields the Many fields scenario creates");
            _countPicker.SelectionChanged += (s, e) =>
            {
                if (!_syncing && CurrentScenario.Many) LoadScenario(CurrentScenario.Name);
            };
            _description = new TextBlock { Name = Names.ScenarioDescription, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 2), Foreground = Brushes.DimGray };
            _wsLabel = new TextBlock { Name = Names.WsLabel, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            _selectionLabel = new TextBlock { Name = Names.SelectionLabel, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            _inspector = new TextBox { Name = Names.Inspector, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, IsTabStop = false };
            ScrollViewer.SetHorizontalScrollBarVisibility(_inspector, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_inspector, ScrollBarVisibility.Auto);
            _logBox = new TextBox { Name = Names.EventLog, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 12, IsTabStop = false };
            ScrollViewer.SetHorizontalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_logBox, ScrollBarVisibility.Auto);

            Content = BuildLayout();
            AddHandler(InputElement.KeyDownEvent, OnPreviewKey, RoutingStrategies.Tunnel);

            _active = null!;
            LoadScenario(scenario);
            AttachedToVisualTree += (s, e) => Dispatcher.UIThread.Post(() => _active.Field.Focus());
        }

        // ------------------------------------------------------------ what a test or a host can read

        public Scenario CurrentScenario { get; private set; } = Scenarios.All[0];
        /// <summary>The field the toolbar, writing-system bar and inspector act on: the focused one.</summary>
        public RichTextField Field => _active.Field;
        public IReadOnlyList<RichTextField> Fields => _sessions.Select(s => s.Field).ToList();
        /// <summary>The host-owned undo stack of the active field.</summary>
        public SimpleUndoStack Undo => _active.Undo;
        public string WsLabelText => _wsLabel.Text ?? string.Empty;
        public string InspectorText => _inspector.Text ?? string.Empty;
        public IReadOnlyList<string> EventLog => _log;
        /// <summary>Completes when the last copy, cut or paste started from a button has finished.</summary>
        public Task ClipboardIdle { get; private set; } = Task.CompletedTask;
        public int FieldCount => (int)(_countPicker.SelectedItem ?? 50);

        // ------------------------------------------------------------ layout

        Control BuildLayout()
        {
            var toolbar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            toolbar.Children.Add(Label("Scenario:"));
            toolbar.Children.Add(_picker);
            toolbar.Children.Add(Label("Fields:"));
            toolbar.Children.Add(_countPicker);
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Bold, new TextBlock { Text = "B", FontWeight = FontWeight.Bold }, "Bold on the selection (Ctrl+B)", () => ToggleBold()));
            toolbar.Children.Add(Tool(Names.Italic, new TextBlock { Text = "I", FontStyle = FontStyle.Italic }, "Italic on the selection (Ctrl+I)", () => ToggleItalic()));
            toolbar.Children.Add(Tool(Names.Underline, new TextBlock { Text = "U", TextDecorations = TextDecorations.Underline }, "Underline on the selection (Ctrl+U)", () => ToggleUnderline()));
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Undo, "Undo", "Ask the host's undo stack to undo (Ctrl+Z)", () => UndoLast("button")));
            toolbar.Children.Add(Tool(Names.Redo, "Redo", "Redo (Ctrl+Y)", () => RedoLast("button")));
            toolbar.Children.Add(Spacer());
            toolbar.Children.Add(Tool(Names.Picture, "Picture", "Insert an inline picture object at the caret", () => InsertPicture()));
            toolbar.Children.Add(Tool(Names.Cut, "Cut", "Cut the selection (Ctrl+X)", () => ClipboardIdle = CopyAsync(true)));
            toolbar.Children.Add(Tool(Names.Copy, "Copy", "Copy the selection (Ctrl+C)", () => ClipboardIdle = CopyAsync(false)));
            toolbar.Children.Add(Tool(Names.Paste, "Paste", "Paste (Ctrl+V)", () => ClipboardIdle = PasteAsync()));
            toolbar.Children.Add(Spacer());
            _readOnlyCheck = new CheckBox { Name = Names.ReadOnly, Content = "Read-only field", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_readOnlyCheck, "Field-level read-only: selection and copy work, nothing changes the text");
            _readOnlyCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _active.Field.IsReadOnly = _readOnlyCheck.IsChecked == true;
                Log("field read-only: " + (_active.Field.IsReadOnly ? "on" : "off"));
            };
            toolbar.Children.Add(_readOnlyCheck);

            var wsBar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            wsBar.Children.Add(_wsLabel);
            wsBar.Children.Add(Tool(Names.DefaultAnalysis, "Default analysis (" + SampleWritingSystems.DefaultAnalysis + ")", "Use the default analysis writing system", () => SetWritingSystem(SampleWritingSystems.DefaultAnalysis)));
            wsBar.Children.Add(Tool(Names.DefaultVernacular, "Default vernacular (" + SampleWritingSystems.DefaultVernacular + ")", "Use the default vernacular writing system", () => SetWritingSystem(SampleWritingSystems.DefaultVernacular)));
            foreach (var vernacular in new[] { false, true })
            {
                wsBar.Children.Add(Label(vernacular ? "Vernacular:" : "Analysis:"));
                foreach (var ws in SampleWritingSystems.All.Where(w => w.Vernacular == vernacular))
                {
                    var tag = ws.Tag;
                    wsBar.Children.Add(Tool(Names.WritingSystem(tag), tag, ws.Name + " (" + ws.Kind + "): sets the selection, or what you type next", () => SetWritingSystem(tag)));
                }
            }
            wsBar.Children.Add(_selectionLabel);

            var fieldScroll = new ScrollViewer
            {
                Content = _fieldStack,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            var fieldBorder = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = fieldScroll };

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition(2, GridUnitType.Star));
            right.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            right.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            var splitter = new GridSplitter { Height = 5, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Rows, HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(_inspector, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(_logBox, 2);
            right.Children.Add(_inspector);
            right.Children.Add(splitter);
            right.Children.Add(_logBox);
            var rightBorder = new Border { Margin = new Thickness(4), Child = right };

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition(3, GridUnitType.Star) { MinWidth = 200 });
            body.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            body.ColumnDefinitions.Add(new ColumnDefinition(2, GridUnitType.Star) { MinWidth = 150 });
            var vsplit = new GridSplitter { Width = 5, Background = Brushes.LightGray, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(fieldBorder, 0);
            Grid.SetColumn(vsplit, 1);
            Grid.SetColumn(rightBorder, 2);
            body.Children.Add(fieldBorder);
            body.Children.Add(vsplit);
            body.Children.Add(rightBorder);

            var top = new StackPanel();
            top.Children.Add(toolbar);
            top.Children.Add(_description);
            top.Children.Add(wsBar);
            var root = new DockPanel { Background = Brushes.WhiteSmoke };
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(body);
            return root;
        }

        static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 2, 2) };

        static Border Spacer() => new Border { Width = 12 };

        static Button Tool(string name, object content, string tip, Action click)
        {
            var button = new Button { Name = name, Content = content, Focusable = false, Margin = new Thickness(2), Padding = new Thickness(8, 3) };
            ToolTip.SetTip(button, tip);
            button.Click += (s, e) => click();
            return button;
        }

        // ------------------------------------------------------------ scenarios

        public void LoadScenario(string name)
        {
            var scenario = Scenarios.Find(name);
            CurrentScenario = scenario;
            _syncing = true;
            _picker.SelectedItem = scenario.Name;
            _syncing = false;
            _description.Text = scenario.Description;
            _countPicker.IsEnabled = scenario.Many;

            _sessions.Clear();
            _fieldStack.Children.Clear();
            var documents = scenario.Build(FieldCount);
            for (int i = 0; i < documents.Count; i++) AddSession(documents[i], i + 1, scenario.Many);
            _active = _sessions[0];
            _syncing = true;
            _readOnlyCheck.IsChecked = false;
            _syncing = false;
            _selectionState = string.Empty;
            Log("scenario: " + scenario.Name + (scenario.Many ? " (" + documents.Count + " fields)" : string.Empty));
            UpdateWsLabel();
            RefreshInspector();
            if (TopLevel.GetTopLevel(this) != null) Dispatcher.UIThread.Post(() => _active.Field.Focus());
        }

        void AddSession(Document document, int number, bool many)
        {
            var field = new RichTextField(document)
            {
                WritingSystems = _registry,
                ObjectImage = _pictures.Get,
                ShowCaretAlways = !many,
                PreferredWidth = many ? 300 : 500,
                Margin = new Thickness(many ? 2 : 6),
            };
            global::Avalonia.Automation.AutomationProperties.SetName(field, many ? "Text field " + number.ToString(CultureInfo.InvariantCulture) : "Text field");
            var session = new Session(field, number);
            session.Undo.Attach(field.Editor);
            _sessions.Add(session);

            field.ActiveWsChanged += (o, n) =>
            {
                if (_active != session) return;
                UpdateWsLabel();
                Log("caret ws " + o + " -> " + n + ": would switch keyboard to " + n);
            };
            field.UndoRequested += () => { Activate(session); UndoLast("Ctrl+Z in the field"); };
            field.RedoRequested += () => { Activate(session); RedoLast("Ctrl+Y in the field"); };
            field.SelectionWsState += state =>
            {
                if (_active != session) return;
                _selectionState = state;
                _selectionLabel.Text = "selection ws: " + state;
            };
            field.Editor.Committed += d =>
            {
                if (_active == session && d.Label != "type") Log("edit: " + d.Label + " (paragraphs " + d.Start + ".." + (d.Start + Math.Max(d.Before.Count, d.After.Count) - 1) + ")");
            };
            field.Editor.UndoGroupEnded += id => { if (_active == session) Log("undo step " + id + " ended: the host closes its undo task"); };
            field.Editor.DocumentChanged += (a, b, c) => { if (_active == session) RefreshInspector(); };
            field.Editor.CaretMoved += () => { if (_active == session) { UpdateWsLabel(); RefreshInspector(); } };
            field.GotFocus += (s, e) => Activate(session);

            if (many)
            {
                var label = new TextBlock { Text = number.ToString(CultureInfo.InvariantCulture), Width = 36, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(4, 0) };
                var row = new DockPanel { Margin = new Thickness(2) };
                DockPanel.SetDock(label, Dock.Left);
                row.Children.Add(label);
                row.Children.Add(new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = field });
                _fieldStack.Children.Add(row);
            }
            else _fieldStack.Children.Add(field);
        }

        void Activate(Session session)
        {
            if (_active == session) return;
            _active = session;
            _selectionState = string.Empty;
            _selectionLabel.Text = string.Empty;
            _syncing = true;
            _readOnlyCheck.IsChecked = session.Field.IsReadOnly;
            _syncing = false;
            Log("focus: field " + session.Number);
            UpdateWsLabel();
            RefreshInspector();
        }

        // ------------------------------------------------------------ label, inspector, log

        void UpdateWsLabel()
        {
            var tag = _active.Field.Editor.ActiveWs;
            var ws = SampleWritingSystems.Find(tag);
            _wsLabel.Text = "ws: " + tag + (ws != null ? " - " + ws.Name + " (" + ws.Kind + ")" : string.Empty);
        }

        void RefreshInspector() => _inspector.Text = Inspector.Describe(_active.Field.Editor, _active.Undo);

        void Log(string message)
        {
            _log.Add(message);
            if (_log.Count > MaxLogLines) _log.RemoveRange(0, _log.Count - MaxLogLines);
            _logBox.Text = string.Join("\n", _log);
            _logBox.CaretIndex = _logBox.Text.Length;
        }

        // ------------------------------------------------------------ keys

        void OnPreviewKey(object? sender, KeyEventArgs e)
        {
            if (!(e.Source is RichTextField) || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
            switch (e.Key)
            {
                case Key.B: ToggleBold(); e.Handled = true; break;
                case Key.I: ToggleItalic(); e.Handled = true; break;
                case Key.U: ToggleUnderline(); e.Handled = true; break;
                case Key.C: Log("clipboard: Ctrl+C, copy through the library"); break;
                case Key.X: Log("clipboard: Ctrl+X, cut through the library"); break;
                case Key.V: Log("clipboard: Ctrl+V, paste through the library"); break;
            }
        }

        // ------------------------------------------------------------ undo and redo

        public bool UndoLast(string source = "button")
        {
            var undo = _active.Undo;
            string label = undo.Done.Count > 0 ? undo.Done[undo.Done.Count - 1].Label : string.Empty;
            bool ok = undo.Undo();
            Log("undo requested (" + source + "): " + (ok ? "undid " + label : "nothing to undo"));
            RefreshInspector();
            return ok;
        }

        public bool RedoLast(string source = "button")
        {
            var undo = _active.Undo;
            string label = undo.Undone.Count > 0 ? undo.Undone[undo.Undone.Count - 1].Label : string.Empty;
            bool ok = undo.Redo();
            Log("redo requested (" + source + "): " + (ok ? "redid " + label : "nothing to redo"));
            RefreshInspector();
            return ok;
        }

        // ------------------------------------------------------------ formatting and writing system

        public bool ToggleBold() => Toggle("bold", TextPropertyNumbers.Bold);
        public bool ToggleItalic() => Toggle("italic", TextPropertyNumbers.Italic);
        public bool ToggleUnderline() => Toggle("underline", TextPropertyNumbers.Underline);

        bool Toggle(string label, int property)
        {
            return FormatSelection(label, runs =>
            {
                bool allOn = runs.All(r => r.Props.TryGetInt(property, out var v) && v == TextPropertyNumbers.ToggleOn);
                Log("format: " + label + (allOn ? " off" : " on"));
                return r =>
                {
                    if (allOn) r.Props.IntProps.Remove(property);
                    else r.Props.SetInt(property, TextPropertyNumbers.ToggleOn);
                };
            });
        }

        /// <summary>A writing-system button: with a selection it changes the writing system of the selected text; with
        /// a caret it sets the writing system of the next text typed (what a keyboard switch does).</summary>
        public void SetWritingSystem(string tag)
        {
            var editor = _active.Field.Editor;
            if (editor.HasSelection) FormatSelection("writing system " + tag, runs => r => r.Ws = tag);
            else editor.SetActiveWs(tag);
        }

        /// <summary>
        /// Changes every non-object run in the selection and pushes the change on the host-owned undo stack: the
        /// editor has no formatting command, so the playground plays the host. The edit is an EditDelta applied with
        /// <c>Apply</c>, so paragraph ids are kept.
        /// </summary>
        bool FormatSelection(string label, Func<List<Run>, Action<Run>> plan)
        {
            var session = _active;
            var editor = session.Field.Editor;
            if (!editor.HasSelection)
            {
                Log("format: " + label + " needs a selection");
                return false;
            }
            if (!editor.CanFormatSelection)
            {
                Log("format: " + label + " refused, the selection holds text that may not be edited");
                return false;
            }
            editor.Ordered(out var a, out var b);
            var before = new List<Paragraph>();
            var after = new List<Paragraph>();
            var targets = new List<Run>();
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var original = editor.Document.Paragraphs[pi];
                var edited = original.Clone();
                int start = pi == a.Paragraph ? a.Offset : 0;
                int end = pi == b.Paragraph ? b.Offset : edited.Length;
                int first = edited.SplitRunAt(start);
                int last = edited.SplitRunAt(end);
                for (int i = first; i < last; i++)
                    if (!edited.Runs[i].IsObject) targets.Add(edited.Runs[i]);
                before.Add(original.Clone());
                after.Add(edited);
            }
            if (targets.Count == 0)
            {
                Log("format: " + label + " has nothing to change in the selection");
                return false;
            }
            var change = plan(targets);
            foreach (var run in targets) change(run);
            foreach (var p in after) p.Normalize();

            var delta = new EditDelta { Start = a.Paragraph, Before = before, After = after, CaretBefore = editor.Caret, CaretAfter = b, Label = label };
            session.Undo.Done.Add(delta);
            session.Undo.Undone.Clear();
            editor.Apply(delta);
            editor.SetCaret(a);
            editor.SetCaret(b, true);
            return true;
        }

        // ------------------------------------------------------------ pictures and clipboard

        /// <summary>Inserts an inline picture object (an object run) at the caret, replacing the selection.</summary>
        public void InsertPicture()
        {
            var editor = _active.Field.Editor;
            var fragment = new Paragraph { DefaultWs = editor.ActiveWs };
            fragment.Runs.Add(Run.Object(editor.ActiveWs, (_pictureCount++ % 2 == 0 ? Scenarios.RedPicture : Scenarios.BluePicture)));
            editor.Paste(new List<Paragraph> { fragment });
            Log("picture inserted at " + editor.Caret);
        }

        public async Task CopyAsync(bool cut)
        {
            var field = _active.Field;
            if (!field.Editor.HasSelection)
            {
                Log("clipboard: " + (cut ? "cut" : "copy") + " ignored, nothing selected");
                return;
            }
            var fragment = field.Editor.GetFragment();
            int chars = fragment.Sum(p => p.Length);
            try
            {
                await field.CopyAsync(cut);
                Log("clipboard: " + (cut ? "cut " : "copied ") + fragment.Count + " paragraph(s), " + chars + " chars, as " + Interchange.ClipboardFormat + " and plain text");
            }
            catch (Exception ex)
            {
                Log("clipboard: " + (cut ? "cut" : "copy") + " failed: " + ex.Message);
            }
        }

        public async Task PasteAsync()
        {
            var field = _active.Field;
            var before = field.Editor.Document.Paragraphs.Count;
            try
            {
                await field.PasteAsync();
                Log("clipboard: pasted, paragraphs " + before + " -> " + field.Editor.Document.Paragraphs.Count + ", caret " + field.Editor.Caret);
            }
            catch (Exception ex)
            {
                Log("clipboard: paste failed: " + ex.Message);
            }
        }
    }
}
