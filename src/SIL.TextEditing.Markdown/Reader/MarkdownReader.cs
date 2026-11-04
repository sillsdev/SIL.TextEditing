using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Reader
{
    /// <summary>Reads GDR Markdown into paragraphs: Markdig for the CommonMark blocks and inlines, our own scan for fenced blocks
    /// (spec 3.1), the notation extension for spec 3.2 to 3.4.</summary>
    internal sealed class MarkdownReader
    {
        readonly MarkdownOptions _options;
        readonly ReaderContext _context;
        readonly string _source;
        readonly List<Paragraph> _paragraphs = new List<Paragraph>();

        MarkdownReader(MarkdownOptions options, string source)
        {
            _options = options;
            _source = source;
            _context = new ReaderContext(options, source);
        }

        MarkdownStyleNames Names => _options.StyleNames;

        public static MarkdownParseResult Read(string markdown, MarkdownOptions options)
        {
            var reader = new MarkdownReader(options, Prepare(markdown));
            reader.ReadAll();
            var document = new Document();
            document.Paragraphs.AddRange(reader._paragraphs);
            return new MarkdownParseResult(document, reader._context.Diagnostics.Select((d, i) => (d, i)).OrderBy(x => x.d.Start).ThenBy(x => x.i).Select(x => x.d).ToList(), reader._context);
        }

        /// <summary>The paragraphs of a text, for the writer's check that what it wrote reads back. <paramref name="clean"/> is false when
        /// the parser found a syntax, table or heading error, which the canonical form never has. Findings about the project (an unknown
        /// writing system or link) are not the writer's concern.</summary>
        public static List<Paragraph> ReadParagraphs(string markdown, MarkdownOptions options, out bool clean)
        {
            var reader = new MarkdownReader(options, Prepare(markdown));
            reader.ReadAll();
            clean = true;
            foreach (var d in reader._context.Diagnostics)
            {
                if (d.Severity == MarkdownSeverity.Error && IsSyntaxFinding(d.Id)) clean = false;
            }
            return reader._paragraphs;
        }

        /// <summary>True for the findings that mean the text is not well-formed notation (syntax, table and heading errors).</summary>
        public static bool IsSyntaxFinding(string id)
            => id.StartsWith("fwmd.syntax.", StringComparison.Ordinal) || id.StartsWith("fwmd.table.", StringComparison.Ordinal) || id.StartsWith("fwmd.heading.", StringComparison.Ordinal)
               || id == MarkdownDiagnosticIds.FieldBlockSyntax;

        /// <summary>One inline-only text (the abstract, spec 3.5): runs, with block syntax reported.</summary>
        public static List<Run> ReadInline(string text, MarkdownOptions options, out ReaderContext context)
        {
            var reader = new MarkdownReader(options, Prepare(text));
            var runs = reader.ReadInlineText();
            context = reader._context;
            return runs;
        }

        static string Prepare(string markdown)
        {
            if (markdown.Length > 0 && markdown[0] == Chars.ByteOrderMark) return " " + markdown.Substring(1); // keep every offset
            return markdown;
        }

        // ---------------------------------------------------------------- the document

        sealed class Fence
        {
            public int Start;
            public AttrBlock? Attrs;
            public readonly List<int[]> Lines = new List<int[]>(); // {start, end} of each content line, terminator excluded
            public bool Closed;
        }

        void ReadAll()
        {
            var fences = new List<Fence>();
            string blanked = ScanFences(fences);
            var fenceStarts = new HashSet<int>();
            foreach (var fence in fences) fenceStarts.Add(fence.Start);
            var tooBig = new List<int[]>();
            var document = TryParse(blanked, NotationExtension.Document, _context);
            if (document == null)
            {
                // Some block is beyond Markdig's limits. Find the blocks that are, keep their lines as text, and read the rest as usual.
                blanked = BlankChunks(blanked, fenceStarts, tooBig, probe: true);
                document = TryParse(blanked, NotationExtension.Document, _context);
                if (document == null)
                {
                    // Every block reads alone but the whole does not: nothing is left to read as structure.
                    tooBig.Clear();
                    blanked = BlankChunks(blanked, fenceStarts, tooBig, probe: false);
                    document = TryParse(blanked, NotationExtension.Document, _context) ?? new Markdig.Syntax.MarkdownDocument();
                }
            }
            _context.EndParse();
            var items = new List<KeyValuePair<int, object>>();
            foreach (var block in document) items.Add(new KeyValuePair<int, object>(block.Span.Start, block));
            foreach (var fence in fences) items.Add(new KeyValuePair<int, object>(fence.Start, fence));
            foreach (var chunk in tooBig)
            {
                ReportTooDeep(chunk[0], FirstLineEnd(chunk[0], chunk[1]));
                items.Add(new KeyValuePair<int, object>(chunk[0], chunk));
            }
            items.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var item in items)
            {
                if (item.Value is Fence fenceBlock) ReadFence(fenceBlock);
                else if (item.Value is int[] chunk) ReadLiteralLines(chunk[0], chunk[1]);
                else if (item.Value is ThematicBreakBlock && fenceStarts.Contains(item.Key)) continue; // a blanked fence, see Interrupt
                else ReadBlock((Block)item.Value, blanked);
            }
        }

        // ---------------------------------------------------------------- Markdig's limits

        /// <summary>True for what Markdig throws when text nests too deeply or is too large for it (its depth limit), or when the stack is
        /// nearly used up. The notation's own parsers and the host's callbacks do not throw these.</summary>
        static bool IsParseLimit(Exception e)
            => e is InsufficientExecutionStackException
               || (e is ArgumentException && e.Message.IndexOf("too deeply nested", StringComparison.Ordinal) >= 0);

        /// <summary>Reads the text with Markdig; null when it is beyond Markdig's limits. The findings a parse that did not finish made are dropped.</summary>
        static Markdig.Syntax.MarkdownDocument? TryParse(string text, Markdig.MarkdownPipeline pipeline, ReaderContext context)
        {
            int mark = context.Count;
            try
            {
                return Markdig.Markdown.Parse(text, pipeline, context.NewParserContext());
            }
            catch (Exception e) when (IsParseLimit(e))
            {
                context.Truncate(mark);
                return null;
            }
        }

        const string TooDeepMessage = "This text is nested too deeply or is too large to read. Split it into smaller paragraphs, lists or tables. Its lines are kept as plain text.";

        void ReportTooDeep(int start, int end) => _context.Report(MarkdownDiagnosticIds.TooDeep, MarkdownSeverity.Error, TooDeepMessage, start, end);

        int FirstLineEnd(int start, int end)
        {
            int i = start;
            while (i < end && _source[i] != '\n' && _source[i] != '\r') i++;
            return i;
        }

        Run LiteralRun(string text) => new Run { Text = text, Ws = _options.WritingSystems.DefaultAnalysis };

        /// <summary>The lines of [start, end) as paragraphs of plain text, the way a block that cannot be read is kept.</summary>
        void ReadLiteralLines(int start, int end)
        {
            _context.Base = 0;
            int i = start;
            while (i < end)
            {
                int lineEnd = i;
                while (lineEnd < end && _source[lineEnd] != '\n' && _source[lineEnd] != '\r') lineEnd++;
                if (lineEnd > i) _paragraphs.Add(MakeParagraph(Names.Normal, new PropertyBag(), new List<Run> { LiteralRun(_source.Substring(i, lineEnd - i)) }, null));
                i = lineEnd < end && _source[lineEnd] == '\r' && lineEnd + 1 < end && _source[lineEnd + 1] == '\n' ? lineEnd + 2 : lineEnd + 1;
            }
        }

        /// <summary>Splits the text into blocks at its blank lines (and at the lines that stand for fences) and returns it with the blocks Markdig
        /// cannot read blanked out, same length, so every offset stays the source's. With <paramref name="probe"/> false every block is blanked.
        /// The blocks blanked are added to <paramref name="chunks"/> as {start, end} of their lines.</summary>
        string BlankChunks(string text, HashSet<int> fenceStarts, List<int[]> chunks, bool probe)
        {
            var blanked = new StringBuilder(text);
            int chunkStart = -1, chunkEnd = -1;
            int pos = 0;
            while (pos < text.Length)
            {
                int lineEnd = pos;
                while (lineEnd < text.Length && text[lineEnd] != '\n' && text[lineEnd] != '\r') lineEnd++;
                int next = lineEnd;
                if (next < text.Length) next += (text[next] == '\r' && next + 1 < text.Length && text[next + 1] == '\n') ? 2 : 1;
                int first = pos;
                while (first < lineEnd && (text[first] == ' ' || text[first] == '\t')) first++;
                bool boundary = first == lineEnd || fenceStarts.Contains(first);
                if (!boundary)
                {
                    if (chunkStart < 0) chunkStart = pos;
                    chunkEnd = lineEnd;
                }
                else FlushChunk(text, blanked, chunks, probe, ref chunkStart, chunkEnd);
                pos = next;
            }
            FlushChunk(text, blanked, chunks, probe, ref chunkStart, chunkEnd);
            return blanked.ToString();
        }

        void FlushChunk(string text, StringBuilder blanked, List<int[]> chunks, bool probe, ref int chunkStart, int chunkEnd)
        {
            if (chunkStart < 0) return;
            int start = chunkStart;
            chunkStart = -1;
            if (probe)
            {
                string chunk = text.Substring(start, chunkEnd - start);
                if (TryParse(chunk, NotationExtension.Document, new ReaderContext(_options, chunk)) != null) return;
            }
            chunks.Add(new[] { start, chunkEnd });
            Blank(blanked, start, chunkEnd);
        }

        List<Run> ReadInlineText()
        {
            string text = _source;
            _context.Base = 0;
            var document = TryParse(text, NotationExtension.Document, _context);
            if (document == null)
            {
                ReportTooDeep(0, FirstLineEnd(0, text.Length));
                return text.Length == 0 ? new List<Run>() : new List<Run> { LiteralRun(text) };
            }
            var runs = new List<Run>();
            bool first = true;
            foreach (var block in document)
            {
                if (block is ParagraphBlock paragraph)
                {
                    if (!first) runs.Add(new Run { Text = Chars.LineSeparatorText, Ws = _options.WritingSystems.DefaultAnalysis });
                    first = false;
                    runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
                    continue;
                }
                _context.Report(MarkdownDiagnosticIds.FieldBlockSyntax, MarkdownSeverity.Error,
                    "The abstract can't hold " + Describe(block) + ". Move it to the body.", block.Span.Start, block.Span.End + 1);
            }
            var p = new Paragraph { Runs = runs };
            p.Normalize();
            if (_options.NormalizeText) ToNfd(p.Runs);
            return p.Runs.ToList();
        }

        static string Describe(Block block)
        {
            if (block is HeadingBlock) return "a heading";
            if (block is ListBlock) return "a list";
            if (block is QuoteBlock) return "a block quote";
            if (block is Table) return "a table";
            return "block syntax";
        }

        // ---------------------------------------------------------------- fences

        /// <summary>Finds the <c>::: {...}</c> blocks and returns the text with them blanked out (same length, so Markdig's offsets
        /// stay the source's offsets).</summary>
        string ScanFences(List<Fence> fences)
        {
            string s = _source;
            var blanked = new StringBuilder(s);
            Fence? open = null;
            int openColons = 0;
            int openLineStart = 0;
            int pos = 0;
            while (pos < s.Length)
            {
                int lineStart = pos;
                int lineEnd = lineStart;
                while (lineEnd < s.Length && s[lineEnd] != '\n' && s[lineEnd] != '\r') lineEnd++;
                int next = lineEnd;
                if (next < s.Length) next += (s[next] == '\r' && next + 1 < s.Length && s[next + 1] == '\n') ? 2 : 1;
                if (open == null)
                {
                    if (TryOpener(lineStart, lineEnd, out int colons, out Fence? fence))
                    {
                        open = fence;
                        openColons = colons;
                        openLineStart = lineStart;
                    }
                }
                else
                {
                    if (IsCloser(lineStart, lineEnd, openColons))
                    {
                        open.Closed = true;
                        Blank(blanked, openLineStart, next);
                        Interrupt(blanked, open);
                        fences.Add(open);
                        open = null;
                    }
                    else open.Lines.Add(new[] { lineStart, lineEnd });
                }
                pos = next;
            }
            if (open != null)
            {
                _context.Report(MarkdownDiagnosticIds.UnclosedMark, MarkdownSeverity.Error, "This ::: block is never closed. Add a line with :::.", open.Start, Math.Min(_source.Length, open.Start + 3),
                    replacement: null);
                Blank(blanked, openLineStart, s.Length);
                Interrupt(blanked, open);
                fences.Add(open);
            }
            return blanked.ToString();
        }

        /// <summary>A blanked fence still ends the list, quote or paragraph before it: its first three characters become a thematic break,
        /// which the reader skips. Without it, two lists with a fence between them would read as one.</summary>
        static void Interrupt(StringBuilder text, Fence fence)
        {
            for (int i = 0; i < 3; i++) text[fence.Start + i] = '*';
        }

        static void Blank(StringBuilder text, int start, int end)
        {
            for (int i = start; i < end; i++) if (text[i] != '\n' && text[i] != '\r') text[i] = ' ';
        }

        bool TryOpener(int lineStart, int lineEnd, out int colons, out Fence? fence)
        {
            colons = 0;
            fence = null;
            string s = _source;
            int i = lineStart;
            while (i < lineEnd && i - lineStart < 3 && s[i] == ' ') i++;
            int colonStart = i;
            while (i < lineEnd && s[i] == ':') i++;
            colons = i - colonStart;
            if (colons < 3) return false;
            while (i < lineEnd && s[i] == ' ') i++;
            if (i >= lineEnd || s[i] != '{') return false;
            fence = new Fence { Start = colonStart };
            var status = AttributeScanner.TryScan(s, i, lineEnd, out var block, out int errorPos, out string error);
            if (status == ScanStatus.Ok)
            {
                int after = block!.End;
                while (after < lineEnd && s[after] == ' ') after++;
                if (after < lineEnd)
                    _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, "Nothing may follow the attribute block of a ::: line.", after, lineEnd);
                fence.Attrs = block;
            }
            else
            {
                _context.Base = 0;
                SpanParser.ReportBadBlock(_context, status, i, errorPos, error);
            }
            return true;
        }

        bool IsCloser(int lineStart, int lineEnd, int openColons)
        {
            string s = _source;
            int i = lineStart;
            while (i < lineEnd && i - lineStart < 3 && s[i] == ' ') i++;
            int colonStart = i;
            while (i < lineEnd && s[i] == ':') i++;
            if (i - colonStart < openColons) return false;
            while (i < lineEnd && (s[i] == ' ' || s[i] == '\t')) i++;
            return i == lineEnd;
        }

        void ReadFence(Fence fence)
        {
            var (style, props) = fence.Attrs == null ? (Names.Normal, new PropertyBag()) : ReadParagraphAttributes(fence.Attrs, false, out _);
            var lines = fence.Lines;
            int i = 0;
            while (i < lines.Count)
            {
                int start = lines[i][0], end = lines[i][1];
                if (IsBlank(start, end)) { i++; continue; }
                while (EndsWithOddBackslashes(start, end) && i + 1 < lines.Count && !IsBlank(lines[i + 1][0], lines[i + 1][1]))
                {
                    i++;
                    end = lines[i][1];
                }
                i++;
                ReadFenceParagraph(start, end, style, props);
            }
        }

        bool IsBlank(int start, int end)
        {
            for (int i = start; i < end; i++) if (_source[i] != ' ' && _source[i] != '\t') return false;
            return true;
        }

        bool EndsWithOddBackslashes(int start, int end)
        {
            int n = 0;
            for (int i = end - 1; i >= start && _source[i] == '\\'; i--) n++;
            return n % 2 == 1;
        }

        void ReadFenceParagraph(int start, int end, string style, PropertyBag props)
        {
            string text = _source.Substring(start, end - start);
            _context.Base = start;
            if (text.Trim() == "{empty}")
            {
                _context.Base = 0;
                _paragraphs.Add(MakeParagraph(style, props, new List<Run>(), null));
                return;
            }
            var document = TryParse(text, NotationExtension.InlineOnly, _context);
            if (document == null)
            {
                ReportTooDeep(start, end);
                _context.Base = 0;
                _paragraphs.Add(MakeParagraph(style, props, new List<Run> { LiteralRun(text) }, null));
                return;
            }
            var runs = new List<Run>();
            foreach (var block in document)
            {
                if (block is ParagraphBlock paragraph) runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
                else _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, "A reference definition is not supported. Escape the [ as \\[.", start, end);
            }
            _context.Base = 0;
            _paragraphs.Add(MakeParagraph(style, props, runs, null));
        }

        // ---------------------------------------------------------------- blocks

        void ReadBlock(Block block, string text)
        {
            _context.Base = 0;
            switch (block)
            {
                case ParagraphBlock paragraph:
                    ReadParagraph(paragraph, text);
                    break;
                case HeadingBlock heading:
                    ReadHeading(heading);
                    break;
                case ListBlock list:
                    ReadList(list);
                    break;
                case QuoteBlock quote:
                    ReadQuote(quote);
                    break;
                case Table table:
                    ReadTable(table);
                    break;
                case LinkReferenceDefinitionGroup group:
                    UnsupportedBlock(group, "A link reference definition is not supported. Escape the [ as \\[.");
                    break;
                case ThematicBreakBlock rule:
                    UnsupportedBlock(rule, "A thematic break is not supported. Escape the first character.");
                    break;
                case CodeBlock code:
                    UnsupportedBlock(code, "A code block is not supported.");
                    break;
                case HtmlBlock html:
                    UnsupportedBlock(html, "Raw HTML is not supported. Escape the < as \\<.");
                    break;
                default:
                    UnsupportedBlock(block, "This block syntax is not supported.");
                    break;
            }
        }

        void UnsupportedBlock(Block block, string message)
        {
            int start = block.Span.Start, end = block.Span.End + 1;
            // Raw HTML is fixed by escaping its angle brackets.
            string? fix = block is HtmlBlock && start >= 0 && end <= _source.Length && end >= start ? _source.Substring(start, end - start).Replace("<", "\\<") : null;
            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, message, start, end, fix);
        }

        void ReadParagraph(ParagraphBlock block, string text)
        {
            string raw = text.Substring(block.Span.Start, block.Span.Length);
            if (TryEmptyMarker(raw, block.Span.Start))
            {
                // Markdig read the line as text before the marker was recognised, so a quoted value that looks like a link or a
                // span has drawn findings of its own. The marker is read from the raw line, and those findings do not apply.
                _context.RemoveParsed(block.Span.Start, block.Span.End + 1);
                return;
            }
            _paragraphs.Add(MakeParagraph(Names.Normal, new PropertyBag(), InlineReader.Read(block.Inline, _context), null));
        }

        /// <summary>A line that is exactly <c>{empty ...}</c> is an empty paragraph (spec 3.1).</summary>
        bool TryEmptyMarker(string raw, int rawStart)
        {
            int first = 0;
            while (first < raw.Length && raw[first] == ' ') first++;
            int last = raw.Length;
            while (last > first && (raw[last - 1] == ' ' || raw[last - 1] == '\t')) last--;
            string line = raw.Substring(first, last - first);
            if (!line.StartsWith("{empty", StringComparison.Ordinal) || line.IndexOf('\n') >= 0 || line.IndexOf('\r') >= 0) return false;
            if (line.Length == 6 || (line[6] != ' ' && line[6] != '}')) return false;
            int at = rawStart + first;
            var status = AttributeScanner.TryScan(_source, at, rawStart + last, out var block, out int errorPos, out string error);
            if (status != ScanStatus.Ok || block!.End != rawStart + last)
            {
                if (status != ScanStatus.Ok) SpanParser.ReportBadBlock(_context, status, at, errorPos, error);
                return false;
            }
            var (style, props) = ReadParagraphAttributes(block, true, out string? defaultWs);
            _paragraphs.Add(MakeParagraph(style, props, new List<Run>(), defaultWs));
            return true;
        }

        void ReadHeading(HeadingBlock heading)
        {
            string? style = Names.Heading(heading.Level);
            if (style == null)
            {
                _context.Report(MarkdownDiagnosticIds.HeadingTooDeep, MarkdownSeverity.Error, "A heading of level " + heading.Level + " has no style. Use ### or a fenced style.",
                    heading.Span.Start, heading.Span.End + 1);
                style = "Heading " + heading.Level;
            }
            _paragraphs.Add(MakeParagraph(style, new PropertyBag(), InlineReader.Read(heading.Inline, _context), null));
        }

        void ReadList(ListBlock list)
        {
            string style = list.IsOrdered ? Names.NumberedList : Names.BulletedList;
            foreach (var child in list)
            {
                if (!(child is ListItemBlock item)) continue;
                bool first = true;
                foreach (var part in item)
                {
                    if (part is ParagraphBlock paragraph)
                    {
                        if (!first)
                            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, "A list item is one paragraph. Start a new item.", paragraph.Span.Start, paragraph.Span.End + 1);
                        first = false;
                        _paragraphs.Add(MakeParagraph(style, new PropertyBag(), InlineReader.Read(paragraph.Inline, _context), null));
                    }
                    else UnsupportedBlock(part, "A list item holds one paragraph; nested blocks are not supported.");
                }
            }
        }

        void ReadQuote(QuoteBlock quote)
        {
            foreach (var part in quote)
            {
                if (part is ParagraphBlock paragraph)
                    _paragraphs.Add(MakeParagraph(Names.BlockQuote, new PropertyBag(), InlineReader.Read(paragraph.Inline, _context), null));
                else UnsupportedBlock(part, "A block quote holds paragraphs only.");
            }
        }

        void ReadTable(Table table)
        {
            ReportAlignments(table);
            int headerCells = -1;
            int rowNumber = 0; // the header is row 1, the delimiter row is not counted
            foreach (var child in table)
            {
                if (!(child is TableRow row)) continue;
                rowNumber++;
                var cellRuns = new List<List<Run>>();
                foreach (var cellBlock in row)
                {
                    if (!(cellBlock is TableCell cell)) continue;
                    if (IsPhantom(cell)) continue;
                    var runs = new List<Run>();
                    foreach (var part in cell)
                        if (part is ParagraphBlock paragraph) runs.AddRange(InlineReader.Read(paragraph.Inline, _context));
                    foreach (var run in runs)
                    {
                        if (run.Text.IndexOf('\t') >= 0 || run.Text.IndexOf(Chars.LineSeparator) >= 0)
                        {
                            _context.Report(MarkdownDiagnosticIds.TableTabInCell, MarkdownSeverity.Error, "A tab or line break inside a cell can't be stored. Replace it with spaces.", cell.Span.Start, cell.Span.End + 1);
                            break;
                        }
                    }
                    cellRuns.Add(runs);
                }
                bool isHeader = row.IsHeader;
                if (isHeader) { headerCells = cellRuns.Count; ReportDelimiterWidth(table, headerCells); }
                else if (headerCells >= 0 && cellRuns.Count != headerCells)
                    _context.Report(MarkdownDiagnosticIds.TableRagged, MarkdownSeverity.Error,
                        "Row " + rowNumber + " has " + cellRuns.Count + (cellRuns.Count == 1 ? " cell" : " cells") + ", the header has " + headerCells + ". Add an empty cell or remove one.",
                        row.Span.Start, row.Span.End + 1);
                var joined = new List<Run>();
                for (int c = 0; c < cellRuns.Count; c++)
                {
                    if (c > 0) joined.Add(new Run { Text = "\t", Ws = _options.WritingSystems.DefaultAnalysis });
                    joined.AddRange(cellRuns[c]);
                }
                _paragraphs.Add(MakeParagraph(isHeader ? Names.TableHeader : Names.TableRow, new PropertyBag(), joined, null));
            }
        }

        /// <summary>The second line of the table, the delimiter row: its start, and its end before the line end.</summary>
        void DelimiterLine(Table table, out int lineStart, out int lineEnd)
        {
            string s = _source;
            int headerEnd = table.Span.Start;
            while (headerEnd < s.Length && s[headerEnd] != '\n' && s[headerEnd] != '\r') headerEnd++;
            lineStart = headerEnd < s.Length ? headerEnd + (s[headerEnd] == '\r' && headerEnd + 1 < s.Length && s[headerEnd + 1] == '\n' ? 2 : 1) : headerEnd;
            lineEnd = lineStart;
            while (lineEnd < s.Length && s[lineEnd] != '\n' && s[lineEnd] != '\r') lineEnd++;
        }

        /// <summary>A delimiter row with another number of cells than the header is a finding of the table's own (<c>fwmd.table.ragged</c>, the
        /// same fault as a row of another width); Markdig reads such a table by the header's width and says nothing.</summary>
        void ReportDelimiterWidth(Table table, int headerCells)
        {
            DelimiterLine(table, out int lineStart, out int lineEnd);
            int a = lineStart, b = lineEnd;
            while (a < b && (_source[a] == ' ' || _source[a] == '\t')) a++;
            while (b > a && (_source[b - 1] == ' ' || _source[b - 1] == '\t')) b--;
            if (a < b && _source[a] == '|') a++;
            if (b > a && _source[b - 1] == '|') b--;
            int cells = 1;
            for (int i = a; i < b; i++) if (_source[i] == '|') cells++;
            if (cells == headerCells) return;
            string replacement = "|" + string.Concat(Enumerable.Repeat(" --- |", headerCells));
            _context.Report(MarkdownDiagnosticIds.TableRagged, MarkdownSeverity.Error,
                "The delimiter row has " + cells + (cells == 1 ? " cell" : " cells") + ", the header has " + headerCells + ". Write one --- for each column of the header.",
                lineStart, lineEnd, replacement);
        }

        /// <summary>A column alignment (<c>:---</c>, <c>---:</c>, <c>:---:</c>) has nowhere to be stored (spec 3.1). Each aligned cell of the
        /// delimiter row, the second line of the table, is a finding of its own, and replacing it with <c>---</c> is the fix.</summary>
        void ReportAlignments(Table table)
        {
            bool aligned = false;
            foreach (var column in table.ColumnDefinitions) if (column.Alignment != null) aligned = true;
            if (!aligned) return;
            const string message = "A column alignment can't be stored. Use ---.";
            string s = _source;
            DelimiterLine(table, out int lineStart, out int lineEnd);
            bool any = false;
            int i = lineStart;
            while (i < lineEnd)
            {
                int cellEnd = i;
                while (cellEnd < lineEnd && s[cellEnd] != '|') cellEnd++;
                int a = i, b = cellEnd;
                while (a < b && (s[a] == ' ' || s[a] == '\t')) a++;
                while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t')) b--;
                if (a < b && (s[a] == ':' || s[b - 1] == ':'))
                {
                    any = true;
                    _context.Report(MarkdownDiagnosticIds.TableAlignment, MarkdownSeverity.Error, message, a, b, replacement: "---");
                }
                i = cellEnd + 1;
            }
            // Markdig saw an alignment this scan did not find: report the delimiter row as a whole.
            if (!any) _context.Report(MarkdownDiagnosticIds.TableAlignment, MarkdownSeverity.Error, message, lineStart, lineEnd);
        }

        /// <summary>Markdig pads short rows with empty cells that have no source.</summary>
        static bool IsPhantom(TableCell cell) => cell.Span.End < cell.Span.Start && cell.Span.Start == 0;

        // ---------------------------------------------------------------- paragraph attributes

        /// <summary>The attributes of a fence or of <c>{empty ...}</c>: style, paragraph properties and, for an empty paragraph, the
        /// writing system typing starts in.</summary>
        (string style, PropertyBag props) ReadParagraphAttributes(AttrBlock attrs, bool isEmpty, out string? defaultWs)
        {
            defaultWs = null;
            string? style = null;
            AttrItem? styleItem = null, wsItem = null, vItem = null;
            var rest = new List<AttrItem>();
            foreach (var item in attrs.Items)
            {
                if (item.Key == "empty" && item.IsFlag && isEmpty) continue;
                if (item.Key == "style" && !item.IsFlag)
                {
                    if (styleItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'style' appears twice. Keep one.", item.Start, item.End);
                    styleItem = styleItem ?? item;
                    continue;
                }
                if (isEmpty && item.Key == "v" && item.IsFlag) { vItem = item; continue; }
                if (isEmpty && item.Key == "ws" && !item.IsFlag) { wsItem = item; continue; }
                rest.Add(item);
            }
            var props = new PropertyBag();
            PropertyKeys.Apply(rest, props, _context, item => ReportUnknownParagraphItem(item, isEmpty));
            if (styleItem != null)
            {
                style = styleItem.Value!;
                var catalog = _options.Styles;
                if (catalog != null && style.Length > 0) _context.CheckStyle(style, true, styleItem.Start, styleItem.End);
            }
            if (props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var s133))
            {
                if (style != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "s133 is the style; it cannot be combined with style.", attrs.Start, attrs.End);
                style = s133;
            }
            props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            if (isEmpty)
            {
                if (wsItem != null && vItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "v and ws both name the writing system. Keep one.", vItem.Start, vItem.End);
                if (wsItem != null)
                {
                    defaultWs = wsItem.Value;
                    _context.CheckWritingSystem(defaultWs!, wsItem.Start, wsItem.End);
                }
                else if (vItem != null) defaultWs = _options.WritingSystems.DefaultVernacular;
            }
            return (style ?? Names.Normal, props);
        }

        void ReportUnknownParagraphItem(AttrItem item, bool isEmpty)
        {
            if (item.Key == "cs" || item.Key == "link" || item.Key == "guid" || item.Key == "url" || item.Key == "tool" || item.Key == "broken" || item.Key == "v" || item.Key == "ws")
            {
                _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Key + "' is for runs, not paragraphs.", item.Start, item.End);
                return;
            }
            var known = new List<string> { "style" };
            if (isEmpty) { known.Add("v"); known.Add("ws"); }
            known.AddRange(PropertyKeys.AllKeys());
            _context.ReportUnknown(item, known);
        }

        /// <summary>The text of every run as NFD, the form the editor stores (ADR 0018). Done once the runs are merged, so a mark and its base
        /// that Markdig handed over in two pieces compose as one string. Findings and objects keep the source's offsets: only the runs change.</summary>
        static void ToNfd(IList<Run> runs)
        {
            foreach (var run in runs) run.Text = TextNormalizer.Normalize(run.Text, NormalizationForm.FormD);
        }

        Paragraph MakeParagraph(string style, PropertyBag props, List<Run> runs, string? defaultWs)
        {
            var paragraph = new Paragraph { Style = style, Props = props.Clone(), Runs = runs };
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Rtl = paragraph.Props.TryGetInt(128, out int rtl) && rtl != 0;
            paragraph.Normalize();
            if (_options.NormalizeText) ToNfd(paragraph.Runs);
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : defaultWs ?? _options.WritingSystems.DefaultAnalysis;
            return paragraph;
        }
    }
}
