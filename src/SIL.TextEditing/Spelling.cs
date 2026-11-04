using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>The dictionary of one writing system, supplied by the host (ADR 0020). The field never loads a dictionary: it
    /// asks this, one word at a time, and the host answers from whatever engine it has. Called on the UI thread.</summary>
    public interface ISpellChecker
    {
        /// <summary>True when the word is spelled correctly. The word is in Unicode form NFC, as the dictionary expects it.</summary>
        bool Check(string word);

        /// <summary>Suggested corrections for a word that failed <see cref="Check"/>, best first. Empty when there are none. All of
        /// them are returned; the menu shows the first <see cref="SpellingSuggestions.MaxInline"/> and puts the rest in a submenu.</summary>
        IReadOnlyList<string> Suggest(string word);

        /// <summary>The user chose "Add to dictionary": the host records the word as correct (and its undo, if it has one). The field then
        /// checks again, so the word's squiggle goes away.</summary>
        void Add(string word);

        /// <summary>The user chose to ignore the word (for this session, or as the host decides). The field then checks again.</summary>
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
        /// <see cref="SpellScanner.DefaultIsWordForming"/>. Digits, which this is not asked about, always form words.</summary>
        bool IsWordForming(string ws, int codePoint);
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
        /// <summary>The word has an embedded object in it. Red, but it is not a spelling to add to the dictionary.</summary>
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

        internal SpellingSuggestions(TextPosition start, TextPosition end, string word, string ws, SpellingProblem problem, IReadOnlyList<string> suggestions, ISpellChecker? checker)
        {
            Start = start;
            End = end;
            Word = word;
            Ws = ws;
            Problem = problem;
            All = suggestions;
            Checker = checker;
        }

        public TextPosition Start { get; }
        public TextPosition End { get; }
        /// <summary>The marked text, in Unicode form NFC, as the dictionary saw it.</summary>
        public string Word { get; }
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
        /// <summary>False for a problem that is not a misspelled word (<see cref="SpellingProblem.Mixed"/>, <see cref="SpellingProblem.Embedded"/>):
        /// "Add to dictionary" is then disabled, as FieldWorks does.</summary>
        public bool CanAddToDictionary => Problem == SpellingProblem.Misspelled && Checker != null;
        ISpellChecker? Checker { get; }

        /// <summary>Hands the word to the checker's <see cref="ISpellChecker.Add"/>; false, and nothing handed over, when
        /// <see cref="CanAddToDictionary"/> is false. The field then checks again (<c>RichTextField.AddToDictionary</c> does both).</summary>
        public bool AddToDictionary()
        {
            if (!CanAddToDictionary) return false;
            Checker!.Add(Word);
            return true;
        }

        /// <summary>Hands the word to the checker's <see cref="ISpellChecker.Ignore"/>; false when it is not a plain misspelling.</summary>
        public bool IgnoreWord()
        {
            if (!CanAddToDictionary) return false;
            Checker!.Ignore(Word);
            return true;
        }
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

        /// <summary>FieldWorks' default for a writing system that says nothing: letters, combining marks and modifier symbols.</summary>
        public static bool DefaultIsWordForming(int codePoint)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0))
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

        static bool IsNumber(int cp)
        {
            var c = CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(cp), 0);
            return c == UnicodeCategory.DecimalDigitNumber || c == UnicodeCategory.LetterNumber || c == UnicodeCategory.OtherNumber;
        }

        /// <summary>Characters inside a word that join its two sides even though they are not word-forming by themselves: the
        /// apostrophes and the zero-width joiners. (A default of ours; a writing system that makes one a letter says so through
        /// <see cref="ISpellCheckHost.IsWordForming"/>.)</summary>
        static bool IsInfix(int cp) => cp == '\'' || cp == 0x2019 || cp == 0x200C || cp == 0x200D;

        sealed class Scan
        {
            public string Text = string.Empty;
            public int[] RunOf = new int[0];
            public List<RunSpan> Spans = new List<RunSpan>();
            public SpellCheckMode[] Modes = new SpellCheckMode[0];
            public bool[] Checked = new bool[0];
        }

        /// <summary>The marks of a paragraph, in order. <paramref name="readOnly"/> is the field-wide read-only flag: text in a
        /// read-only field is not checked unless its run forces checking.</summary>
        public static List<SpellingMark> Check(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false)
        {
            var marks = new List<SpellingMark>();
            ScanWords(paragraph, host, styles, readOnly, (a, b, scan, problem, word, checker) => marks.Add(new SpellingMark(a, b - a, problem)));
            return marks;
        }

        /// <summary>The mark at an offset (the word the offset is in or at the end of), with the suggestions for the menu, or null when the
        /// offset is not in a marked word.</summary>
        public static SpellingSuggestions? SuggestionsAt(Paragraph paragraph, int paragraphIndex, int offset, ISpellCheckHost host, IStyleResolver? styles = null, bool readOnly = false)
        {
            SpellingSuggestions? found = null;
            ScanWords(paragraph, host, styles, readOnly, (a, b, scan, problem, word, checker) =>
            {
                if (found != null || offset < a || offset > b) return;
                var list = problem == SpellingProblem.Misspelled && checker != null ? checker.Suggest(word) : new string[0];
                found = new SpellingSuggestions(new TextPosition(paragraphIndex, a), new TextPosition(paragraphIndex, b), word, scan.Spans[scan.RunOf[a]].Ws, problem, list ?? new string[0], checker);
            });
            return found;
        }

        delegate void Report(int start, int end, Scan scan, SpellingProblem problem, string word, ISpellChecker? checker);

        static void ScanWords(Paragraph paragraph, ISpellCheckHost host, IStyleResolver? styles, bool readOnly, Report report)
        {
            var scan = new Scan { Text = paragraph.Text, Spans = paragraph.Spans() };
            int n = scan.Text.Length;
            if (n == 0) return;
            scan.RunOf = new int[n];
            scan.Checked = new bool[n];
            scan.Modes = new SpellCheckMode[scan.Spans.Count];
            for (int i = 0; i < scan.Spans.Count; i++)
            {
                var span = scan.Spans[i];
                var mode = ModeOf(paragraph, span.Run, styles);
                scan.Modes[i] = mode;
                bool isChecked = mode == SpellCheckMode.ForceCheck || (mode == SpellCheckMode.Normal && !readOnly && DocumentEditor.AllowsEditing(span.Run));
                for (int k = span.Start; k < span.Start + span.Length; k++)
                {
                    scan.RunOf[k] = i;
                    scan.Checked[k] = isChecked;
                }
            }

            bool WordForming(int index, int cp)
            {
                if (cp == Run.ObjectReplacement) return true;
                return IsNumber(cp) || host.IsWordForming(scan.Spans[scan.RunOf[index]].Ws, cp);
            }

            int pos = 0;
            while (pos < n)
            {
                int cp = CodePointAt(scan.Text, pos, out int width);
                if (!WordForming(pos, cp)) { pos += width; continue; }
                int start = pos;
                int end = pos + width;
                while (end < n)
                {
                    int c2 = CodePointAt(scan.Text, end, out int w2);
                    if (WordForming(end, c2)) { end += w2; continue; }
                    // An infix character between two word characters stays in the word.
                    if (IsInfix(c2) && end + w2 < n)
                    {
                        int c3 = CodePointAt(scan.Text, end + w2, out _);
                        if (WordForming(end + w2, c3)) { end += w2; continue; }
                    }
                    break;
                }
                CheckWord(scan, host, start, end, report);
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

        static void CheckWord(Scan scan, ISpellCheckHost host, int a, int b, Report report)
        {
            // Cut the ends back to text that is checked.
            while (b > a && !scan.Checked[a]) a = RunEnd(scan, a);
            while (b > a && !scan.Checked[b - 1]) b = scan.Spans[scan.RunOf[b - 1]].Start;
            if (b <= a) return;
            string lastWs = scan.Spans[scan.RunOf[b - 1]].Ws;
            for (int i = a; i < b; i++)
            {
                if (scan.Text[i] == Run.ObjectReplacement) continue;
                if (!scan.Checked[i] || scan.Spans[scan.RunOf[i]].Ws != lastWs)
                {
                    report(a, b, scan, SpellingProblem.Mixed, scan.Text.Substring(a, b - a), null);
                    return;
                }
            }
            var checker = host.GetChecker(lastWs);
            if (checker == null) return;
            string word = scan.Text.Substring(a, b - a);
            bool hasObject = word.IndexOf(Run.ObjectReplacement) >= 0;
            if (!hasObject)
            {
                try { word = word.Normalize(NormalizationForm.FormC); } catch (ArgumentException) { return; }
                // Numbers alone are not words; no dictionary has them, and FieldWorks' dictionaries accept them.
                if (IsAllNumbers(word)) return;
                if (checker.Check(word)) return;
                report(a, b, scan, SpellingProblem.Misspelled, word, checker);
            }
            else report(a, b, scan, SpellingProblem.Embedded, word, checker);
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
