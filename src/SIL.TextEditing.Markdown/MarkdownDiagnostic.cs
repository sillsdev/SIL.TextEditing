using System;
using System.Collections.Generic;

namespace SIL.TextEditing.Markdown
{
    public enum MarkdownSeverity { Suggestion, Warning, Error }

    /// <summary>A finding the parser makes while reading Markdown: a stable id (the spec's <c>gdr.*</c> ids), a place in the
    /// source and, where one exists, the exact fix. The project-aware checker in Motif adds its own findings to these.</summary>
    public sealed class MarkdownDiagnostic
    {
        public MarkdownDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end)
        {
            Id = id;
            Severity = severity;
            Message = message;
            Start = start;
            End = Math.Max(start, end);
        }

        public string Id { get; }
        public MarkdownSeverity Severity { get; }
        public string Message { get; }

        /// <summary>UTF-16 offset of the first character of the finding in the source text.</summary>
        public int Start { get; }
        /// <summary>UTF-16 offset after the last character.</summary>
        public int End { get; }

        /// <summary>1-based line of <see cref="Start"/>.</summary>
        public int Line { get; internal set; }
        /// <summary>1-based column of <see cref="Start"/> in UTF-16 code units.</summary>
        public int Column { get; internal set; }
        public int EndLine { get; internal set; }
        public int EndColumn { get; internal set; }

        /// <summary>The text the finding is about.</summary>
        public string? Observed { get; set; }

        /// <summary>One exact replacement of <see cref="Observed"/> that fixes the finding, or null when intent is ambiguous.</summary>
        public string? Replacement { get; set; }

        /// <summary>The choices, when there is no single safe fix.</summary>
        public IReadOnlyList<string> Choices { get; set; } = Array.Empty<string>();

        /// <summary>The names the finding suggests instead (<c>gdr.link.not-found</c>, <c>gdr.style.unknown</c>, <c>gdr.ws.unknown</c>).</summary>
        public IReadOnlyList<string> Nearest { get; set; } = Array.Empty<string>();

        public override string ToString() => Id + " " + Line + ":" + Column + " " + Message;
    }

    /// <summary>The result of reading Markdown: the document and every finding.</summary>
    public sealed class MarkdownParseResult
    {
        readonly Reader.ReaderContext _context;

        internal MarkdownParseResult(Document document, IReadOnlyList<MarkdownDiagnostic> diagnostics, Reader.ReaderContext context)
        {
            Document = document;
            Diagnostics = diagnostics;
            _context = context;
        }

        /// <summary>The text the offsets, lines and columns of the findings and objects refer to (the text that was read; a byte order mark
        /// is a space here so that every offset stays the same).</summary>
        public string Source => _context.Source;

        /// <summary>Every object the parser built from the notation (links and pictures), in the order it read them (source order), each with its place
        /// in the text and, when the host resolved it, its data. The host places its own findings with them.</summary>
        public IReadOnlyList<MarkdownObject> Objects => _context.Objects;

        /// <summary>Makes a finding for a range of <see cref="Source"/>, with its line, column and observed text filled in, for a host that checks
        /// more than the parser does (an unmarked vernacular run, a rule of the project). It is not added to <see cref="Diagnostics"/>.</summary>
        public MarkdownDiagnostic CreateDiagnostic(string id, MarkdownSeverity severity, string message, int start, int end,
            string? replacement = null, IReadOnlyList<string>? choices = null, IReadOnlyList<string>? nearest = null)
            => _context.CreateDiagnostic(id, severity, message, start, end, replacement, choices, nearest);

        /// <summary>The paragraphs read. Paragraph keys are null. When there are errors the document is a best effort.</summary>
        public Document Document { get; }

        public IReadOnlyList<MarkdownDiagnostic> Diagnostics { get; }

        public bool HasErrors
        {
            get
            {
                foreach (var d in Diagnostics) if (d.Severity == MarkdownSeverity.Error) return true;
                return false;
            }
        }
    }

    /// <summary>The ids of the findings this library reports. They are the spec's checker ids (section 6); the ones marked "added"
    /// are not in the spec's table.</summary>
    public static class MarkdownDiagnosticIds
    {
        public const string UnclosedMark = "gdr.syntax.unclosed-mark";
        public const string UnknownAttribute = "gdr.syntax.unknown-attribute";
        public const string DuplicateAttribute = "gdr.syntax.duplicate-attribute";
        public const string UnsupportedMarkup = "gdr.syntax.unsupported-markup";
        /// <summary>Added: an attribute block that breaks the grammar of spec 3.3 (an empty block, a bad character or escape), a value that does not fit its key, or text after the block of a ::: line. A key written as a flag is <see cref="UnknownAttribute"/>, and v with ws is <see cref="DuplicateAttribute"/>; the spec has no id for the rest.</summary>
        public const string MalformedAttribute = "gdr.syntax.malformed-attribute";
        /// <summary>Added: a block, line or table too deeply nested or too large for the parser (Markdig's limit). Its lines are kept as plain text.</summary>
        public const string TooDeep = "gdr.syntax.too-deep";
        public const string WsUnknown = "gdr.ws.unknown";
        public const string LinkNotFound = "gdr.link.not-found";
        /// <summary>Reported by hosts (through <see cref="MarkdownProblem"/>): a link name matches more than one target.</summary>
        public const string LinkAmbiguous = "gdr.link.ambiguous";
        /// <summary>Reported by hosts (through <see cref="MarkdownProblem"/>): the link's target was deleted; a warning.</summary>
        public const string LinkBroken = "gdr.link.broken";
        public const string StyleUnknown = "gdr.style.unknown";
        public const string HeadingTooDeep = "gdr.heading.too-deep";
        public const string TableRagged = "gdr.table.ragged";
        public const string TableTabInCell = "gdr.table.tab-in-cell";
        public const string TableAlignment = "gdr.table.alignment";
        public const string PictureUnknown = "gdr.picture.unknown";
        public const string AbstractBlockSyntax = "gdr.abstract.block-syntax";
        public const string PropUnknownNumber = "gdr.prop.unknown-number";
    }

    /// <summary>Raised by the writer when a paragraph cannot be written so that it reads back unchanged. It should not happen for
    /// text; the usual cause is a host whose <see cref="IObjectMapper"/> does not invert itself.</summary>
    public sealed class MarkdownWriteException : InvalidOperationException
    {
        public MarkdownWriteException(string message) : base(message) { }
    }
}
