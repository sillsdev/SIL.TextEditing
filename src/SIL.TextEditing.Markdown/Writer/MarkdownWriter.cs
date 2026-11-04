using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Notation;
using SIL.TextEditing.Markdown.Reader;

namespace SIL.TextEditing.Markdown.Writer
{
    /// <summary>Writes paragraphs as canonical GDR Markdown (spec 5). Every paragraph is written, read back by the parser and
    /// compared with the model; when the first spelling does not read back, the writer moves down a ladder of safer spellings
    /// (attribute marks, attribute links, numeric references), so what it writes always reads back to the same paragraph.</summary>
    internal sealed class MarkdownWriter
    {
        readonly MarkdownOptions _options;

        MarkdownWriter(MarkdownOptions options) { _options = options; }

        MarkdownStyleNames Names => _options.StyleNames;

        sealed class Entry
        {
            public Paragraph Paragraph = null!;
            public PropertyBag Props = null!;
            public bool IsEmpty;
            public bool HasLineSeparator;
        }

        public static string Write(IEnumerable<Paragraph> paragraphs, MarkdownOptions options)
            => new MarkdownWriter(options).WriteAll(paragraphs);

        /// <summary>Writes the runs of one inline-only text (the abstract, spec 3.5).</summary>
        public static string WriteInline(IEnumerable<Run> runs, MarkdownOptions options)
        {
            var writer = new MarkdownWriter(options);
            var paragraph = new Paragraph { Runs = runs.Select(r => r.Clone()).ToList() };
            paragraph.Normalize();
            var cells = new List<List<Run>> { paragraph.Runs };
            var generator = new InlineGenerator(options, BlockContext.Plain());
            bool Reads(string candidate)
            {
                var back = MarkdownReader.ReadInline(candidate, options, out var context);
                var findings = context.Diagnostics;
                return !findings.Any(f => f.Severity == MarkdownSeverity.Error && MarkdownReader.IsSyntaxFinding(f.Id)) && Equivalence.Equal(paragraph.Runs, back);
            }
            // The text is read with and without a final line end: a file has one, a string in a database may not.
            string? text = writer.Search(generator, cells, candidate => Reads(candidate) && Reads(candidate + "\n"));
            return text ?? throw new MarkdownWriteException("The runs cannot be written so that they read back unchanged.");
        }

        string WriteAll(IEnumerable<Paragraph> paragraphs)
        {
            var entries = paragraphs.Select(Prepare).ToList();
            var blocks = new List<string>();
            int i = 0;
            while (i < entries.Count)
            {
                var entry = entries[i];
                if (entry.IsEmpty) { blocks.Add(EmptyLine(entry)); i++; continue; }
                if (entry.Paragraph.Style == Names.TableHeader && entry.Props.Count == 0)
                {
                    string? table = TryTable(entries, i, out int used);
                    if (table != null) { blocks.Add(table); i += used; continue; }
                }
                var kind = KindOf(entry);
                if (kind == Kind.Bullet || kind == Kind.Number)
                {
                    var lines = new List<string>();
                    int j = i;
                    while (j < entries.Count && !entries[j].IsEmpty && KindOf(entries[j]) == kind)
                    {
                        var context = kind == Kind.Bullet ? BlockContext.Bullet() : BlockContext.Number(j - i + 1);
                        lines.Add(context.FirstPrefix + PlainInline(entries[j], context));
                        j++;
                    }
                    blocks.Add(string.Join("\n", lines));
                    i = j;
                    continue;
                }
                if (kind != Kind.None)
                {
                    var context = kind == Kind.Quote ? BlockContext.Quote() : kind == Kind.Heading ? BlockContext.Heading(HeadingLevel(entry)) : BlockContext.Plain();
                    blocks.Add(context.FirstPrefix + PlainInline(entry, context));
                    i++;
                    continue;
                }
                int end = i + 1;
                while (end < entries.Count && SameFence(entry, entries[end])) end++;
                blocks.Add(Fence(entries, i, end));
                i = end;
            }
            return blocks.Count == 0 ? string.Empty : string.Join("\n\n", blocks) + "\n";
        }

        Entry Prepare(Paragraph paragraph)
        {
            var copy = Equivalence.Normalized(paragraph);
            if (copy.Style == null) copy.Style = string.Empty;
            var props = Equivalence.EffectiveProps(copy);
            return new Entry
            {
                Paragraph = copy,
                Props = props,
                IsEmpty = copy.Runs.Count == 0,
                HasLineSeparator = copy.Text.IndexOf(Chars.LineSeparator) >= 0,
            };
        }

        // ---------------------------------------------------------------- what a paragraph is written as

        enum Kind { None, Plain, Heading, Bullet, Number, Quote }

        int HeadingLevel(Entry e) => e.Paragraph.Style == Names.Heading1 ? 1 : e.Paragraph.Style == Names.Heading2 ? 2 : 3;

        Kind KindOf(Entry e)
        {
            if (e.Props.Count > 0) return Kind.None;
            string style = e.Paragraph.Style;
            if (style == Names.Normal) return Kind.Plain;
            if (style == Names.Heading1 || style == Names.Heading2 || style == Names.Heading3) return e.HasLineSeparator ? Kind.None : Kind.Heading;
            if (style == Names.BulletedList) return Kind.Bullet;
            if (style == Names.NumberedList) return Kind.Number;
            if (style == Names.BlockQuote) return Kind.Quote;
            return Kind.None;
        }

        bool SameFence(Entry first, Entry other)
            => !other.IsEmpty && other.Paragraph.Style == first.Paragraph.Style && other.Props.ContentEquals(first.Props) && KindOf(other) == Kind.None
               && !(other.Paragraph.Style == Names.TableHeader && other.Props.Count == 0);

        static List<List<Run>> OneCell(Entry e) => new List<List<Run>> { e.Paragraph.Runs };

        string PlainInline(Entry e, BlockContext context)
        {
            var generator = new InlineGenerator(_options, context);
            string? text = Search(generator, OneCell(e), candidate => ReadsBack(context.Wrap(context.FirstPrefix + candidate), e.Paragraph));
            return text ?? throw new MarkdownWriteException("A paragraph cannot be written so that it reads back unchanged: " + Describe(e));
        }

        static string Describe(Entry e) => "style '" + e.Paragraph.Style + "', " + e.Paragraph.Runs.Count + " runs, " + e.Paragraph.Length + " characters";

        bool ReadsBack(string blockText, Paragraph expected, int paragraphs = 1)
        {
            var read = MarkdownReader.ReadParagraphs(blockText + "\n", _options, out bool clean); // a block is always followed by a line end
            return clean && read.Count == paragraphs && Equivalence.Equal(expected, read[0]);
        }

        // ---------------------------------------------------------------- the ladder

        /// <summary>How many characters the search may have the parser read for one paragraph: it spends them in whole reads, so a longer
        /// paragraph gets fewer. Only a paragraph that needs thousands of escapes can come near it (the search takes about two reads per
        /// escape it keeps, times the log of the candidates it spares); what is left unsearched stays escaped, which still reads back.</summary>
        const long ReadBudgetChars = 5_000_000;

        const int MinReads = 64;

        /// <summary>Finds the first spelling of the inline text that <paramref name="accepts"/>. Within a spelling it tries no optional
        /// escape, then every one, then takes escapes away from the end back to the start, keeping each that is still needed. It takes them
        /// away in blocks: a block whose removal reads back goes in one read, one that does not is split in halves, the upper half first.
        /// Where a removal that fails is not rescued by another, which is the usual case, that is the same result as taking them one at a
        /// time, in about two reads per escape that stays, not one per candidate.</summary>
        string? Search(InlineGenerator generator, IReadOnlyList<List<Run>> cells, Func<string, bool> accepts, bool keepEscapes = false)
        {
            bool anyStar = false, anyParen = false;
            for (int step = 0; step < 4; step++)
            {
                GenMode mode = step == 0 ? new GenMode { StarMarks = true, ParenLinks = true }
                    : step == 1 ? new GenMode { ParenLinks = true }
                    : step == 2 ? new GenMode()
                    : new GenMode { Numeric = true };
                if (step == 1 && !anyStar) continue;
                if (step == 2 && !anyParen) continue;
                string bare = generator.Generate(cells, mode, null, out int count);
                if (step == 0) { anyStar = generator.UsedStar; anyParen = generator.UsedParenLink; }
                bool bareReads = accepts(bare);
                if (bareReads && (!keepEscapes || count == 0)) return bare;
                if (count == 0) continue;
                var mask = new bool[count];
                for (int k = 0; k < count; k++) mask[k] = true;
                string all = generator.Generate(cells, mode, mask, out _);
                if (!accepts(all)) { if (bareReads) return bare; continue; }
                if (keepEscapes) return all; // the caller wants every escape that is allowed, not the fewest
                // The state of the mask always reads back: it starts as "every escape" and only a removal that was just read is kept.
                long reads = Math.Max(MinReads, ReadBudgetChars / Math.Max(1, all.Length));
                int mid = count / 2;
                RemoveEscapes(generator, cells, mode, mask, mid, count, accepts, ref reads);
                RemoveEscapes(generator, cells, mode, mask, 0, mid, accepts, ref reads);
                return generator.Generate(cells, mode, mask, out _);
            }
            return null;
        }

        /// <summary>Takes the escapes in [low, high) away when the text still reads back without them; otherwise splits the block and
        /// tries the upper half, then the lower half.</summary>
        static void RemoveEscapes(InlineGenerator generator, IReadOnlyList<List<Run>> cells, GenMode mode, bool[] mask, int low, int high,
            Func<string, bool> accepts, ref long reads)
        {
            if (low >= high || reads <= 0) return;
            reads--;
            for (int k = low; k < high; k++) mask[k] = false;
            if (accepts(generator.Generate(cells, mode, mask, out _))) return;
            for (int k = low; k < high; k++) mask[k] = true;
            if (high - low == 1) return;
            int mid = low + (high - low) / 2;
            RemoveEscapes(generator, cells, mode, mask, mid, high, accepts, ref reads);
            RemoveEscapes(generator, cells, mode, mask, low, mid, accepts, ref reads);
        }

        // ---------------------------------------------------------------- empty paragraphs and fences

        string EmptyLine(Entry e)
        {
            var items = new List<PropertyKeys.Item> { new PropertyKeys.Item("empty", null) };
            if (e.Paragraph.Style != Names.Normal) items.Add(new PropertyKeys.Item("style", e.Paragraph.Style));
            var ws = _options.WritingSystems;
            if (e.Paragraph.DefaultWs != ws.DefaultAnalysis)
                items.Add(e.Paragraph.DefaultWs == ws.DefaultVernacular ? new PropertyKeys.Item("v", null) : new PropertyKeys.Item("ws", e.Paragraph.DefaultWs));
            items.AddRange(PropertyKeys.ToItems(e.Props));
            string line = Braces(items);
            if (!ReadsBack(line, e.Paragraph)) throw new MarkdownWriteException("An empty paragraph cannot be written so that it reads back unchanged: " + Describe(e));
            return line;
        }

        static string Braces(List<PropertyKeys.Item> items, bool inTable = false)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(items[i].Key);
                if (items[i].Value != null) sb.Append('=').Append(AttributeText.Value(items[i].Value!, inTable));
            }
            return sb.Append('}').ToString();
        }

        string Fence(List<Entry> entries, int start, int end)
        {
            var first = entries[start];
            var items = new List<PropertyKeys.Item>();
            if (first.Paragraph.Style != Names.Normal || first.Props.Count == 0) items.Add(new PropertyKeys.Item("style", first.Paragraph.Style));
            items.AddRange(PropertyKeys.ToItems(first.Props));
            string header = Braces(items);
            var context = BlockContext.Fence(header);
            var sb = new StringBuilder("::: ").Append(header);
            for (int i = start; i < end; i++)
            {
                var entry = entries[i];
                var generator = new InlineGenerator(_options, context);
                string? text = Search(generator, OneCell(entry), candidate => ReadsBack(context.Wrap(candidate), entry.Paragraph, 2));
                if (text == null) throw new MarkdownWriteException("A paragraph in a fenced block cannot be written so that it reads back unchanged: " + Describe(entry));
                sb.Append('\n').Append(text);
            }
            return sb.Append("\n:::").ToString();
        }

        // ---------------------------------------------------------------- tables

        static string Delimiter(int width) => "|" + string.Concat(Enumerable.Repeat(" --- |", width));


        static string PlaceholderHeader(int width) => "|" + string.Concat(Enumerable.Repeat(" x |", width));

        static string SyntheticRow(int width) => "|" + string.Concat(Enumerable.Repeat(" [x]{v} |", width));

        static bool CellEligible(Entry e)
        {
            if (e.HasLineSeparator) return false;
            foreach (var run in e.Paragraph.Runs) if (run.ObjData != null && run.Text.IndexOf(Chars.Tab) >= 0) return false;
            return true;
        }

        static List<List<Run>> SplitCells(Paragraph paragraph)
        {
            var cells = new List<List<Run>> { new List<Run>() };
            foreach (var run in paragraph.Runs)
            {
                if (run.ObjData != null) { cells[cells.Count - 1].Add(run); continue; }
                string[] pieces = run.Text.Split(Chars.Tab);
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (i > 0) cells.Add(new List<Run>());
                    if (pieces[i].Length > 0) cells[cells.Count - 1].Add(run.CloneWithText(pieces[i]));
                }
            }
            return cells;
        }

        /// <summary>Writes a header and the rows after it that fit. A bracket one cell leaves open can pair with a closing bracket in
        /// another row, which no single row shows, so every row is checked with a row of closing brackets after it: a bracket left open
        /// is caught in its own row. The whole table is read once more at the end. If rows still disturb one another there (a bracket
        /// opened in the header and closed in a row), every cell is written with all its optional escapes, which cannot pair with
        /// anything, and the table is read again; only when that does not read back either is the table left to the fences.</summary>
        string? TryTable(List<Entry> entries, int start, out int used)
        {
            used = 0;
            var header = entries[start];
            if (!CellEligible(header)) return null;
            var headerCells = SplitCells(header.Paragraph);
            int width = headerCells.Count;
            var context = new BlockContext { InTable = true };
            var expected = new List<Paragraph> { header.Paragraph };
            var cellsOf = new List<List<List<Run>>> { headerCells };
            var acceptsOf = new List<Func<string, bool>>();
            var lines = new List<string>();

            Func<string, bool> headerAccepts = candidate =>
            {
                var read = MarkdownReader.ReadParagraphs(candidate + "\n" + Delimiter(width) + "\n" + SyntheticRow(width) + "\n", _options, out bool clean);
                return clean && read.Count == 2 && Equivalence.Equal(header.Paragraph, read[0]);
            };
            string? headerLine = Search(new InlineGenerator(_options, context), headerCells, headerAccepts);
            if (headerLine == null) return null;
            acceptsOf.Add(headerAccepts);
            lines.Add(headerLine);
            int j = start + 1;
            while (j < entries.Count)
            {
                var row = entries[j];
                // A row with no runs is one empty cell, a row of a one-column table; in a wider table its cell count ends the table.
                if (row.Props.Count > 0 || row.Paragraph.Style != Names.TableRow || !CellEligible(row)) break;
                var cells = SplitCells(row.Paragraph);
                if (cells.Count != width) break;
                Func<string, bool> rowAccepts = candidate =>
                {
                    var read = MarkdownReader.ReadParagraphs(PlaceholderHeader(width) + "\n" + Delimiter(width) + "\n" + candidate + "\n" + SyntheticRow(width) + "\n", _options, out bool clean);
                    return clean && read.Count == 3 && Equivalence.Equal(row.Paragraph, read[1]);
                };
                string? line = Search(new InlineGenerator(_options, context), cells, rowAccepts);
                if (line == null) break;
                expected.Add(row.Paragraph);
                cellsOf.Add(cells);
                acceptsOf.Add(rowAccepts);
                lines.Add(line);
                j++;
            }
            if (!TableReadsBack(lines, width, expected))
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string? safe = Search(new InlineGenerator(_options, context), cellsOf[i], acceptsOf[i], keepEscapes: true);
                    if (safe == null) return null;
                    lines[i] = safe;
                }
                if (!TableReadsBack(lines, width, expected)) return null;
            }
            used = j - start;
            return TableText(lines, width);
        }

        static string TableText(List<string> lines, int width)
            => lines[0] + "\n" + Delimiter(width) + (lines.Count > 1 ? "\n" + string.Join("\n", lines.Skip(1)) : string.Empty);

        /// <summary>True when the table made of these lines (header first, then the rows) reads back as exactly the paragraphs expected.</summary>
        bool TableReadsBack(List<string> lines, int width, List<Paragraph> expected)
        {
            var all = MarkdownReader.ReadParagraphs(TableText(lines, width) + "\n", _options, out bool clean);
            if (!clean || all.Count != expected.Count) return false;
            for (int i = 0; i < expected.Count; i++) if (!Equivalence.Equal(expected[i], all[i])) return false;
            return true;
        }
    }
}
