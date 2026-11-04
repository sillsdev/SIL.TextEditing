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
        bool _spellPosted;
        ScrollViewer? _spellScroll;
        static readonly IReadOnlyList<SpellingMark> NoMarks = new SpellingMark[0];

        /// <summary>The host's spell checking. Null, the default, checks nothing. Setting it, or changing anything it answers from (a dictionary,
        /// the word-forming characters), is followed by <see cref="RestartSpelling"/>.</summary>
        public ISpellCheckHost? SpellChecking
        {
            get => _spellHost;
            set
            {
                _spellHost = value;
                RestartSpelling();
            }
        }

        /// <summary>For a test: how idle work is asked for. It is given the action to run when the application is idle; null (the default)
        /// posts it to the field's dispatcher at background priority, after input and layout. One request is pending at a time.</summary>
        internal Action<Action>? SpellIdlePost { get; set; }

        /// <summary>For a test: the part of the field in view as top and bottom in the field's coordinates; null works it out from the
        /// scroll viewer the field is in, or takes the whole field when it is in none.</summary>
        internal Func<(double Top, double Bottom)>? SpellVisibleSpan { get; set; }

        /// <summary>Forgets every result and checks again as idle time allows: the dictionaries changed, the styles changed, or the
        /// read-only state changed. Adding a word through <see cref="AddToDictionary"/> does this itself; a host whose dictionary changed
        /// some other way (an undo of an add, a respelling) calls it.</summary>
        public void RestartSpelling()
        {
            for (int i = 0; i < _info.Count; i++)
            {
                _info[i].Marks = null;
                _info[i].CheckedText = null;
                _layouts[i]?.SetSpellingMarks(null);
            }
            InvalidateVisual();
            ScheduleSpellingIfWanted();
        }

        /// <summary>True when every paragraph in view has been checked (or there is no spell checking). Cheap: it looks at the paragraphs in
        /// view only.</summary>
        public bool IsSpellingComplete => NextUncheckedVisible() < 0;

        /// <summary>Checks one paragraph in view that has not been checked, in document order (FieldWorks checks a step at a time during
        /// idle time), and returns true when nothing in view is left to check. The idle work calls it; a host or test may too.</summary>
        public bool CheckSpellingStep()
        {
            if (_spellHost == null) return true;
            int i = NextUncheckedVisible();
            if (i < 0) return true;
            var paragraph = Editor.Document.Paragraphs[i];
            var marks = SpellScanner.Check(paragraph, _spellHost, _styles, Editor.IsReadOnly);
            _info[i].Marks = marks;
            _info[i].CheckedText = paragraph.Text;
            _layouts[i]?.SetSpellingMarks(marks);
            InvalidateVisual();
            return NextUncheckedVisible() < 0;
        }

        /// <summary>The marks of a paragraph as last checked, in order; empty when it is not checked yet or its text has changed since.</summary>
        public IReadOnlyList<SpellingMark> SpellingMarks(int paragraph) => LiveMarks(paragraph) ?? NoMarks;

        /// <summary>The marked word at a position, with what the context menu needs (ADR 0021): the word, its writing system, why it is
        /// marked and the checker's suggestions. This is the hook the host's context menu reads when it builds its spelling items (the
        /// suggestions of <c>ContextInfo</c>); null when the position is not in a marked word or there is no host. The checker is asked
        /// again, so the answer is current.</summary>
        public SpellingSuggestions? SpellingSuggestionsAt(TextPosition position)
        {
            if (_spellHost == null || position.Paragraph < 0 || position.Paragraph >= Editor.Document.Paragraphs.Count) return null;
            return SpellScanner.SuggestionsAt(Editor.Document.Paragraphs[position.Paragraph], position.Paragraph, position.Offset, _spellHost, _styles, Editor.IsReadOnly);
        }

        /// <summary>Replaces the marked word with the host's choice (a suggestion, or for a mixed word the text with spaces added), as one
        /// undo step, keeping the word's writing system and the formatting of its first run. False, and nothing changes, when the text can
        /// no longer be edited or is no longer the word the menu was made for.</summary>
        public bool ApplySpellingSuggestion(SpellingSuggestions suggestions, string replacement)
        {
            if (suggestions == null) throw new ArgumentNullException(nameof(suggestions));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (!StillTheWord(suggestions) || Editor.IsReadOnly || !Editor.CanEditRange(suggestions.Start, suggestions.End)) return false;
            var paragraph = Editor.Document.Paragraphs[suggestions.Start.Paragraph];
            var first = paragraph.Slice(suggestions.Start.Offset, suggestions.End.Offset)[0];
            var run = new Run { Ws = first.Ws, Text = replacement, Props = first.Props.Clone() };
            var fragment = new Paragraph { DefaultWs = first.Ws };
            fragment.Runs.Add(run);
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

        /// <summary>"Ignore": hands the word to its checker (<see cref="ISpellChecker.Ignore"/>) and checks again. False for a word that is
        /// not a plain misspelling.</summary>
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
            var slice = text.Substring(s.Start.Offset, s.End.Offset - s.Start.Offset);
            if (slice == s.Word) return true;
            try { return slice.Normalize(NormalizationForm.FormC) == s.Word; }
            catch (ArgumentException) { return false; }
        }

        // ------------------------------------------------ results and idle work

        /// <summary>The marks of a paragraph, or null when it is not checked or has changed since (a result for other text is never used).</summary>
        List<SpellingMark>? LiveMarks(int index)
        {
            if (index < 0 || index >= _info.Count) return null;
            var info = _info[index];
            if (info.Marks == null) return null;
            if (info.CheckedText != Editor.Document.Paragraphs[index].Text)
            {
                info.Marks = null;
                info.CheckedText = null;
                return null;
            }
            return info.Marks;
        }

        void ApplySpelling(int index, ParagraphLayout layout)
        {
            var marks = LiveMarks(index);
            if (marks != null) layout.SetSpellingMarks(marks);
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
            return (double.NegativeInfinity, double.PositiveInfinity);
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
            if (_spellHost == null || _spellPosted || NextUncheckedVisible() < 0) return;
            RequestSpellingIdle();
        }

        /// <summary>Asks for idle time now, without looking at the layout (a document change is not the moment to lay paragraphs out):
        /// the idle work finds out what is due. One request is pending at a time.</summary>
        void RequestSpellingIdle()
        {
            if (_spellHost == null || _spellPosted) return;
            _spellPosted = true;
            void Step()
            {
                _spellPosted = false;
                if (_spellHost != null && !CheckSpellingStep()) RequestSpellingIdle();
            }
            var post = SpellIdlePost;
            if (post != null) post(Step);
            else OwnDispatcher.Post(Step, global::Avalonia.Threading.DispatcherPriority.Background);
        }

        void WatchScrolling()
        {
            StopWatchingScrolling();
            _spellScroll = this.FindAncestorOfType<ScrollViewer>();
            if (_spellScroll != null) _spellScroll.PropertyChanged += OnScrollerChanged;
        }

        void StopWatchingScrolling()
        {
            if (_spellScroll != null) _spellScroll.PropertyChanged -= OnScrollerChanged;
            _spellScroll = null;
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
