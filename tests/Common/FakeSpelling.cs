using System.Collections.Generic;
using SIL.TextEditing;

namespace SIL.TextEditing.Tests
{
    /// <summary>A host with a word list per writing system, recording what it was asked.</summary>
    public sealed class FakeSpellHost : ISpellCheckHost
    {
        public readonly Dictionary<string, FakeChecker> Checkers = new Dictionary<string, FakeChecker>();
        public readonly HashSet<int> WordForming = new HashSet<int>();

        public FakeSpellHost(params string[] words) => With("en", words);

        public FakeSpellHost With(string ws, params string[] words)
        {
            var c = new FakeChecker();
            foreach (var w in words) c.Known.Add(w);
            Checkers[ws] = c;
            return this;
        }

        public ISpellChecker? GetChecker(string ws) => Checkers.TryGetValue(ws, out var c) ? c : null;
        public bool IsWordForming(string ws, int cp) => WordForming.Contains(cp) || SpellScanner.DefaultIsWordForming(cp);
    }

    public sealed class FakeChecker : ISpellChecker
    {
        public readonly HashSet<string> Known = new HashSet<string>();
        public readonly List<string> Asked = new List<string>();
        public List<string> Suggestions = new List<string>();
        public readonly List<string> Added = new List<string>();
        public readonly List<string> Ignored = new List<string>();

        public bool Check(string word) { Asked.Add(word); return Known.Contains(word); }
        public IReadOnlyList<string> Suggest(string word) => Suggestions;
        public void Add(string word) { Added.Add(word); Known.Add(word); }
        public void Ignore(string word) { Ignored.Add(word); Known.Add(word); }
    }
}
