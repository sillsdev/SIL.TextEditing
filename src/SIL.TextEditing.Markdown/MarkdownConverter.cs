using System;
using System.Collections.Generic;
using SIL.TextEditing.Markdown.Reader;

namespace SIL.TextEditing.Markdown
{
    /// <summary>Converts between the run model and GDR Markdown (ADR 0011, Motif spec 2026-11-03-gdr-markdown-design).</summary>
    public static class MarkdownConverter
    {
        /// <summary>Reads Markdown into a document and the findings the parser makes. The paragraph keys are null.</summary>
        public static MarkdownParseResult Parse(string markdown, MarkdownOptions? options = null)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            return MarkdownReader.Read(markdown, options ?? new MarkdownOptions());
        }
    }
}
