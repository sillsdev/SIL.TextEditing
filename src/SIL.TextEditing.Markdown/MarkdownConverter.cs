using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing.Markdown.Reader;
using SIL.TextEditing.Markdown.Writer;

namespace SIL.TextEditing.Markdown
{
    /// <summary>The result of reading one inline-only text.</summary>
    public sealed class MarkdownInlineResult
    {
        internal MarkdownInlineResult(IReadOnlyList<Run> runs, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            Runs = runs;
            Diagnostics = diagnostics;
        }

        public IReadOnlyList<Run> Runs { get; }
        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }
        public bool HasErrors => Diagnostics.Any(d => d.Severity == MarkdownSeverity.Error);
    }

    /// <summary>Converts between the run model and GDR Markdown (ADR 0011; the notation is Motif's spec 2026-11-03-gdr-markdown-design).
    /// Writing then reading gives back the same paragraphs, and reading then writing is stable: the writer is a function of the model
    /// alone, and it checks every paragraph it writes by reading it back.</summary>
    public static class MarkdownConverter
    {
        /// <summary>Writes the document as canonical Markdown: one line per paragraph, one blank line between blocks, <c>\n</c> line ends,
        /// minimal escaping, the attribute order of spec 3.3. The text is as stored; nothing is normalised.</summary>
        /// <exception cref="MarkdownWriteException">A paragraph cannot be written so that it reads back unchanged.</exception>
        public static string ToMarkdown(Document document, MarkdownOptions? options = null)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return ToMarkdown(document.Paragraphs, options);
        }

        public static string ToMarkdown(IEnumerable<Paragraph> paragraphs, MarkdownOptions? options = null)
        {
            if (paragraphs == null) throw new ArgumentNullException(nameof(paragraphs));
            return MarkdownWriter.Write(paragraphs, options ?? new MarkdownOptions());
        }

        /// <summary>Reads Markdown into a document and the findings the parser makes. Paragraph keys are null.</summary>
        public static MarkdownParseResult Parse(string markdown, MarkdownOptions? options = null)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            return MarkdownReader.Read(markdown, options ?? new MarkdownOptions());
        }

        /// <summary>Reads Markdown and writes it again: the canonical form of the text, when it has no errors.</summary>
        public static string Canonicalize(string markdown, MarkdownOptions? options = null)
        {
            options = options ?? new MarkdownOptions();
            return ToMarkdown(Parse(markdown, options).Document, options);
        }

        /// <summary>Writes the runs of one string with the inline grammar only (the abstract of spec 3.5).</summary>
        public static string ToInlineMarkdown(IEnumerable<Run> runs, MarkdownOptions? options = null)
        {
            if (runs == null) throw new ArgumentNullException(nameof(runs));
            return MarkdownWriter.WriteInline(runs, options ?? new MarkdownOptions());
        }

        /// <summary>Reads text that may use the inline grammar only; block syntax is reported as <c>gdr.abstract.block-syntax</c>.</summary>
        public static MarkdownInlineResult ParseInline(string markdown, MarkdownOptions? options = null)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            var runs = MarkdownReader.ReadInline(markdown, options ?? new MarkdownOptions(), out var diagnostics);
            return new MarkdownInlineResult(runs, diagnostics);
        }

        /// <summary>True when two paragraphs say the same as far as Markdown carries: after empty runs are dropped and neighbours of one
        /// format are merged, the same style, other paragraph properties (direction included), runs (text, writing system, properties,
        /// object data) and, for an empty paragraph, typing writing system. Paragraph keys are not compared; a non-empty paragraph's
        /// <see cref="Paragraph.DefaultWs"/> is taken to be its first run's, as the LCModel adapter sets it.</summary>
        public static bool AreEquivalent(Paragraph a, Paragraph b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return Equivalence.Equal(a, b);
        }

        /// <summary>True when the two lists are equivalent paragraph by paragraph (<see cref="AreEquivalent(Paragraph, Paragraph)"/>).</summary>
        public static bool AreEquivalent(IReadOnlyList<Paragraph> a, IReadOnlyList<Paragraph> b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!Equivalence.Equal(a[i], b[i])) return false;
            return true;
        }
    }
}
