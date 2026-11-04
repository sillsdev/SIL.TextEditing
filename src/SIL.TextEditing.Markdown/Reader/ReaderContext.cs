using System;
using System.Collections.Generic;
using Markdig;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>The state of one read: the options, the source text (for line and column numbers) and the findings. Markdig's
    /// parsers reach it through <see cref="MarkdownParserContext"/>.</summary>
    internal sealed class ReaderContext : IAttrIssues
    {
        internal const string ContextKey = "SIL.TextEditing.Markdown.Reader";

        readonly List<MarkdownDiagnostic> _diagnostics = new List<MarkdownDiagnostic>();
        List<int>? _lineStarts;

        public ReaderContext(MarkdownOptions options, string source)
        {
            Options = options;
            Source = source;
        }

        public MarkdownOptions Options { get; }
        public string Source { get; }
        public IReadOnlyList<MarkdownDiagnostic> Diagnostics
        {
            get
            {
                if (_removed != null)
                {
                    _diagnostics.RemoveAll(_removed.Contains);
                    _removed = null;
                }
                return _diagnostics;
            }
        }

        // The findings Markdig's parsers made while it read the whole text, in order of where they start, and the ones dropped since.
        MarkdownDiagnostic[]? _parsed;
        HashSet<MarkdownDiagnostic>? _removed;

        /// <summary>Called once Markdig has read the text: the findings so far are the ones <see cref="RemoveParsed"/> may drop.</summary>
        public void EndParse()
        {
            var parsed = _diagnostics.ToArray();
            var starts = new int[parsed.Length];
            for (int i = 0; i < parsed.Length; i++) starts[i] = parsed[i].Start;
            Array.Sort(starts, parsed);
            _parsed = parsed;
        }

        /// <summary>Drops the findings Markdig's parsers made that start in [start, end): the line was read as text before it was known to be
        /// a marker. Binary search and a mark, so a document with many markers and many findings is read in one pass.</summary>
        public void RemoveParsed(int start, int end)
        {
            var parsed = _parsed;
            if (parsed == null) return;
            int lo = 0, hi = parsed.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (parsed[mid].Start < start) lo = mid + 1; else hi = mid;
            }
            for (int i = lo; i < parsed.Length && parsed[i].Start < end; i++)
            {
                if (_removed == null) _removed = new HashSet<MarkdownDiagnostic>();
                _removed.Add(parsed[i]);
            }
        }

        /// <summary>The offset in <see cref="Source"/> of the text Markdig is parsing now (a fence line is parsed on its own).</summary>
        public int Base { get; set; }

        public MarkdownParserContext NewParserContext()
        {
            var context = new MarkdownParserContext();
            context.Properties[ContextKey] = this;
            return context;
        }

        public static ReaderContext? From(MarkdownParserContext? context)
            => context != null && context.Properties.TryGetValue(ContextKey, out var value) ? value as ReaderContext : null;

        public MarkdownDiagnostic Report(string id, MarkdownSeverity severity, string message, int start, int end, string? replacement = null, IReadOnlyList<string>? choices = null)
        {
            start = Math.Max(0, Math.Min(start, Source.Length));
            end = Math.Max(start, Math.Min(end, Source.Length));
            var d = new MarkdownDiagnostic(id, severity, message, start, end) { Observed = Source.Substring(start, end - start), Replacement = replacement };
            if (choices != null) d.Choices = choices;
            Locate(start, out int line, out int column);
            d.Line = line;
            d.Column = column;
            Locate(end, out int endLine, out int endColumn);
            d.EndLine = endLine;
            d.EndColumn = endColumn;
            _diagnostics.Add(d);
            return d;
        }

        void IAttrIssues.Report(string id, MarkdownSeverity severity, string message, AttrItem item, string? replacement)
            => Report(id, severity, message, Base + item.Start, Base + item.End, replacement);

        /// <summary>Reports an item of an attribute block whose key nothing recognises, with the nearest key as a hint.</summary>
        public void ReportUnknown(AttrItem item, IEnumerable<string> knownKeys)
        {
            string? nearest = Nearest(item.Key, knownKeys);
            string message = "'" + item.Key + "' is not a known " + (item.IsFlag ? "flag" : "attribute") + "." + (nearest == null ? string.Empty : " Did you mean " + nearest + "?");
            var choices = nearest == null ? null : new[] { nearest };
            Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, message, Base + item.Start, Base + item.End, null, choices);
        }

        static string? Nearest(string key, IEnumerable<string> known)
        {
            string? best = null;
            int bestDistance = 3;
            foreach (var k in known)
            {
                int d = Distance(key.ToLowerInvariant(), k.ToLowerInvariant());
                if (d < bestDistance) { bestDistance = d; best = k; }
            }
            return best;
        }

        static int Distance(string a, string b)
        {
            var row = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) row[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                int diagonal = row[0];
                row[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int above = row[j];
                    row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                    diagonal = above;
                }
            }
            return row[b.Length];
        }

        void Locate(int offset, out int line, out int column)
        {
            if (_lineStarts == null)
            {
                _lineStarts = new List<int> { 0 };
                for (int i = 0; i < Source.Length; i++)
                {
                    if (Source[i] == '\n') _lineStarts.Add(i + 1);
                    else if (Source[i] == '\r' && !(i + 1 < Source.Length && Source[i + 1] == '\n')) _lineStarts.Add(i + 1);
                }
            }
            int lo = 0, hi = _lineStarts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lineStarts[mid] <= offset) lo = mid; else hi = mid - 1;
            }
            line = lo + 1;
            column = offset - _lineStarts[lo] + 1;
        }
    }
}
