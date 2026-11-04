using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    // Host-supplied spell checking (ADR 0020): the host gives a checker per writing system; the field checks the paragraphs in view
    // while the application is idle and the layout draws the squiggles. The rules (words, skipped text) are in SpellScanner.
    public partial class RichTextField
    {
        ISpellCheckHost? _spellHost;
        ISpellCheckHost? _spellGuard;
        ISpellCheckHost? _spellListening;
        readonly HashSet<string> _spellFailed = new HashSet<string>();
        bool _spellPosted;
        ScrollViewer? _spellScroll;
        static readonly IReadOnlyList<SpellingMark> NoMarks = new SpellingMark[0];

        /// <summary>How far below the top of a field the checking goes when nothing says how much of it is in view (the field is in no
        /// scroll viewer and has no window yet to measure against): about a screenful and a half.</summary>
        const double UnmeasuredSpan = 2000;

        /// <summary>The host's spell checking. Null, the default, checks nothing. Setting it starts the checking over
        /// (<see cref="RestartSpelling"/>); the field listens to the host's <see cref="ISpellCheckHost.DictionariesChanged"/> while it is in
        /// the visual tree.</summary>
        public ISpellCheckHost? SpellChecking
        {
            get => _spellHost;
            set
            {
                StopListening();
                _spellHost = value;
                _spellGuard = value == null ? null : new GuardedHost(this, value);
                if (_onScreen) StartListening();
                RestartSpelling();
            }
        }

        /// <summary>Raised on the field's thread when a checker or the host throws while the field is checking: the writing system, and the
        /// exception. The field then stops checking that writing system (until <see cref="RestartSpelling"/> or a new host) and goes on with the
        /// others, so a failing dictionary does not end the application. Raised once per writing system until it is checked again. When
        /// nothing handles it, the failure is written to <see cref="System.Diagnostics.Trace"/> (once per writing system, as it is raised), so it is not silent.
        /// A host that repairs the dictionary calls <see cref="ISpellCheckHost.DictionariesChanged"/> (or <see cref="RestartSpelling"/>), which tries the
        /// writing system again.</summary>
        public event Action<string, Exception>? SpellCheckerFailed;

        /// <summary>For a test: where the trace line of a failure nobody handles goes; null (the default) writes it to
        /// <see cref="System.Diagnostics.Trace"/>.</summary>
        internal Action<string>? SpellTraceSink { get; set; }

        /// <summary>For a test: how many times checking has been restarted.</summary>
        internal int SpellRestarts { get; private set; }

        /// <summary>For a test: how many checker wrappers the field has made (one per writing system and checker instance until a restart).</summary>
        internal int SpellWrappersCreated { get; private set; }

        /// <summary>For a test: how idle work is asked for. It is given the action to run when the application is idle; null (the default)
        /// posts it to the field's dispatcher at background priority, after input and layout. One request is pending at a time.</summary>
        internal Action<Action>? SpellIdlePost { get; set; }

        /// <summary>For a test: the part of the field in view as top and bottom in the field's coordinates; null works it out from the
        /// scroll viewer the field is in, else from its window, else a fixed span below the top.</summary>
        internal Func<(double Top, double Bottom)>? SpellVisibleSpan { get; set; }

        /// <summary>Forgets every result and checks again as idle time allows. Call it when the styles changed or a checker's answers changed
        /// without <see cref="ISpellCheckHost.DictionariesChanged"/> being raised. The field does it itself when a word is added
        /// (<see cref="AddToDictionary"/>) or ignored, when the read-only state changes (<c>IsReadOnly</c> of the field or of its editor), when
        /// the host raises <see cref="ISpellCheckHost.DictionariesChanged"/>, and in <see cref="RefreshFromDocument"/>. A writing system that failed is
        /// tried again.
        /// <para>What a host calls when it changes text itself, not through the editor: the paragraph notices a change to its runs, a run's
        /// text, writing system or object data, a replaced property bag and a new style (<see cref="Paragraph.ChangeStamp"/>), so the marks of
        /// that paragraph are dropped at the next draw or query and it is checked again; call <see cref="RefreshFromDocument"/> as well so the
        /// layout follows the new text (it restarts spelling too). A property bag changed in place, a changed paragraph list, or changed styles
        /// are not noticed: call <see cref="RefreshFromDocument"/> (or this, when only what is checked changed). Edits made through the editor,
        /// <see cref="DocumentEditor.Apply"/> and <see cref="DocumentEditor.ExternalReplaceParagraph"/> need no call.</para></summary>
        public void RestartSpelling()
        {
            SpellRestarts++;
            _spellFailed.Clear();
            (_spellGuard as GuardedHost)?.Forget();
            for (int i = 0; i < _info.Count; i++)
            {
                _info[i].Marks = null;
                _layouts[i]?.SetSpellingMarks(null);
            }
            InvalidateVisual();
            ScheduleSpellingIfWanted();
        }

        /// <summary>True when every paragraph in view has been checked (or there is no spell checking). Cheap: it looks at the paragraphs in
        /// view only.</summary>
        public bool IsSpellingComplete => NextUncheckedVisible() < 0;

        /// <summary>Checks one paragraph in view that has not been checked, in document order (FieldWorks checks a step at a time during
        /// idle time), and returns true when nothing in view is left to check. The idle work calls it; a host or test may too. A checker that
        /// throws is reported through <see cref="SpellCheckerFailed"/>, not thrown.</summary>
        public bool CheckSpellingStep()
        {
            if (_spellHost == null) return true;
            int i = NextUncheckedVisible();
            if (i < 0) return true;
            var paragraph = Editor.Document.Paragraphs[i];
            var marks = SpellScanner.Check(paragraph, _spellGuard!, _styles, Editor.IsReadOnly, SpellLimit(i));
            _info[i].Marks = marks;
            _info[i].CheckedStamp = paragraph.ChangeStamp;
            _layouts[i]?.SetSpellingMarks(marks);
            InvalidateVisual();
            return NextUncheckedVisible() < 0;
        }

        /// <summary>The marks of a paragraph as last checked, in order; empty when it is not checked yet or its text has changed since.</summary>
        public IReadOnlyList<SpellingMark> SpellingMarks(int paragraph) => LiveMarks(paragraph) ?? NoMarks;

        /// <summary>The marked word at a position, with what the context menu needs (ADR 0021): the word, its writing system, why it is
        /// marked and the checker's suggestions. This is the hook the host's context menu reads when it builds its spelling items (the
        /// suggestions of <c>ContextInfo</c>); null when the position is not in a marked word, is in text a line limit hides, or there is
        /// no host. Only the word at the position is checked, so the answer is current and cheap.</summary>
        public SpellingSuggestions? SpellingSuggestionsAt(TextPosition position)
        {
            if (_spellHost == null || position.Paragraph < 0 || position.Paragraph >= Editor.Document.Paragraphs.Count) return null;
            int limit = SpellLimit(position.Paragraph);
            if (position.Offset >= limit && limit < int.MaxValue) return null;
            return SpellScanner.SuggestionsAt(Editor.Document.Paragraphs[position.Paragraph], position.Paragraph, position.Offset, _spellGuard!, _styles, Editor.IsReadOnly, limit);
        }

        /// <summary>Replaces the marked word with text in the word's writing system: one run with the properties of the word's first run, as
        /// one undo step. For a misspelling, a suggestion; for a <see cref="SpellingProblem.Mixed"/> word it makes the whole word one writing
        /// system (to keep each part's writing system use the overload that takes runs). False, and nothing changes, for an
        /// <see cref="SpellingProblem.Embedded"/> word (the text would lose its object; use the overload), when the text can no longer be
        /// edited, is hidden by a line limit, or is no longer the word the menu was made for.</summary>
        public bool ApplySpellingSuggestion(SpellingSuggestions suggestions, string replacement)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (suggestions.Problem == SpellingProblem.Embedded || !StillTheWord(suggestions)) return false;
            var paragraph = Editor.Document.Paragraphs[suggestions.Start.Paragraph];
            var first = paragraph.Slice(suggestions.Start.Offset, suggestions.End.Offset)[0];
            return ReplaceSpelling(suggestions, new List<Run> { new Run { Ws = first.Ws, Text = replacement, Props = first.Props.Clone() } });
        }

        /// <summary>Replaces the marked word with runs, as one undo step: each keeps its own writing system, properties and, for an object,
        /// its object data. This is how "insert missing spaces" is done (<see cref="SpellingSuggestions.WithMissingSpaces"/>) for a mixed or
        /// embedded word, and how a host makes any other choice for a word that is not a plain misspelling. False, and nothing changes, when
        /// there are no runs, the text can no longer be edited, is hidden by a line limit, or is no longer the word the menu was made for.</summary>
        public bool ApplySpellingSuggestion(SpellingSuggestions suggestions, IReadOnlyList<Run> replacement)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (replacement.Count == 0 || !StillTheWord(suggestions)) return false;
            return ReplaceSpelling(suggestions, replacement.Select(r => r.Clone()).ToList());
        }

        bool ReplaceSpelling(SpellingSuggestions suggestions, List<Run> runs)
        {
            if (Editor.IsReadOnly || !Editor.CanEditRange(suggestions.Start, suggestions.End)) return false;
            var fragment = new Paragraph { DefaultWs = runs[0].Ws };
            fragment.Runs.AddRange(runs);
            Editor.CloseUndoGroup();
            Editor.ReplaceRange(suggestions.Start, suggestions.End, new List<Paragraph> { fragment }, "spelling");
            Editor.CloseUndoGroup();
            return true;
        }

        /// <summary>"Add to dictionary": hands the word to its checker (<see cref="ISpellChecker.Add"/>) and checks again. False for a
        /// word that is not a plain misspelling (<see cref="SpellingSuggestions.CanAddToDictionary"/>). The host records its own undo.</summary>
        public bool AddToDictionary(SpellingSuggestions suggestions)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (!suggestions.AddToDictionary()) return false;
            RestartSpelling();
            return true;
        }

        /// <summary>"Ignore": hands the word to its checker (<see cref="ISpellIgnorer.Ignore"/>) and checks again. False for a word that is
        /// not a plain misspelling or whose checker cannot ignore (<see cref="SpellingSuggestions.CanIgnore"/>).</summary>
        public bool IgnoreWord(SpellingSuggestions suggestions)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (!suggestions.IgnoreWord()) return false;
            RestartSpelling();
            return true;
        }

        bool StillTheWord(SpellingSuggestions s)
        {
            int p = s.Start.Paragraph;
            if (p < 0 || p >= Editor.Document.Paragraphs.Count || s.End.Paragraph != p) return false;
            var text = Editor.Document.Paragraphs[p].Text;
            if (s.Start.Offset < 0 || s.End.Offset > text.Length || s.Start.Offset >= s.End.Offset) return false;
            if (s.Start.Offset >= SpellLimit(p)) return false;
            var slice = text.Substring(s.Start.Offset, s.End.Offset - s.Start.Offset);
            if (slice == s.Word) return true;
            try { return slice.Normalize(NormalizationForm.FormC) == s.Word; }
            catch (ArgumentException) { return false; }
        }

        // ------------------------------------------------ a host that fails must not end the application

        void SpellFailure(string ws, Exception error)
        {
            if (!_spellFailed.Add(ws)) return;
            var handler = SpellCheckerFailed;
            if (handler == null)
            {
                Trace("Spell checking of writing system '" + ws + "' failed and is switched off until it is restarted: " + error);
                return;
            }
            try { handler(ws, error); }
            catch (Exception thrown)
            {
                // A handler that throws must not take the idle work down with it.
                Trace("The SpellCheckerFailed handler threw for writing system '" + ws + "': " + thrown);
            }
        }

        void Trace(string message)
        {
            var sink = SpellTraceSink;
            if (sink != null) sink(message);
            else System.Diagnostics.Trace.TraceWarning("SIL.TextEditing: " + message);
        }

        /// <summary>The host as the scanner sees it: a call that throws is reported and the writing system is left alone from then on.</summary>
        sealed class GuardedHost : ISpellCheckHost
        {
            readonly RichTextField _field;
            readonly ISpellCheckHost _inner;
            // One wrapper per writing system for as long as the host keeps answering with the same checker, so a word does not allocate one.
            // The host is still asked every time, so a checker it swaps for another is noticed.
            readonly Dictionary<string, (ISpellChecker Inner, ISpellChecker Wrapper)> _wrappers = new Dictionary<string, (ISpellChecker, ISpellChecker)>();

            public GuardedHost(RichTextField field, ISpellCheckHost inner)
            {
                _field = field;
                _inner = inner;
            }

            public ISpellChecker? GetChecker(string ws)
            {
                if (_field._spellFailed.Contains(ws)) return null;
                try
                {
                    var checker = _inner.GetChecker(ws);
                    if (checker == null)
                    {
                        _wrappers.Remove(ws);
                        return null;
                    }
                    if (_wrappers.TryGetValue(ws, out var cached) && ReferenceEquals(cached.Inner, checker)) return cached.Wrapper;
                    ISpellChecker wrapper = checker is ISpellIgnorer ? new GuardedIgnoringChecker(_field, ws, checker) : new GuardedChecker(_field, ws, checker);
                    _wrappers[ws] = (checker, wrapper);
                    _field.SpellWrappersCreated++;
                    return wrapper;
                }
                catch (Exception e)
                {
                    _field.SpellFailure(ws, e);
                    return null;
                }
            }

            /// <summary>Forgets the wrappers: a restart starts from what the host says now.</summary>
            public void Forget() => _wrappers.Clear();

            public bool IsWordForming(string ws, int codePoint)
            {
                try { return _inner.IsWordForming(ws, codePoint); }
                catch (Exception e)
                {
                    _field.SpellFailure(ws, e);
                    return SpellScanner.DefaultIsWordForming(codePoint);
                }
            }

            public bool JoinsWords(string ws, int codePoint)
            {
                try { return _inner.JoinsWords(ws, codePoint); }
                catch (Exception e)
                {
                    _field.SpellFailure(ws, e);
                    return SpellScanner.DefaultJoinsWords(codePoint);
                }
            }

            // The field listens to the host itself; nothing listens to the guard.
            public event EventHandler? DictionariesChanged { add { } remove { } }
        }

        class GuardedChecker : ISpellChecker
        {
            readonly RichTextField _field;
            readonly string _ws;
            protected readonly ISpellChecker Inner;

            public GuardedChecker(RichTextField field, string ws, ISpellChecker inner)
            {
                _field = field;
                _ws = ws;
                Inner = inner;
            }

            // A word that cannot be judged is taken as right: nothing is marked on a failure.
            public bool Check(string word)
            {
                if (_field._spellFailed.Contains(_ws)) return true;
                try { return Inner.Check(word); }
                catch (Exception e)
                {
                    _field.SpellFailure(_ws, e);
                    return true;
                }
            }

            public IReadOnlyList<string> Suggest(string word)
            {
                try { return Inner.Suggest(word); }
                catch (Exception e)
                {
                    _field.SpellFailure(_ws, e);
                    return new string[0];
                }
            }

            // A user's choice is made on purpose, so an exception here is the host's to see.
            public void Add(string word) => Inner.Add(word);
        }

        sealed class GuardedIgnoringChecker : GuardedChecker, ISpellIgnorer
        {
            public GuardedIgnoringChecker(RichTextField field, string ws, ISpellChecker inner) : base(field, ws, inner) { }
            public void Ignore(string word) => ((ISpellIgnorer)Inner).Ignore(word);
        }

        // ------------------------------------------------ results and idle work

        /// <summary>The marks of a paragraph, or null when it is not checked or has changed since (a result for other text is never used).</summary>
        List<SpellingMark>? LiveMarks(int index)
        {
            if (index < 0 || index >= _info.Count) return null;
            var info = _info[index];
            if (info.Marks == null) return null;
            // A result is for the paragraph as it was when checked: any change the paragraph noticed, made through the editor or behind its back
            // (Paragraph.ChangeStamp), makes it stale. Comparing a number is cheap, so the render path asks every time.
            if (index >= Editor.Document.Paragraphs.Count || info.CheckedStamp != Editor.Document.Paragraphs[index].ChangeStamp)
            {
                info.Marks = null;
                return null;
            }
            return info.Marks;
        }

        /// <summary>Called as a paragraph is drawn: a mark the layout still draws for a paragraph that has changed since (behind the editor's back) goes.</summary>
        void DropStaleSpelling(int index, ParagraphLayout layout)
        {
            if (_spellHost != null && _info[index].Marks != null && LiveMarks(index) == null) layout.SetSpellingMarks(null);
        }

        void ApplySpelling(int index, ParagraphLayout layout)
        {
            var marks = LiveMarks(index);
            if (marks != null) layout.SetSpellingMarks(marks);
        }

        /// <summary>The end of the text of a paragraph that shows: where a line limit cuts it, or no end at all.</summary>
        int SpellLimit(int index) => FormatOf(index).MaxLines > 0 ? ParagraphLayoutFor(index).VisibleEnd : int.MaxValue;

        /// <summary>Where a line limit cuts a paragraph changes with the width, so what was checked of such a paragraph is made again.</summary>
        void ForgetLimitedSpelling()
        {
            if (_spellHost == null) return;
            for (int i = 0; i < _info.Count && i < Editor.Document.Paragraphs.Count; i++)
                if (_info[i].Marks != null && FormatOf(i).MaxLines > 0) _info[i].Marks = null;
        }

        (double Top, double Bottom) VisibleSpan()
        {
            var seam = SpellVisibleSpan;
            if (seam != null) return seam();
            var scroll = _spellScroll;
            if (scroll != null)
            {
                var origin = scroll.TranslatePoint(default, this);
                double height = scroll.Viewport.Height > 0 ? scroll.Viewport.Height : scroll.Bounds.Height;
                if (origin != null && height > 0) return (origin.Value.Y, origin.Value.Y + height);
            }
            // Not in a scroll viewer (or one with no viewport): what the window shows of the field.
            var top = TopLevel.GetTopLevel(this);
            if (top != null && top.Bounds.Height > 0)
            {
                var origin = top.TranslatePoint(default, this);
                if (origin != null) return (origin.Value.Y, origin.Value.Y + top.Bounds.Height);
            }
            return (0, UnmeasuredSpan);
        }

        int NextUncheckedVisible()
        {
            if (_spellHost == null) return -1;
            var (top, bottom) = VisibleSpan();
            double y = 0;
            for (int i = 0; i < _layouts.Count && i < Editor.Document.Paragraphs.Count; i++)
            {
                double height = ParagraphLayoutFor(i).Height;
                if (y + height >= top && y <= bottom && LiveMarks(i) == null) return i;
                y += height;
                if (y > bottom) break;
            }
            return -1;
        }

        /// <summary>Asks for idle time when something in view needs checking.</summary>
        void ScheduleSpellingIfWanted()
        {
            if (_spellHost == null || _spellPosted || !_onScreen || NextUncheckedVisible() < 0) return;
            RequestSpellingIdle();
        }

        /// <summary>Asks for idle time now, without looking at the layout (a document change is not the moment to lay paragraphs out):
        /// the idle work finds out what is due. One request is pending at a time, and none while the field is not in the visual tree.</summary>
        void RequestSpellingIdle()
        {
            if (_spellHost == null || _spellPosted || !_onScreen) return;
            _spellPosted = true;
            void Step()
            {
                _spellPosted = false;
                if (_spellHost != null && _onScreen && !CheckSpellingStep()) RequestSpellingIdle();
            }
            var post = SpellIdlePost;
            if (post != null) post(Step);
            else OwnDispatcher.Post(Step, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        // ------------------------------------------------ the visual tree

        /// <summary>The field is now in the visual tree: it follows the scroll viewer it is in and the host's notices, and checks what is due.</summary>
        void WatchScrolling()
        {
            StopWatchingScrolling();
            _spellScroll = this.FindAncestorOfType<ScrollViewer>();
            if (_spellScroll != null) _spellScroll.PropertyChanged += OnScrollerChanged;
            StartListening();
            ScheduleSpellingIfWanted();
        }

        void StopWatchingScrolling()
        {
            if (_spellScroll != null) _spellScroll.PropertyChanged -= OnScrollerChanged;
            _spellScroll = null;
            StopListening();
        }

        void StartListening()
        {
            if (_spellHost == null || _spellListening == _spellHost) return;
            StopListening();
            _spellListening = _spellHost;
            _spellHost.DictionariesChanged += OnDictionariesChanged;
        }

        void StopListening()
        {
            if (_spellListening != null) _spellListening.DictionariesChanged -= OnDictionariesChanged;
            _spellListening = null;
        }

        /// <summary>The editor's read-only flag changed, on whatever thread changed it: checking starts over on the field's own.</summary>
        void OnEditorReadOnlyChanged()
        {
            if (CheckAccess()) RestartSpelling();
            else OwnDispatcher.Post(RestartSpelling, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void OnDictionariesChanged(object? sender, EventArgs e)
        {
            if (CheckAccess()) RestartSpelling();
            else OwnDispatcher.Post(RestartSpelling, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void OnScrollerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ScrollViewer.OffsetProperty && e.Property != ScrollViewer.ViewportProperty) return;
            if (_spellHost == null) return;
            // The content moves when the scroll viewer next arranges it, so ask once that has happened.
            OwnDispatcher.Post(ScheduleSpellingIfWanted, global::Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
