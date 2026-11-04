using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>The dictionary of one writing system, supplied by the host (ADR 0020). The field never loads a dictionary: it
    /// asks this, one word at a time, and the host answers from whatever engine it has. Called on the UI thread. A checker that
    /// can also ignore a word implements <see cref="ISpellIgnorer"/> too.</summary>
    public interface ISpellChecker
    {
        /// <summary>True when the word is spelled correctly. The word is in Unicode form NFC, as the dictionary expects it.</summary>
        bool Check(string word);

        /// <summary>Suggested corrections for a word that failed <see cref="Check"/>, best first. Empty when there are none. All of
        /// them are returned; the menu shows the first <see cref="SpellingSuggestions.MaxInline"/> and puts the rest in a submenu.</summary>
        IReadOnlyList<string> Suggest(string word);

        /// <summary>The user chose "Add to dictionary": the host records the word as correct (and its undo, if it has one), and tells the
        /// fields to check again through <see cref="ISpellCheckHost.DictionariesChanged"/> (the field that asked checks again at once).</summary>
        void Add(string word);
    }

    /// <summary>The optional second capability of an <see cref="ISpellChecker"/>: ignoring a word, for this session or as the host
    /// decides. FieldWorks' dictionaries have no such operation, so a checker need not have one; without it the field offers no
    /// "Ignore" (<see cref="SpellingSuggestions.CanIgnore"/>).</summary>
    public interface ISpellIgnorer
    {
        /// <summary>The user chose to ignore the word. The field then checks again.</summary>
        void Ignore(string word);
    }

    /// <summary>What the host tells the field about spelling: the dictionary of each writing system and which characters make words.</summary>
    public interface ISpellCheckHost
    {
        /// <summary>The checker for a writing system, or null when the writing system has no dictionary (its text is then not
        /// checked, though a word that mixes writing systems is still marked).</summary>
        ISpellChecker? GetChecker(string ws);

        /// <summary>True when the code point forms words in the writing system. FieldWorks asks the writing system (its valid
        /// characters can make an apostrophe or a hyphen a letter); a host with no opinion returns
        /// <see cref="SpellScanner.DefaultIsWordForming"/>. Digits, which this is not asked about, always form words. (FieldWorks asks about
        /// a UTF-16 unit; the field asks about a code point, so a supplementary-plane letter arrives whole.)</summary>
        bool IsWordForming(string ws, int codePoint);

        /// <summary>True when the code point, between two word-forming characters, stays in the word: the apostrophes and the zero-width
        /// joiners, so "don't" is one word. A host with no opinion returns <see cref="SpellScanner.DefaultJoinsWords"/>; false for every
        /// code point turns the rule off, and then "don't" is "don" and "t" unless <see cref="IsWordForming"/> says the apostrophe forms words.</summary>
        bool JoinsWords(string ws, int codePoint);

        /// <summary>Raised by the host when what its checkers answer has changed: a word added or removed (an add, or the undo of one), a
        /// dictionary installed or changed. Every field using the host checks again. It may be raised on any thread; the field handles it on
        /// its own. A field listens while it is in the visual tree. A field stops checking a writing system whose checker threw
        /// (<c>RichTextField.SpellCheckerFailed</c>) until it hears this, or is restarted, so a host that repairs such a dictionary raises it.
        /// <see cref="SpellCheckHostBase"/> raises it for a host that derives from it.</summary>
        event EventHandler? DictionariesChanged;
    }

    /// <summary>A base for a host: write <see cref="GetChecker"/> and nothing else. The word tests answer with
    /// <see cref="SpellScanner.DefaultIsWordForming"/> and <see cref="SpellScanner.DefaultJoinsWords"/> (override them when a writing system says
    /// otherwise), and <see cref="RaiseDictionariesChanged"/> announces a change to every field using the host, from any thread. A host that
    /// repairs a dictionary the field reported as failed (<c>RichTextField.SpellCheckerFailed</c>) calls it afterwards, so the field tries again.</summary>
    public abstract class SpellCheckHostBase : ISpellCheckHost
    {
        public abstract ISpellChecker? GetChecker(string ws);

        public virtual bool IsWordForming(string ws, int codePoint) => SpellScanner.DefaultIsWordForming(codePoint);

        public virtual bool JoinsWords(string ws, int codePoint) => SpellScanner.DefaultJoinsWords(codePoint);

        public event EventHandler? DictionariesChanged;

        /// <summary>Tells every field using this host that what its checkers answer has changed: a word added or removed, a dictionary
        /// installed, changed or repaired. Safe to call from any thread.</summary>
        public void RaiseDictionariesChanged() => DictionariesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The values of FieldWorks' spell-check text property (ktptSpellCheck, 26).</summary>
    public enum SpellCheckMode
    {
        /// <summary>Checked, unless the text may not be edited.</summary>
        Normal = 0,
        /// <summary>Never checked.</summary>
        DoNotCheck = 1,
        /// <summary>Checked even when the text may not be edited.</summary>
        ForceCheck = 2,
    }

    /// <summary>Why a stretch of text is marked.</summary>
    public enum SpellingProblem
    {
        /// <summary>The writing system's dictionary does not have the word. Red.</summary>
        Misspelled,
        /// <summary>The "word" is not one thing a dictionary can judge: it has text in several writing systems, or text the field does not
        /// check, inside it. Blue. The fix is spaces between the parts, not a spelling.</summary>
        Mixed,
        /// <summary>The word has an embedded object in it, next to text. Red, but it is not a spelling to add to the dictionary. An object
        /// alone is not marked.</summary>
        Embedded,
    }

    /// <summary>A marked stretch of one paragraph: the squiggled word.</summary>
    public readonly struct SpellingMark : IEquatable<SpellingMark>
    {
        public SpellingMark(int start, int length, SpellingProblem problem)
        {
            Start = start;
            Length = length;
            Problem = problem;
        }

        /// <summary>First UTF-16 offset in the paragraph.</summary>
        public int Start { get; }
        public int Length { get; }
        public int End => Start + Length;
        public SpellingProblem Problem { get; }

        public bool Equals(SpellingMark other) => Start == other.Start && Length == other.Length && Problem == other.Problem;
        public override bool Equals(object? obj) => obj is SpellingMark m && Equals(m);
        public override int GetHashCode() => (Start * 397 ^ Length) * 31 + (int)Problem;
        public override string ToString() => Problem + "[" + Start + "," + End + ")";
    }

    /// <summary>What the host's context menu needs for the marked word under a point (ADR 0021): the word, its writing system, the
    /// problem, and the suggestions. The host builds the menu; this applies the choices.</summary>
    public sealed class SpellingSuggestions
    {
        /// <summary>FieldWorks puts this many suggestions in the menu itself and the rest in an "Additional suggestions" submenu.</summary>
        public const int MaxInline = 7;

        internal SpellingSuggestions(TextPosition start, TextPosition end, string word, string ws, SpellingProblem problem, IReadOnlyList<string> suggestions, ISpellChecker? checker, IReadOnlyList<Run> parts)
        {
            Start = start;
            End = end;
            Word = word;
            Ws = ws;
            Problem = problem;
            All = suggestions;
            Checker = checker;
            Parts = parts;
        }

        public TextPosition Start { get; }
        public TextPosition End { get; }
        /// <summary>The marked text, in Unicode form NFC, as the dictionary saw it (a <see cref="SpellingProblem.Embedded"/> word keeps its object
        /// characters).</summary>
        public string Word { get; }
        /// <summary>The writing system of the last character of the word (the one whose dictionary is asked).</summary>
        public string Ws { get; }
        public SpellingProblem Problem { get; }
        /// <summary>Every suggestion, best first. Empty is a real answer: the word is wrong and there is nothing to offer ("No suggestions").</summary>
        public IReadOnlyList<string> All { get; }
        /// <summary>The first <see cref="MaxInline"/> suggestions.</summary>
        public IEnumerable<string> Inline
        {
            get { for (int i = 0; i < All.Count && i < MaxInline; i++) yield return All[i]; }
        }
        /// <summary>The suggestions after the first <see cref="MaxInline"/>.</summary>
        public IEnumerable<string> Additional
        {
            get { for (int i = MaxInline; i < All.Count; i++) yield return All[i]; }
        }
        /// <summary>The runs of the marked text, as they are in the paragraph (copies): a host building a replacement that keeps each part's
        /// writing system, properties or object starts from these.</summary>
        public IReadOnlyList<Run> Parts { get; }

        /// <summary>False for a problem that is not a misspelled word (<see cref="SpellingProblem.Mixed"/>, <see cref="SpellingProblem.Embedded"/>):
        /// "Add to dictionary" is then disabled, as FieldWorks does.</summary>
        public bool CanAddToDictionary => Problem == SpellingProblem.Misspelled && Checker != null;

        /// <summary>True when "Ignore" can be offered: a plain misspelling whose checker is an <see cref="ISpellIgnorer"/>.</summary>
        public bool CanIgnore => CanAddToDictionary && Checker is ISpellIgnorer;

        ISpellChecker? Checker { get; }

        /// <summary>The same answer for the same word in the same paragraph, with fresh copies of the parts (a host may change the ones it was given).</summary>
        internal SpellingSuggestions Again(int paragraphIndex, Paragraph paragraph)
            => new SpellingSuggestions(new TextPosition(paragraphIndex, Start.Offset), new TextPosition(paragraphIndex, End.Offset), Word, Ws, Problem, All, Checker, paragraph.Slice(Start.Offset, End.Offset));

        /// <summary>Hands the word to the checker's <see cref="ISpellChecker.Add"/>; false, and nothing handed over, when
        /// <see cref="CanAddToDictionary"/> is false. The field then checks again (<c>RichTextField.AddToDictionary</c> does both).</summary>
        public bool AddToDictionary()
        {
            if (!CanAddToDictionary) return false;
            Checker!.Add(Word);
            return true;
        }

        /// <summary>Hands the word to the checker's <see cref="ISpellIgnorer.Ignore"/>; false when <see cref="CanIgnore"/> is false.</summary>
        public bool IgnoreWord()
        {
            if (!CanIgnore) return false;
            ((ISpellIgnorer)Checker!).Ignore(Word);
            return true;
        }

        /// <summary>The parts with a space between two that a dictionary cannot judge together: a part in another writing system, one that
        /// differs in whether it is checked, or an object and the text beside it. This is FieldWorks' "insert missing spaces" for a
        /// <see cref="SpellingProblem.Mixed"/> or <see cref="SpellingProblem.Embedded"/> word; pass it to
        /// <c>RichTextField.ApplySpellingSuggestion</c>. Each space is in the writing system of the part before it.</summary>
        public List<Run> WithMissingSpaces()
        {
            var result = new List<Run>();
            foreach (var part in Parts)
            {
                if (result.Count > 0 && NeedsSpace(result[result.Count - 1], part))
                    result.Add(new Run { Ws = result[result.Count - 1].Ws, Text = " " });
                result.Add(part.Clone());
            }
            return result;
        }

        static bool NeedsSpace(Run a, Run b)
        {
            if (a.IsObject || b.IsObject || a.Ws != b.Ws) return true;
            return Prop(a, TextPropertyNumbers.SpellCheck) != Prop(b, TextPropertyNumbers.SpellCheck) || Prop(a, TextPropertyNumbers.Editable) != Prop(b, TextPropertyNumbers.Editable);
        }

        static int Prop(Run r, int number) => r.Props.TryGetInt(number, out var v) ? v : -1;
    }

    /// <summary>
    /// Finds the misspelled words of a paragraph, as FieldWorks' spell checker does (ADR 0020), without any user interface.
    /// A word is a maximal run of word-forming characters (letters, marks, modifier symbols, and numbers) across runs; the characters
    /// at its ends that are not checked are cut off; a word with a hole in the checking or with several writing systems in it is
    /// <see cref="SpellingProblem.Mixed"/>; otherwise its text, in NFC, goes to the checker of its writing system.
    /// </summary>
    public static class SpellScanner
    {
        /// <summary>The spell-check mode of a run: style layers, then the run's own bag, later winning.</summary>
        public static SpellCheckMode ModeOf(Paragraph paragraph, Run run, IStyleResolver? styles)
        {
            int mode = 0;
            string paraStyle = paragraph.Style.Length > 0 ? paragraph.Style : paragraph.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var s) ? s : string.Empty;
            if (styles != null && paraStyle.Length > 0)
            {
                var bag = styles.Resolve(paraStyle, run.Ws);
                if (bag != null && bag.TryGetInt(TextPropertyNumbers.SpellCheck, out var v)) mode = v;
            }
            if (paragraph.Props.TryGetInt(TextPropertyNumbers.SpellCheck, out var pv)) mode = pv;
            if (styles != null && run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var charStyle) && charStyle.Length > 0)
            {
                var bag = styles.Resolve(charStyle, run.Ws);
                if (bag != null && bag.TryGetInt(TextPropertyNumbers.SpellCheck, out var v)) mode = v;
            }
            if (run.Props.TryGetInt(TextPropertyNumbers.SpellCheck, out var rv)) mode = rv;
            return mode == TextPropertyNumbers.SpellCheckDoNot ? SpellCheckMode.DoNotCheck : mode == TextPropertyNumbers.SpellCheckForce ? SpellCheckMode.ForceCheck : SpellCheckMode.Normal;
        }

        // The Unicode category of a code point, with no allocation for the common case; a value that is not a character (a lone surrogate, out
        // of range) is not a letter or a number.
        static UnicodeCategory CategoryOf(int codePoint)
        {
            if (codePoint < 0 || codePoint > 0x10FFFF) return UnicodeCategory.OtherNotAssigned;
            if (codePoint < 0x10000) return CharUnicodeInfo.GetUnicodeCategory((char)codePoint);
#if NET10_0_OR_GREATER
            return CharUnicodeInfo.GetUnicodeCategory(codePoint);
#else
            return CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0);
#endif
        }

        /// <summary>FieldWorks' default for a writing system that says nothing: letters, combining marks and modifier symbols. A value that
        /// is not a character (a lone surrogate, out of range) is not word-forming.</summary>
        public static bool DefaultIsWordForming(int codePoint)
        {
            switch (CategoryOf(codePoint))
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.ModifierSymbol:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The characters that join the word on each side of them when the host has no opinion (ours; FieldWorks asks the writing
        /// system): the apostrophes U+0027 and U+2019 and the zero-width joiner and non-joiner U+200D and U+200C.</summary>
        public static bool DefaultJoinsWords(int codePoint) => codePoint == '\'' || codePoint == 0x2019 || codePoint == 0x200C || codePoint == 0x200D;

        static bool IsNumber(int cp)
        {
            var c = CategoryOf(cp);
            return c == UnicodeCategory.DecimalDigitNumber || c == UnicodeCategory.LetterNumber || c == UnicodeCategory.OtherNumber;
        }

        /// <summary>One word the scanner found and judged: its range, why it is marked, the text the dictionary saw and its checker (null for a mixed word).</summary>
        internal readonly struct Judged
        {
            public Judged(int start, int end, SpellingProblem problem, string word, ISpellChecker? checker)
            {
                Start = start;
                End = end;
                Problem = problem;
                Word = word;
                Checker = checker;
            }

            public int Start { get; }
            public int End { get; }
            public SpellingProblem Problem { get; }
            public string Word { get; }
            public ISpellChecker? Checker { get; }
        }

        /// <summary>The marks of a paragraph, in order. <paramref name="readOnly"/> is the field-wide read-only flag: text in a
        /// read-only field is not checked unless its run forces checking. <paramref name="limit"/> is the end of the text that shows (a line
        /// limit hides the rest): a word that starts at or after it is not checked.</summary>
        public static List<SpellingMark> Check(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
        {
            var marks = new List<SpellingMark>();
            var scan = new Window(paragraph, host, styles, readOnly);
            int pos = 0;
            while (scan.NextWord(pos, out int a, out int b) && a < limit)
            {
                var judged = scan.Judge(a, b);
                if (judged != null) marks.Add(new SpellingMark(judged.Value.Start, judged.Value.End - judged.Value.Start, judged.Value.Problem));
                pos = b;
            }
            return marks;
        }

        /// <summary>The mark at an offset (the word the offset is in or at the end of), with the suggestions for the menu, or null when the
        /// offset is not in a marked word. Only the word at the offset is looked at: the work is that of one word however long the paragraph is.</summary>
        public static SpellingSuggestions? SuggestionsAt(Paragraph paragraph, int paragraphIndex, int offset, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
            => SuggestionsAt(paragraph, paragraphIndex, offset, host, styles, readOnly, limit, null);

        internal static SpellingSuggestions? SuggestionsAt(Paragraph paragraph, int paragraphIndex, int offset, ISpellCheckHost host, IStyleResolver? styles, bool readOnly, int limit, Func<Judged, IReadOnlyList<string>?>? cachedSuggestions)
        {
            if (offset < 0 || offset > paragraph.Length) return null;
            var scan = new Window(paragraph, host, styles, readOnly);
            if (!scan.NextWord(scan.WordStart(offset), out int a, out int b) || a >= limit || offset < a || offset > b) return null;
            var judged = scan.Judge(a, b);
            if (judged == null) return null;
            var j = judged.Value;
            if (offset < j.Start || offset > j.End) return null;   // the ends that are not checked are cut off, and the offset may be in them
            IReadOnlyList<string>? list = null;
            if (j.Problem == SpellingProblem.Misspelled && j.Checker != null)
                list = cachedSuggestions?.Invoke(j) ?? j.Checker.Suggest(j.Word);
            return new SpellingSuggestions(new TextPosition(paragraphIndex, j.Start), new TextPosition(paragraphIndex, j.End), j.Word, scan.WsAt(j.End - 1), j.Problem, list ?? new string[0], j.Checker, paragraph.Slice(j.Start, j.End));
        }

        /// <summary>A window onto one paragraph for finding and judging words: nothing is worked out for the whole paragraph, only for the text a call
        /// walks over, so checking a few words or one word costs that and not the length of the paragraph.</summary>
        internal sealed class Window
        {
            readonly Paragraph _paragraph;
            readonly ISpellCheckHost _host;
            readonly IStyleResolver? _styles;
            readonly bool _readOnly;
            readonly string _text;
            readonly Dictionary<int, bool> _checkedRuns = new Dictionary<int, bool>();
            // The run the last character asked about is in, so the next one rarely needs a search.
            int _runIndex = -1, _runStart, _runEnd;

            public Window(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles, bool readOnly)
            {
                _paragraph = paragraph;
                _host = host;
                _styles = styles;
                _readOnly = readOnly;
                _text = paragraph.Text;
            }

            public int Length => _text.Length;

            void Locate(int index)
            {
                if (_runIndex >= 0 && index >= _runStart && index < _runEnd) return;
                _runIndex = _paragraph.RunIndexAt(index, out _runStart);
                _runEnd = _runStart + _paragraph.Runs[_runIndex].Text.Length;
            }

            public string WsAt(int index)
            {
                Locate(index);
                return _paragraph.Runs[_runIndex].Ws;
            }

            bool IsChecked(int index)
            {
                Locate(index);
                if (_checkedRuns.TryGetValue(_runIndex, out var known)) return known;
                var run = _paragraph.Runs[_runIndex];
                var mode = ModeOf(_paragraph, run, _styles);
                bool isChecked = mode == SpellCheckMode.ForceCheck || (mode == SpellCheckMode.Normal && !_readOnly && DocumentEditor.AllowsEditing(run));
                _checkedRuns[_runIndex] = isChecked;
                return isChecked;
            }

            int RunEndOf(int index)
            {
                Locate(index);
                return _runEnd;
            }

            int RunStartOf(int index)
            {
                Locate(index);
                return _runStart;
            }

            bool WordForming(int index, int cp)
            {
                if (cp == Run.ObjectReplacement) return true;
                return IsNumber(cp) || _host.IsWordForming(WsAt(index), cp);
            }

            int CodePointBefore(int index, out int width)
            {
                char c = _text[index - 1];
                if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(_text[index - 2]))
                {
                    width = 2;
                    return char.ConvertToUtf32(_text[index - 2], c);
                }
                width = 1;
                return char.IsSurrogate(c) ? 0xFFFD : c;
            }

            /// <summary>Where the word that contains or ends at <paramref name="pos"/> starts, so that scanning forward from there finds the same words a
            /// scan from the start of the paragraph would: back over word-forming characters and over a joiner between two of them.</summary>
            public int WordStart(int pos)
            {
                int p = Math.Min(pos, _text.Length);
                if (p > 0 && p < _text.Length && char.IsLowSurrogate(_text[p]) && char.IsHighSurrogate(_text[p - 1])) p--;   // an offset between the halves of a pair is in the pair
                while (p > 0)
                {
                    int cp = CodePointBefore(p, out int w);
                    if (WordForming(p - w, cp)) { p -= w; continue; }
                    if (p - w > 0 && p < _text.Length && _host.JoinsWords(WsAt(p - w), cp))
                    {
                        int before = CodePointBefore(p - w, out int w2);
                        if (WordForming(p - w - w2, before) && WordForming(p, CodePointAt(_text, p, out _))) { p -= w; continue; }
                    }
                    break;
                }
                return p;
            }

            /// <summary>Where checking again must start for text changed from <paramref name="pos"/> on: <see cref="WordStart"/>, and one word further back
            /// when a joiner (an apostrophe) stands just before it, because the word before the joiner may have been one word with what was edited.</summary>
            public int WordStartForRecheck(int pos)
            {
                int p = WordStart(pos);
                if (p > 0)
                {
                    int cp = CodePointBefore(p, out int w);
                    if (p - w > 0 && _host.JoinsWords(WsAt(p - w), cp)) p = WordStart(p - w);
                }
                return p;
            }

            /// <summary>The first word that starts at or after <paramref name="from"/>: its range, or false when none does.</summary>
            public bool NextWord(int from, out int start, out int end)
            {
                int n = _text.Length;
                int pos = Math.Max(0, from);
                start = end = n;
                while (pos < n)
                {
                    int cp = CodePointAt(_text, pos, out int width);
                    if (!WordForming(pos, cp)) { pos += width; continue; }
                    start = pos;
                    end = pos + width;
                    while (end < n)
                    {
                        int c2 = CodePointAt(_text, end, out int w2);
                        if (WordForming(end, c2)) { end += w2; continue; }
                        // A joining character between two word characters stays in the word.
                        if (end + w2 < n && _host.JoinsWords(WsAt(end), c2))
                        {
                            int c3 = CodePointAt(_text, end + w2, out _);
                            if (WordForming(end + w2, c3)) { end += w2; continue; }
                        }
                        break;
                    }
                    return true;
                }
                return false;
            }

            /// <summary>Judges the word [a, b): null when nothing is to be marked (its text is not checked, it is a lone object, the dictionary has it or
            /// there is none), else the problem, with the ends that are not checked cut off.</summary>
            public Judged? Judge(int a, int b)
            {
                // Cut the ends back to text that is checked.
                while (b > a && !IsChecked(a)) a = RunEndOf(a);
                while (b > a && !IsChecked(b - 1)) b = RunStartOf(b - 1);
                if (b <= a) return null;
                // Objects and the no-break space that precedes a footnote marker are not words: nothing to mark when nothing else is there.
                bool onlyObjects = true;
                for (int i = a; i < b && onlyObjects; i++)
                    if (_text[i] != Run.ObjectReplacement && _text[i] != '﻿') onlyObjects = false;
                if (onlyObjects) return null;
                string lastWs = WsAt(b - 1);
                for (int i = a; i < b; i++)
                {
                    if (_text[i] == Run.ObjectReplacement) continue;
                    if (!IsChecked(i) || WsAt(i) != lastWs)
                    {
                        string raw = _text.Substring(a, b - a);
                        return new Judged(a, b, SpellingProblem.Mixed, Nfc(raw) ?? raw, null);
                    }
                }
                var checker = _host.GetChecker(lastWs);
                if (checker == null) return null;
                string word = _text.Substring(a, b - a);
                if (word.IndexOf(Run.ObjectReplacement) >= 0) return new Judged(a, b, SpellingProblem.Embedded, word, checker);
                var normal = Nfc(word);
                if (normal == null) return null;
                // Numbers alone are not words; no dictionary has them, and FieldWorks' dictionaries accept them.
                if (IsAllNumbers(normal) || checker.Check(normal)) return null;
                return new Judged(a, b, SpellingProblem.Misspelled, normal, checker);
            }
        }

        static int CodePointAt(string text, int index, out int width)
        {
            char c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                width = 2;
                return char.ConvertToUtf32(c, text[index + 1]);
            }
            width = 1;
            return char.IsSurrogate(c) ? 0xFFFD : c;
        }

        static string? Nfc(string text)
        {
            try { return text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return null; }
        }

        static bool IsAllNumbers(string word)
        {
            for (int i = 0; i < word.Length; )
            {
                int cp = CodePointAt(word, i, out int w);
                if (!IsNumber(cp)) return false;
                i += w;
            }
            return true;
        }
    }
}
