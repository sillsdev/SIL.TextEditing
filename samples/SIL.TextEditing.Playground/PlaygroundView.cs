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
            public const string CharStyle = "CharStyleBox";
            public const string ParaStyle = "ParaStyleBox";
            public const string Undo = "UndoButton";
            public const string Redo = "RedoButton";
            public const string Picture = "PictureButton";
            public const string Cut = "CutButton";
            public const string Copy = "CopyButton";
            public const string Paste = "PasteButton";
            public const string ReadOnly = "ReadOnlyCheck";
            public const string SingleParagraph = "SingleParagraphCheck";
            public const string HostHandlesLeave = "HostHandlesLeaveCheck";
            public const string SpellCheck = "SpellCheckBox";
            public const string SpellShow = "SpellShowButton";
            public const string SpellReplace = "SpellReplaceButton";
            public const string SpellSpaces = "SpellSpacesButton";
            public const string SpellWholeFirst = "SpellWholeFirstButton";
            public const string SpellWholeLast = "SpellWholeLastButton";
            public const string SpellAdd = "SpellAddButton";
            public const string SpellIgnore = "SpellIgnoreButton";
            public const string HyphenLetter = "HyphenLetterBox";
            public const string SpellBackground = "SpellBackgroundBox";
            public const string DefaultAnalysis = "DefaultAnalysisButton";
            public const string DefaultVernacular = "DefaultVernacularButton";
            public const string WsLabel = "WsLabel";
            public const string SelectionLabel = "SelectionLabel";
            public const string Inspector = "Inspector";
            public const string EventLog = "EventLog";
            public const string ScenarioDescription = "ScenarioDescription";
            public const string FindText = "FindText";
            public const string ReplaceText = "ReplaceText";
            public const string MatchCase = "MatchCaseCheck";
            public const string MatchDiacritics = "MatchDiacriticsCheck";
            public const string WholeWord = "WholeWordCheck";
            public const string RegularExpression = "RegularExpressionCheck";
            public const string HighlightAll = "HighlightAllCheck";
            public const string FindNext = "FindNextButton";
            public const string FindPrevious = "FindPreviousButton";
            public const string Replace = "ReplaceButton";
            public const string ReplaceAll = "ReplaceAllButton";
            public const string FindStatus = "FindStatus";
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
        CheckBox _singleParagraphCheck = null!;
        CheckBox _leaveHandled = null!;
        CheckBox _spellCheck = null!;
        SampleSpelling? _spelling;
        CheckBox _hyphenLetter = null!;
        CheckBox _spellBackground = null!;
        bool _spellOffUiThread;
        TextBox _findBox = null!;
        TextBox _replaceBox = null!;
        CheckBox _matchCase = null!;
        CheckBox _matchDiacritics = null!;
        CheckBox _wholeWord = null!;
        CheckBox _regex = null!;
        CheckBox _highlightAll = null!;
        TextBlock _findStatus = null!;
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

        /// <summary>The playground's dictionaries while spell checking is on, else null.</summary>
        public SampleSpelling? Spelling => _spelling;
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
            toolbar.Children.Add(Label("Character style:"));
            toolbar.Children.Add(StyleBox(Names.CharStyle, SampleStyles.CharacterStyles, "Apply a character style to the selection (the host's stylesheet decides how it looks)", name => ApplyCharacterStyle(name)));
            toolbar.Children.Add(Label("Paragraph style:"));
            toolbar.Children.Add(StyleBox(Names.ParaStyle, SampleStyles.ParagraphStyles, "Apply a paragraph style to the paragraphs of the selection", name => ApplyParagraphStyle(name)));
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
            _singleParagraphCheck = new CheckBox { Name = Names.SingleParagraph, Content = "Single paragraph", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_singleParagraphCheck, "Single-paragraph mode: Enter goes to the host instead of splitting the paragraph");
            _singleParagraphCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _active.Field.SingleParagraph = _singleParagraphCheck.IsChecked == true;
                Log("single paragraph: " + (_active.Field.SingleParagraph ? "on" : "off"));
            };
            toolbar.Children.Add(_singleParagraphCheck);
            _leaveHandled = new CheckBox { Name = Names.HostHandlesLeave, Content = "Host takes leave requests", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_leaveHandled, "Tab, Shift+Tab, Ctrl+Tab and arrows at the field's edge raise LeaveRequested; when ticked the host marks it handled, so Tab does not move focus");
            toolbar.Children.Add(_leaveHandled);
            toolbar.Children.Add(Spacer());
            _spellCheck = new CheckBox { Name = Names.SpellCheck, Content = "Spell checking", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_spellCheck, "Give the fields the playground's English and French word lists as the host's dictionaries; the field checks the paragraphs in view when idle");
            _spellCheck.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                SetSpelling(_spellCheck.IsChecked == true);
            };
            toolbar.Children.Add(_spellCheck);
            _hyphenLetter = new CheckBox { Name = Names.HyphenLetter, Content = "Hyphen is a letter", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_hyphenLetter, "The host's word rule (RichTextField.WordRules): a writing system that makes the hyphen a letter makes well-known one word for a double-click, Ctrl+Left and Right, find and the spell checker");
            _hyphenLetter.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                SetWordRules(_hyphenLetter.IsChecked == true);
            };
            toolbar.Children.Add(_hyphenLetter);
            _spellBackground = new CheckBox { Name = Names.SpellBackground, Content = "Slow dictionary, off the UI thread", Focusable = false, Margin = new Thickness(2) };
            ToolTip.SetTip(_spellBackground, "The playground's dictionaries take 15 ms a word and say they are safe off the UI thread (IBackgroundSpellChecker): the field finds the words, a worker asks them and the squiggles arrive without holding up typing");
            _spellBackground.IsCheckedChanged += (s, e) =>
            {
                if (_syncing) return;
                _spellOffUiThread = _spellBackground.IsChecked == true;
                if (_spelling != null)
                {
                    _spelling.OffUiThread = _spellOffUiThread;
                    _spelling.Delay = _spellOffUiThread ? 15 : 0;
                    _spelling.RaiseDictionariesChanged();       // the fields ask again, now of the other kind of checker
                }
                Log("dictionary: " + (_spellOffUiThread ? "slow, asked off the UI thread" : "asked on the UI thread"));
            };
            toolbar.Children.Add(_spellBackground);
            toolbar.Children.Add(Tool(Names.SpellShow, "Spelling at caret", "What the context menu would offer for the marked word at the caret: the word, why it is marked and the suggestions", () => ShowSpelling()));
            toolbar.Children.Add(Tool(Names.SpellReplace, "Use first suggestion", "Replace the marked word at the caret with the first suggestion (one undo step)", () => ReplaceSpelling()));
            toolbar.Children.Add(Tool(Names.SpellSpaces, "Insert missing spaces", "For a word that mixes writing systems or holds an object: put a space between the parts, each part keeping its own writing system", () => SpacesSpelling()));
            toolbar.Children.Add(Tool(Names.SpellWholeFirst, "Whole word, first writing system", "For a word that mixes writing systems: make the whole word the first writing system in it, as FieldWorks' menu item does", () => WholeWordSpelling(false)));
            toolbar.Children.Add(Tool(Names.SpellWholeLast, "Whole word, last writing system", "For a word that mixes writing systems: make the whole word the last writing system in it", () => WholeWordSpelling(true)));
            toolbar.Children.Add(Tool(Names.SpellAdd, "Add to dictionary", "Add the marked word at the caret to its dictionary", () => AddSpelling(false)));
            toolbar.Children.Add(Tool(Names.SpellIgnore, "Ignore", "Ignore the marked word at the caret", () => AddSpelling(true)));

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

            var findBar = BuildFindBar();

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
            top.Children.Add(findBar);
            var root = new DockPanel { Background = Brushes.WhiteSmoke };
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            root.Children.Add(body);
            return root;
        }

        // ------------------------------------------------------------ find and replace (the host's dialog, ADR 0021)

        Control BuildFindBar()
        {
            var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2) };
            bar.Children.Add(Label("Find:"));
            _findBox = new TextBox { Name = Names.FindText, MinWidth = 150, Margin = new Thickness(2), IsTabStop = false };
            ToolTip.SetTip(_findBox, "The text to find in the focused field");
            _findBox.TextChanged += (s, e) => UpdateFindHighlight();
            bar.Children.Add(_findBox);
            bar.Children.Add(Label("Replace with:"));
            _replaceBox = new TextBox { Name = Names.ReplaceText, MinWidth = 150, Margin = new Thickness(2), IsTabStop = false };
            ToolTip.SetTip(_replaceBox, "The replacement text; a regular expression may use $1");
            bar.Children.Add(_replaceBox);
            CheckBox Option(string name, string text, string tip, bool on)
            {
                var box = new CheckBox { Name = name, Content = text, IsChecked = on, Focusable = false, Margin = new Thickness(2) };
                ToolTip.SetTip(box, tip);
                box.IsCheckedChanged += (s, e) => UpdateFindHighlight();
                bar.Children.Add(box);
                return box;
            }
            _matchCase = Option(Names.MatchCase, "Match case", "Upper and lower case differ (diacritics are then matched too)", false);
            _matchDiacritics = Option(Names.MatchDiacritics, "Match diacritics", "Combining marks must agree; off, they are ignored", true);
            _wholeWord = Option(Names.WholeWord, "Whole word", "A match starts and ends at word boundaries", false);
            _regex = Option(Names.RegularExpression, "Regular expression", "The text is a regular expression", false);
            _highlightAll = Option(Names.HighlightAll, "Highlight all", "Highlight every match, not only the current one", true);
            bar.Children.Add(Tool(Names.FindPrevious, "Find Previous", "Select the previous match", () => DoFind(false)));
            bar.Children.Add(Tool(Names.FindNext, "Find Next", "Select the next match", () => DoFind(true)));
            bar.Children.Add(Tool(Names.Replace, "Replace", "Replace the selected match, then find the next", () => DoReplace()));
            bar.Children.Add(Tool(Names.ReplaceAll, "Replace All", "Replace every match in the field as one undo step", () => DoReplaceAll()));
            _findStatus = new TextBlock { Name = Names.FindStatus, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2) };
            bar.Children.Add(_findStatus);
            return bar;
        }

        FindOptions CurrentFindOptions() => new FindOptions
        {
            Pattern = _findBox.Text ?? string.Empty,
            MatchCase = _matchCase.IsChecked == true,
            MatchDiacritics = _matchDiacritics.IsChecked == true,
            WholeWord = _wholeWord.IsChecked == true,
            UseRegularExpression = _regex.IsChecked == true,
        };

        public string FindStatusText => _findStatus.Text ?? string.Empty;

        void UpdateFindHighlight()
        {
            if (_active == null || _highlightAll == null) return;
            var options = CurrentFindOptions();
            _active.Field.SetFindHighlight(options.Pattern.Length == 0 || !options.IsValid(out _) ? null : options, _highlightAll.IsChecked == true);
        }

        bool FindOptionsUsable(out FindOptions options)
        {
            options = CurrentFindOptions();
            if (!options.IsValid(out var error))
            {
                _findStatus.Text = "Not a valid regular expression: " + error;
                Log("find: " + _findStatus.Text);
                return false;
            }
            if (options.Pattern.Length == 0)
            {
                _findStatus.Text = "Type something to find";
                return false;
            }
            return true;
        }

        /// <summary>Find Next or Find Previous: the host's dialog asks the focused field to search, select and highlight, wrapping
        /// round and saying so, as FieldWorks' dialog does.</summary>
        public bool DoFind(bool forward)
        {
            if (!FindOptionsUsable(out var options)) return false;
            var field = _active.Field;
            UpdateFindHighlight();
            bool found = field.Find(options, forward, true, out bool wrapped);
            _findStatus.Text = found ? (wrapped ? "Wrapped. " : string.Empty) + "Found at " + field.Editor.SelectionRange : "Not found";
            Log("find " + (forward ? "next" : "previous") + ": " + _findStatus.Text);
            return found;
        }

        /// <summary>Replace: replaces the selected match (one undo step), then finds the next, as FieldWorks' dialog does.</summary>
        public bool DoReplace()
        {
            if (!FindOptionsUsable(out var options)) return false;
            var field = _active.Field;
            bool done = field.Editor.ReplaceCurrent(options, _replaceBox.Text ?? string.Empty);
            if (!done)
            {
                // Nothing selected is a match yet (or it may not be edited): select the next one, as Replace does in FieldWorks.
                _findStatus.Text = "Select a match first; found the next one";
                DoFind(true);
                return false;
            }
            Log("replace: replaced the selected match");
            UpdateFindHighlight();
            bool next = field.Find(options, true, true, out bool wrapped);
            _findStatus.Text = "Replaced" + (next ? "; next match at " + field.Editor.SelectionRange : "; no more matches");
            return true;
        }

        /// <summary>Replace All: every match in the focused field, one undo step.</summary>
        public int DoReplaceAll()
        {
            if (!FindOptionsUsable(out var options)) return 0;
            var field = _active.Field;
            int count = field.Editor.ReplaceAll(options, _replaceBox.Text ?? string.Empty);
            _findStatus.Text = count == 0 ? "Not found" : "Replaced " + count + (count == 1 ? " match" : " matches");
            Log("replace all: " + _findStatus.Text);
            UpdateFindHighlight();
            return count;
        }

        // ------------------------------------------------------------ context menu (the host builds it, ADR 0021)

        /// <summary>What the last context-menu request told the host.</summary>
        public ContextInfo? LastContextInfo { get; private set; }
        /// <summary>The captions of the menu the playground built for it.</summary>
        public IReadOnlyList<string> LastContextMenuItems { get; private set; } = new List<string>();
        ContextMenu? _contextMenu;
        /// <summary>Closes the menu the last context-menu request opened, if it is still open.</summary>
        public void CloseContextMenu() => _contextMenu?.Close();


        void ShowContextMenu(Session session, ContextMenuRequestedEventArgs e)
        {
            var info = e.Info;
            LastContextInfo = info;
            var field = session.Field;
            var items = new List<Control>();
            var captions = new List<string>();
            void Add(string caption, bool enabled, Action? click = null)
            {
                var item = new MenuItem { Header = caption, IsEnabled = enabled };
                if (click != null) item.Click += (s, a) => click();
                items.Add(item);
                captions.Add(caption);
            }
            if (info.Word.Length > 0)
            {
                Add("Word: " + info.Word + " (" + info.WritingSystem + ")", false);
                if (info.SpellingSuggestions.Count == 0) Add("(no spelling suggestions)", false);
                foreach (var s in info.SpellingSuggestions) Add(s, true);
                Add("Find \"" + info.Word + "\"", true, () => { _findBox.Text = info.Word; });
            }
            if (info.EmbeddedObject != null)
                Add((info.EmbeddedObject.IsLink ? "Open link" : "Object") + ": " + info.EmbeddedObject.Data.Substring(Math.Min(1, info.EmbeddedObject.Data.Length)), info.EmbeddedObject.IsLink);
            Add("Cut", field.Editor.HasSelection && field.Editor.CanEdit, () => ClipboardIdle = CopyAsync(true));
            Add("Copy", field.Editor.HasSelection, () => ClipboardIdle = CopyAsync(false));
            Add("Paste", field.Editor.CanEdit, () => ClipboardIdle = PasteAsync());
            LastContextMenuItems = captions;
            Log("context menu (" + e.Trigger + ") at " + e.Position.X.ToString("0", CultureInfo.InvariantCulture) + "," + e.Position.Y.ToString("0", CultureInfo.InvariantCulture)
                + ": word " + (info.Word.Length == 0 ? "none" : "\"" + info.Word + "\" " + info.WordRange)
                + ", ws " + info.WritingSystem
                + (info.EmbeddedObject != null ? ", " + (info.EmbeddedObject.IsLink ? "link" : "object") + " at " + info.EmbeddedObject.Range : string.Empty)
                + ", selection " + (info.HasSelection ? info.Selection.ToString() : "none"));
            _contextMenu?.Close();
            _contextMenu = new ContextMenu { ItemsSource = items };
            _contextMenu.Open(field);
        }

        static TextBlock Label(string text) => new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 2, 2, 2) };

        static Border Spacer() => new Border { Width = 12 };

        ComboBox StyleBox(string name, IReadOnlyList<string> styles, string tip, Action<string> apply)
        {
            var box = new ComboBox { Name = name, MinWidth = 140, Margin = new Thickness(2), IsTabStop = false, ItemsSource = styles, SelectedIndex = -1, PlaceholderText = "(choose)" };
            ToolTip.SetTip(box, tip);
            box.SelectionChanged += (s, e) =>
            {
                if (_syncing || !(box.SelectedItem is string style)) return;
                apply(style);
                _syncing = true;
                box.SelectedIndex = -1;
                _syncing = false;
                _active.Field.Focus();
            };
            return box;
        }

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
            _singleParagraphCheck.IsChecked = false;
            _spellCheck.IsChecked = scenario.Name == Scenarios.Spelling;
            _hyphenLetter.IsChecked = false;
            _syncing = false;
            SetSpelling(scenario.Name == Scenarios.Spelling);
            _selectionState = string.Empty;
            Log("scenario: " + scenario.Name + (scenario.Many ? " (" + documents.Count + " fields)" : string.Empty));
            UpdateFindHighlight();
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
                StyleResolver = SampleStyles.Instance,
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
                Log(o == n ? "focus gained, ws " + n + ": would switch keyboard to " + n : "caret ws " + o + " -> " + n + ": would switch keyboard to " + n);
            };
            field.LeaveRequested += (o, e) =>
            {
                if (_active != session) return;
                Log("leave requested: " + e.Direction + " by " + e.Key + (e.ToEnd ? " (to the " + (e.Direction == LeaveDirection.Next ? "last" : "first") + " field)" : string.Empty) + (double.IsNaN(e.GoalX) ? string.Empty : " at x " + Math.Round(e.GoalX)) + (_leaveHandled.IsChecked == true ? ": the host moved focus" : string.Empty));
                e.Handled = _leaveHandled.IsChecked == true;
            };
            field.EnterRequested += m => Log("Enter passed to the host" + (m == KeyModifiers.None ? string.Empty : " (" + m + ")") + ": the host's default button would run");
            field.ContextMenuRequested += (o, e) => { Activate(session); ShowContextMenu(session, e); };
            field.ScrollRequested += (o, e) =>
            {
                // The playground leaves scrolling to Avalonia's scroll viewer (the field asks it when nothing handles the request); it only
                // reports the requests that are not just the caret following typing.
                if (_active == session && e.Reason != ScrollReason.Caret) Log("scroll requested: " + e.Reason + (e.Delta == 0 ? string.Empty : " by " + e.Delta.ToString("0", CultureInfo.InvariantCulture) + " px"));
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
            _singleParagraphCheck.IsChecked = session.Field.SingleParagraph;
            _spellCheck.IsChecked = session.Field.SpellChecking != null;
            _syncing = false;
            Log("focus: field " + session.Number);
            UpdateWsLabel();
            RefreshInspector();
        }

        // ------------------------------------------------------------ spelling

        /// <summary>The host's word rule for every field: with <paramref name="hyphenIsLetter"/> the hyphen forms words, as a writing system that says so would make it.</summary>
        void SetWordRules(bool hyphenIsLetter)
        {
            foreach (var session in _sessions) session.Field.WordRules = hyphenIsLetter ? SampleWordRules.HyphenIsLetter : null;
            Log("word rule: " + (hyphenIsLetter ? "the hyphen is a letter (a double-click, Ctrl+arrows, find and spelling take it)" : "the defaults"));
        }

        void SetSpelling(bool on)
        {
            _spelling = on ? new SampleSpelling(Log) { OffUiThread = _spellOffUiThread, Delay = _spellOffUiThread ? 15 : 0 } : null;
            foreach (var session in _sessions) session.Field.SpellChecking = _spelling;
            Log("spell checking: " + (on ? "on (English and French word lists)" : "off"));
        }

        SpellingSuggestions? SpellingAtCaret(string action)
        {
            var field = _active.Field;
            var at = field.SpellingSuggestionsAt(field.Editor.Caret);
            if (at == null) Log(action + ": no marked word at the caret");
            return at;
        }

        void ShowSpelling()
        {
            var at = SpellingAtCaret("context menu");
            if (at == null) return;
            Log("context menu: '" + at.Word + "' (" + at.Ws + ", " + at.Problem + "): " + (at.All.Count == 0 ? "No suggestions" : string.Join(", ", at.Inline) + (at.Additional.Any() ? " + Additional suggestions: " + string.Join(", ", at.Additional) : string.Empty)) + (at.CanAddToDictionary ? "; Add to dictionary" : "; (Add to dictionary disabled)"));
        }

        void ReplaceSpelling()
        {
            var at = SpellingAtCaret("replace");
            if (at == null) return;
            if (at.All.Count == 0) { Log("replace: no suggestions for '" + at.Word + "'"); return; }
            Log("replace: '" + at.Word + "' -> '" + at.All[0] + "': " + (_active.Field.ApplySpellingSuggestion(at, at.All[0]) ? "done" : "refused"));
        }

        void SpacesSpelling()
        {
            var at = SpellingAtCaret("insert spaces");
            if (at == null) return;
            Log("insert spaces: '" + at.Word + "': " + (_active.Field.ApplySpellingSuggestion(at, at.WithMissingSpaces()) ? "done" : "refused"));
        }

        void WholeWordSpelling(bool last)
        {
            var at = SpellingAtCaret("whole word");
            if (at == null) return;
            if (at.WritingSystems.Count < 2) { Log("whole word: '" + at.Word + "' is in one writing system"); return; }
            var ws = last ? at.WritingSystems[at.WritingSystems.Count - 1] : at.WritingSystems[0];
            Log("whole word: '" + at.Word + "' -> " + ws + ": " + (_active.Field.ApplySpellingSuggestion(at, at.WithWholeWordIn(ws)) ? "done" : "refused"));
        }

        void AddSpelling(bool ignore)
        {
            var at = SpellingAtCaret(ignore ? "ignore" : "add");
            if (at == null) return;
            bool done = ignore ? _active.Field.IgnoreWord(at) : _active.Field.AddToDictionary(at);
            if (!done) Log((ignore ? "ignore" : "add") + ": '" + at.Word + "' is not a plain misspelling" + (ignore && at.CanAddToDictionary ? " or its dictionary cannot ignore" : string.Empty));
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
        public bool ToggleUnderline() => Toggle("underline", TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle);

        bool Toggle(string label, int property, int on = TextPropertyNumbers.ToggleOn)
        {
            return FormatSelection(label, runs =>
            {
                bool allOn = runs.All(r => r.Props.TryGetInt(property, out var v) && v == on);
                Log("format: " + label + (allOn ? " off" : " on"));
                return r =>
                {
                    if (allOn) r.Props.IntProps.Remove(property);
                    else r.Props.SetInt(property, on);
                };
            });
        }

        /// <summary>Sets (or, with "(none)", removes) the named character style on the selected runs.</summary>
        public bool ApplyCharacterStyle(string style)
        {
            bool none = style == SampleStyles.None;
            return FormatSelection("character style " + style, runs =>
            {
                Log("format: character style " + style);
                return r =>
                {
                    if (none) r.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                    else r.Props.SetStr(TextPropertyNumbers.NamedStyle, style);
                };
            });
        }

        /// <summary>Gives the paragraphs of the selection (or the caret's paragraph) the named paragraph style; "(none)" clears it.</summary>
        public bool ApplyParagraphStyle(string style)
        {
            var session = _active;
            var editor = session.Field.Editor;
            editor.Ordered(out var a, out var b);
            var before = new List<Paragraph>();
            var after = new List<Paragraph>();
            for (int pi = a.Paragraph; pi <= b.Paragraph; pi++)
            {
                var original = editor.Document.Paragraphs[pi];
                var edited = original.Clone();
                edited.Style = style == SampleStyles.None ? string.Empty : style;
                before.Add(original.Clone());
                after.Add(edited);
            }
            Log("format: paragraph style " + style);
            var delta = new EditDelta { Start = a.Paragraph, Before = before, After = after, CaretBefore = editor.Caret, CaretAfter = editor.Caret, Label = "paragraph style " + style };
            session.Undo.Done.Add(delta);
            session.Undo.Undone.Clear();
            var anchor = editor.Anchor;
            var caret = editor.Caret;
            editor.Apply(delta);
            editor.SetCaret(anchor);
            editor.SetCaret(caret, true);
            return true;
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
