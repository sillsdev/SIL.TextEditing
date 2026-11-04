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
        /// its own. A field listens while it is in the visual tree.</summary>
        event EventHandler? DictionariesChanged;
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

        sealed class Scan
        {
            public string Text = string.Empty;
            public int[] RunOf = new int[0];
            public List<RunSpan> Spans = new List<RunSpan>();
            public bool[] Checked = new bool[0];
        }

        /// <summary>The marks of a paragraph, in order. <paramref name="readOnly"/> is the field-wide read-only flag: text in a
        /// read-only field is not checked unless its run forces checking. <paramref name="limit"/> is the end of the text that shows (a line
        /// limit hides the rest): a word that starts at or after it is not checked.</summary>
        public static List<SpellingMark> Check(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
        {
            var marks = new List<SpellingMark>();
            ScanWords(paragraph, host, styles, readOnly, limit, -1, (a, b, scan, problem, word, checker) => { marks.Add(new SpellingMark(a, b - a, problem)); return false; });
            return marks;
        }

        /// <summary>The mark at an offset (the word the offset is in or at the end of), with the suggestions for the menu, or null when the
        /// offset is not in a marked word. Only the word at the offset is checked.</summary>
        public static SpellingSuggestions? SuggestionsAt(Paragraph paragraph, int paragraphIndex, int offset, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false, int limit = int.MaxValue)
        {
            SpellingSuggestions? found = null;
            ScanWords(paragraph, host, styles, readOnly, limit, offset, (a, b, scan, problem, word, checker) =>
            {
                if (offset < a || offset > b) return false;
                var list = problem == SpellingProblem.Misspelled && checker != null ? checker.Suggest(word) : new string[0];
                found = new SpellingSuggestions(new TextPosition(paragraphIndex, a), new TextPosition(paragraphIndex, b), word, scan.Spans[scan.RunOf[b - 1]].Ws, problem, list ?? new string[0], checker, paragraph.Slice(a, b));
                return true;
            });
            return found;
        }

        // Called for each marked word: its range, why, the text to show, and the checker (null for a mixed word). True stops the scan.
        delegate bool Report(int start, int end, Scan scan, SpellingProblem problem, string word, ISpellChecker? checker);

        static void ScanWords(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles, bool readOnly, int limit, int focus, Report report)
        {
            var scan = new Scan { Text = paragraph.Text, Spans = paragraph.Spans() };
            int n = scan.Text.Length;
            if (n == 0) return;
            scan.RunOf = new int[n];
            scan.Checked = new bool[n];
            for (int i = 0; i < scan.Spans.Count; i++)
            {
                var span = scan.Spans[i];
                var mode = ModeOf(paragraph, span.Run, styles);
                bool isChecked = mode == SpellCheckMode.ForceCheck || (mode == SpellCheckMode.Normal && !readOnly && DocumentEditor.AllowsEditing(span.Run));
                for (int k = span.Start; k < span.Start + span.Length; k++)
                {
                    scan.RunOf[k] = i;
                    scan.Checked[k] = isChecked;
                }
            }

            string WsAt(int index) => scan.Spans[scan.RunOf[index]].Ws;

            bool WordForming(int index, int cp)
            {
                if (cp == Run.ObjectReplacement) return true;
                return IsNumber(cp) || host.IsWordForming(WsAt(index), cp);
            }

            int pos = 0;
            while (pos < n && pos < limit)
            {
                int cp = CodePointAt(scan.Text, pos, out int width);
                if (!WordForming(pos, cp)) { pos += width; continue; }
                int start = pos;
                int end = pos + width;
                while (end < n)
                {
                    int c2 = CodePointAt(scan.Text, end, out int w2);
                    if (WordForming(end, c2)) { end += w2; continue; }
                    // A joining character between two word characters stays in the word.
                    if (end + w2 < n && host.JoinsWords(WsAt(end), c2))
                    {
                        int c3 = CodePointAt(scan.Text, end + w2, out _);
                        if (WordForming(end + w2, c3)) { end += w2; continue; }
                    }
                    break;
                }
                // Only the word at the asked offset matters when a menu is being made.
                if ((focus < 0 || (focus >= start && focus <= end)) && CheckWord(scan, host, start, end, report)) return;
                if (focus >= 0 && end >= focus) return;
                pos = end;
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

        // True when the report asked to stop.
        static bool CheckWord(Scan scan, ISpellCheckHost host, int a, int b, Report report)
        {
            // Cut the ends back to text that is checked.
            while (b > a && !scan.Checked[a]) a = RunEnd(scan, a);
            while (b > a && !scan.Checked[b - 1]) b = scan.Spans[scan.RunOf[b - 1]].Start;
            if (b <= a) return false;
            // Objects and the no-break space that precedes a footnote marker are not words: nothing to mark when nothing else is there.
            bool onlyObjects = true;
            for (int i = a; i < b && onlyObjects; i++)
                if (scan.Text[i] != Run.ObjectReplacement && scan.Text[i] != '﻿') onlyObjects = false;
            if (onlyObjects) return false;
            string lastWs = scan.Spans[scan.RunOf[b - 1]].Ws;
            for (int i = a; i < b; i++)
            {
                if (scan.Text[i] == Run.ObjectReplacement) continue;
                if (!scan.Checked[i] || scan.Spans[scan.RunOf[i]].Ws != lastWs)
                    return report(a, b, scan, SpellingProblem.Mixed, Nfc(scan.Text.Substring(a, b - a)) ?? scan.Text.Substring(a, b - a), null);
            }
            var checker = host.GetChecker(lastWs);
            if (checker == null) return false;
            string word = scan.Text.Substring(a, b - a);
            if (word.IndexOf(Run.ObjectReplacement) >= 0)
                return report(a, b, scan, SpellingProblem.Embedded, word, checker);
            var normal = Nfc(word);
            if (normal == null) return false;
            // Numbers alone are not words; no dictionary has them, and FieldWorks' dictionaries accept them.
            if (IsAllNumbers(normal) || checker.Check(normal)) return false;
            return report(a, b, scan, SpellingProblem.Misspelled, normal, checker);
        }

        static string? Nfc(string text)
        {
            try { return text.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { return null; }
        }

        static int RunEnd(Scan scan, int index)
        {
            var span = scan.Spans[scan.RunOf[index]];
            return span.Start + span.Length;
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
