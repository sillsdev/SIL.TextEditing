using System;
using System.Collections.Generic;
using SIL.TextEditing;

namespace SIL.TextEditing.Tests
{
    /// <summary>A host with a word list per writing system, recording what it was asked.</summary>
    public sealed class FakeSpellHost : ISpellCheckHost
    {
        public readonly Dictionary<string, FakeChecker> Checkers = new Dictionary<string, FakeChecker>();
        /// <summary>Checkers that are not a <see cref="FakeChecker"/>: they win over <see cref="Checkers"/>.</summary>
        public readonly Dictionary<string, ISpellChecker> Custom = new Dictionary<string, ISpellChecker>();
        public readonly HashSet<int> WordForming = new HashSet<int>();
        /// <summary>False turns the apostrophe and joiner rule off.</summary>
        public bool Joiners = true;
        public Exception? WordFormingThrows;
        public Exception? JoinsWordsThrows;
        public Exception? GetCheckerThrows;

        public event EventHandler? DictionariesChanged;
        public int Subscribers => DictionariesChanged?.GetInvocationList().Length ?? 0;
        public void RaiseDictionariesChanged() => DictionariesChanged?.Invoke(this, EventArgs.Empty);

        public FakeSpellHost(params string[] words) => With("en", words);

        public FakeSpellHost With(string ws, params string[] words)
        {
            var c = new FakeChecker();
            foreach (var w in words) c.Known.Add(w);
            Checkers[ws] = c;
            return this;
        }

        public ISpellChecker? GetChecker(string ws)
        {
            if (GetCheckerThrows != null) throw GetCheckerThrows;
            return Custom.TryGetValue(ws, out var x) ? x : Checkers.TryGetValue(ws, out var c) ? c : null;
        }

        public bool IsWordForming(string ws, int cp)
        {
            if (WordFormingThrows != null) throw WordFormingThrows;
            return WordForming.Contains(cp) || SpellScanner.DefaultIsWordForming(cp);
        }

        public bool JoinsWords(string ws, int cp)
        {
            if (JoinsWordsThrows != null) throw JoinsWordsThrows;
            return Joiners && SpellScanner.DefaultJoinsWords(cp);
        }
    }

    public class FakeBareChecker : ISpellChecker
    {
        public readonly HashSet<string> Known = new HashSet<string>();
        public readonly List<string> Asked = new List<string>();
        public List<string> Suggestions = new List<string>();
        public readonly List<string> Added = new List<string>();
        /// <summary>When set, <see cref="Check"/> and <see cref="Suggest"/> throw it.</summary>
        public Exception? Throws;
        /// <summary>When set, only <see cref="Suggest"/> throws it.</summary>
        public Exception? SuggestThrows;

        public bool Check(string word)
        {
            Asked.Add(word);
            if (Throws != null) throw Throws;
            return Known.Contains(word);
        }

        public IReadOnlyList<string> Suggest(string word)
        {
            if (Throws != null) throw Throws;
            if (SuggestThrows != null) throw SuggestThrows;
            return Suggestions;
        }

        public void Add(string word) { Added.Add(word); Known.Add(word); }
    }

    /// <summary>A checker that can also ignore words (the optional <see cref="ISpellIgnorer"/>).</summary>
    public sealed class FakeChecker : FakeBareChecker, ISpellIgnorer
    {
        public readonly List<string> Ignored = new List<string>();
        public void Ignore(string word) { Ignored.Add(word); Known.Add(word); }
    }
}
