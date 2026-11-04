using System;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>A <c>[text]{...}</c> span: the inlines between the brackets, and the attribute block.</summary>
    internal sealed class SpanInline : ContainerInline
    {
        public AttrBlock Attrs = null!;
        /// <summary>Where the opening bracket is, as Markdig numbers it (the source text; a fence line is numbered from its own start).</summary>
        public int OpenAt;
    }

    /// <summary>A <c>[[kind:Name]]</c> link with its optional attribute block.</summary>
    internal sealed class NameLinkInline : LeafInline
    {
        public string Kind = string.Empty;
        public string Name = string.Empty;
        public AttrBlock? Attrs;
    }

    /// <summary>The attribute block that follows a <c>[text](url)</c> or <c>![caption](url)</c>; it is the link's next sibling.</summary>
    internal sealed class AttrsInline : LeafInline
    {
        public AttrBlock Attrs = null!;
    }

    /// <summary>A numeric character reference, decoded exactly: NUL, lone surrogates, CR and LF come back as written.</summary>
    internal sealed class CharRefInline : LeafInline
    {
        public string Text = string.Empty;
    }

    /// <summary>The Markdown extension of the notation (spec 3.2 to 3.4): numeric references, <c>[[kind:Name]]</c>, <c>[text]{...}</c> and
    /// <c>{...}</c> after a link.</summary>
    internal sealed class NotationExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            pipeline.InlineParsers.Insert(0, new CharRefParser());
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new NameLinkParser());
            pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new SpanParser());
            pipeline.InlineParsers.Insert(0, new AfterLinkAttrsParser());
        }

        public void Setup(MarkdownPipeline pipeline, Markdig.Renderers.IMarkdownRenderer renderer) { }

        /// <summary>The two pipelines: the document (blocks and pipe tables) and the inline-only one used for fence lines, where no
        /// block marker means anything.</summary>
        public static readonly MarkdownPipeline Document = new MarkdownPipelineBuilder()
            .UsePreciseSourceLocation()
            .UsePipeTables(new PipeTableOptions { RequireHeaderSeparator = true })
            .Use(new NotationExtension())
            .Build();

        public static readonly MarkdownPipeline InlineOnly = BuildInlineOnly();

        static MarkdownPipeline BuildInlineOnly()
        {
            var builder = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Use(new NotationExtension());
            builder.BlockParsers.Clear();
            builder.BlockParsers.Add(new ParagraphBlockParser());
            return builder.Build();
        }
    }

    internal static class CharRef
    {
        /// <summary>Reads <c>&amp;#x...;</c> or <c>&amp;#...;</c> at <paramref name="at"/>. A scalar value gives its UTF-16 text, a surrogate
        /// gives the lone code unit, so the text is exactly what was written.</summary>
        public static bool TryRead(string text, int at, int limit, out string value, out int length)
        {
            value = string.Empty;
            length = 0;
            if (at + 3 >= limit || text[at] != '&' || text[at + 1] != '#') return false;
            int i = at + 2;
            bool hex = text[i] == 'x' || text[i] == 'X';
            if (hex) i++;
            int digitsStart = i;
            long cp = 0;
            while (i < limit && i - digitsStart <= 8)
            {
                int d = hex ? AttributeScanner.HexValue(text[i]) : (text[i] >= '0' && text[i] <= '9' ? text[i] - '0' : -1);
                if (d < 0) break;
                cp = cp * (hex ? 16 : 10) + d;
                i++;
            }
            if (i == digitsStart || i >= limit || text[i] != ';' || i - digitsStart > 8 || cp > 0x10FFFF) return false;
            value = cp >= 0x10000 ? char.ConvertFromUtf32((int)cp) : ((char)cp).ToString();
            length = i + 1 - at;
            return true;
        }
    }

    sealed class CharRefParser : InlineParser
    {
        public CharRefParser() { OpeningCharacters = new[] { '&' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekChar() != '#') return false;
            if (!CharRef.TryRead(slice.Text, slice.Start, slice.End + 1, out var value, out int length)) return false;
            int start = slice.Start;
            processor.Inline = new CharRefInline
            {
                Text = value,
                Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(start + length - 1)),
                Line = line,
                Column = column,
            };
            slice.Start = start + length;
            return true;
        }
    }

    /// <summary><c>[[kind:Name]]</c>, only for kinds the host resolves; the name is the run text, with <c>\X</c> and numeric references decoded.</summary>
    sealed class NameLinkParser : InlineParser
    {
        public NameLinkParser() { OpeningCharacters = new[] { '[' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekChar() != '[') return false;
            var context = ReaderContext.From(processor.Context);
            if (context == null) return false;
            string text = slice.Text;
            int limit = slice.End + 1;
            int start = slice.Start;
            int i = start + 2;
            int kindStart = i;
            if (i >= limit || !IsAlpha(text[i])) return false;
            while (i < limit && (IsAlpha(text[i]) || (text[i] >= '0' && text[i] <= '9') || text[i] == '-')) i++;
            if (i >= limit || text[i] != ':') return false;
            string kind = text.Substring(kindStart, i - kindStart);
            if (!context.Options.Objects.IsKnownLinkKind(kind)) return false;
            i++;
            var name = new StringBuilder();
            while (true)
            {
                if (i >= limit || text[i] == '\n' || text[i] == '\r') return false;
                char c = text[i];
                if (c == ']' && i + 1 < limit && text[i + 1] == ']') break;
                if (c == '\\' && i + 1 < limit && IsAsciiPunctuation(text[i + 1])) { name.Append(text[i + 1]); i += 2; continue; }
                if (c == '&' && CharRef.TryRead(text, i, limit, out var value, out int length)) { name.Append(value); i += length; continue; }
                name.Append(c);
                i++;
            }
            int end = i + 2; // after "]]"
            AttrBlock? attrs = null;
            if (end < limit && text[end] == '{')
            {
                var status = AttributeScanner.TryScan(text, end, limit, out var block, out int errorPos, out string error);
                int delta = processor.GetSourcePosition(start) - start;
                if (status == ScanStatus.Ok) { end = block!.End; attrs = block.Shifted(delta); }
                else SpanParser.ReportBadBlock(context, status, end, errorPos, error, delta);
            }
            processor.Inline = new NameLinkInline
            {
                Kind = kind,
                Name = name.ToString(),
                Attrs = attrs,
                Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(end - 1)),
                Line = line,
                Column = column,
            };
            slice.Start = end;
            return true;
        }

        static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        internal static bool IsAsciiPunctuation(char c) => (c >= '!' && c <= '/') || (c >= ':' && c <= '@') || (c >= '[' && c <= '`') || (c >= '{' && c <= '~');
    }

    /// <summary><c>]{...}</c> closes the nearest open <c>[</c> as a span with attributes. Everything else about <c>]</c> is Markdig's.</summary>
    sealed class SpanParser : InlineParser
    {
        public SpanParser() { OpeningCharacters = new[] { ']' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            if (slice.PeekChar() != '{') return false;
            var context = ReaderContext.From(processor.Context);
            if (context == null) return false;
            var open = processor.Inline?.FirstParentOfType<LinkDelimiterInline>();
            if (open == null || open.IsImage || !open.IsActive) return false;
            int braceAt = slice.Start + 1;
            var status = AttributeScanner.TryScan(slice.Text, braceAt, slice.End + 1, out var block, out int errorPos, out string error);
            int delta = processor.GetSourcePosition(braceAt) - braceAt;
            if (status != ScanStatus.Ok)
            {
                ReportBadBlock(context, status, braceAt, errorPos, error, delta);
                return false;
            }
            var span = new SpanInline { Attrs = block!.Shifted(delta), OpenAt = open.Span.Start };
            int openStart = open.Span.Start;
            int line = open.Line, column = open.Column;
            open.ReplaceBy(span);
            processor.Inline = span;
            processor.PostProcessInlines(0, span, null, false);
            span.IsClosed = true;
            span.Span = new SourceSpan(openStart, processor.GetSourcePosition(block!.End - 1));
            span.Line = line;
            span.Column = column;
            slice.Start = block.End;
            return true;
        }

        /// <summary>The block with the brace it lacks, when that is an exact fix: the result reads as a block, and every bare word in it is a
        /// flag the grammar knows. Stray words (<c>{ws=ceb and more</c>) would become unknown flags, so they get no fix.</summary>
        static string? ClosingBraceFix(string observed)
        {
            string fixedBlock = observed + "}";
            if (AttributeScanner.TryScan(fixedBlock, 0, fixedBlock.Length, out var block, out _, out _) != ScanStatus.Ok) return null;
            foreach (var item in block!.Items)
                if (item.IsFlag && item.Key != "v" && item.Key != "broken" && item.Key != "empty") return null;
            return fixedBlock;
        }

        /// <param name="delta">What to add to an offset in Markdig's text to get the offset in the source (not zero inside a table).</param>
        internal static void ReportBadBlock(ReaderContext context, ScanStatus status, int blockStart, int errorPos, string error, int delta = 0)
        {
            string id = status == ScanStatus.Unclosed ? MarkdownDiagnosticIds.UnclosedMark : MarkdownDiagnosticIds.MalformedAttribute;
            int start = context.Base + delta + blockStart;
            // An unclosed block ran out at errorPos (the line end or the end of the text), so it ends there and the line end is not part
            // of it; any other error is at a character of the block, which is the last one it covers.
            int end = Math.Min(context.Source.Length, context.Base + delta + errorPos + (status == ScanStatus.Unclosed ? 0 : 1));
            string? fix = status == ScanStatus.Unclosed && error == "no closing }" && end > start ? ClosingBraceFix(context.Source.Substring(start, end - start)) : null;
            context.Report(id, MarkdownSeverity.Error, "The attribute block here is not valid: " + error + ". Escape the brace as \\{ if it is text.", start, end, fix);
        }
    }

    /// <summary><c>{...}</c> right after <c>[text](url)</c> or <c>![caption](url)</c>.</summary>
    sealed class AfterLinkAttrsParser : InlineParser
    {
        public AfterLinkAttrsParser() { OpeningCharacters = new[] { '{' }; }

        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // The link must end right before the brace. Its Span is unreliable inside a table cell, so look at the text.
            if (!(processor.Inline is LinkInline link) || !link.IsClosed || slice.Start == 0 || slice.Text[slice.Start - 1] != ')') return false;
            var context = ReaderContext.From(processor.Context);
            if (context == null) return false;
            var status = AttributeScanner.TryScan(slice.Text, slice.Start, slice.End + 1, out var block, out int errorPos, out string error);
            int start = slice.Start;
            int delta = processor.GetSourcePosition(start) - start;
            if (status != ScanStatus.Ok)
            {
                SpanParser.ReportBadBlock(context, status, start, errorPos, error, delta);
                return false;
            }
            processor.Inline = new AttrsInline
            {
                Attrs = block!.Shifted(delta),
                Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(block!.End - 1)),
                Line = line,
                Column = column,
            };
            slice.Start = block.End;
            return true;
        }
    }
}
