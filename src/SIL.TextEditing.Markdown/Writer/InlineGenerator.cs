using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Writer
{
    /// <summary>The block a paragraph is written in, as far as writing its inline text needs to know.</summary>
    internal sealed class BlockContext
    {
        /// <summary>Text before the first line of the paragraph (<c>"# "</c>, <c>"- "</c>).</summary>
        public string FirstPrefix = string.Empty;
        /// <summary>Text before each later line, after a backslash line end (the list indent, <c>"> "</c>).</summary>
        public string ContinuationPrefix = string.Empty;
        /// <summary>Whether a line separator may be written as a backslash at the end of a line. A heading, a table cell and
        /// the empty paragraph marker are one line.</summary>
        public bool AllowsContinuation;
        public bool InTable;
        /// <summary>Wraps the inline text into the whole block text the parser reads back, for the check.</summary>
        public Func<string, string> Wrap = text => text;

        public static BlockContext Plain() => new BlockContext { AllowsContinuation = true };
        public static BlockContext Heading(int level) => new BlockContext { FirstPrefix = new string('#', level) + " " };
        public static BlockContext Bullet() => new BlockContext { FirstPrefix = "- ", ContinuationPrefix = "  ", AllowsContinuation = true };
        public static BlockContext Number(int number)
        {
            string marker = number.ToString(CultureInfo.InvariantCulture) + ". ";
            return new BlockContext { FirstPrefix = marker, ContinuationPrefix = new string(' ', marker.Length), AllowsContinuation = true };
        }
        public static BlockContext Quote() => new BlockContext { FirstPrefix = "> ", ContinuationPrefix = "> ", AllowsContinuation = true };
        public static BlockContext Fence(string header) => new BlockContext
        {
            AllowsContinuation = true,
            // A line follows the paragraph: a fence line that ends in a backslash continues into the next line, so the check needs one.
            Wrap = text => "::: " + header + "\n" + text + "\nx\n:::",
        };
    }

    internal struct GenMode
    {
        /// <summary>Bold and italic that CommonMark's flanking rules accept are written <c>**x**</c> and <c>*x*</c>; otherwise as attributes.</summary>
        public bool StarMarks;
        /// <summary>An external link whose URL is plain is written <c>[text](url)</c>; otherwise <c>[text]{url="..."}</c>.</summary>
        public bool ParenLinks;
        /// <summary>Every character of text is a numeric reference: the form that always reads back.</summary>
        public bool Numeric;
    }

    /// <summary>Writes the inline text of one paragraph (or of the cells of one table row) from its runs (spec 3.2 to 3.4). Optional
    /// backslash escapes are numbered; a mask says which ones are written, so the writer can search for the fewest that read back.</summary>
    internal sealed class InlineGenerator
    {
        const string Candidates = "\\!#&()*+-.:=>[]_{}~|";
        readonly MarkdownOptions _options;
        readonly BlockContext _context;
        StringBuilder _out = new StringBuilder();
        bool[]? _mask;
        int _candidate;
        bool _lineStart;
        GenMode _mode;
        string _cellText = string.Empty;
        int _cellOffset;

        public bool UsedStar { get; private set; }
        public bool UsedParenLink { get; private set; }

        public InlineGenerator(MarkdownOptions options, BlockContext context)
        {
            _options = options;
            _context = context;
        }

        MarkdownStyleNames Names => _options.StyleNames;

        /// <summary>Writes the cells (one, outside a table). With a mask, candidate <c>i</c> is escaped when <c>mask[i]</c>; without one none is.</summary>
        public string Generate(IReadOnlyList<List<Run>> cells, GenMode mode, bool[]? mask, out int candidates)
        {
            _mode = mode;
            _mask = mask;
            _candidate = 0;
            _out = new StringBuilder();
            UsedStar = false;
            UsedParenLink = false;
            bool table = _context.InTable;
            if (table) _out.Append('|');
            for (int c = 0; c < cells.Count; c++)
            {
                if (table) _out.Append(' ');
                WriteCell(cells[c]);
                if (table) _out.Append(" |");
            }
            candidates = _candidate;
            return _out.ToString();
        }

        void WriteCell(List<Run> runs)
        {
            _lineStart = true;
            var text = new StringBuilder();
            foreach (var run in runs) text.Append(run.Text);
            _cellText = text.ToString();
            _cellOffset = 0;
            for (int i = 0; i < runs.Count; i++)
            {
                WriteRun(runs[i], i == runs.Count - 1);
                _cellOffset += runs[i].Text.Length;
            }
        }

        // ---------------------------------------------------------------- runs

        void WriteRun(Run run, bool lastInCell)
        {
            if (run.ObjData == null) { WriteTextRun(run, lastInCell); return; }
            var described = _options.Objects.Describe(run.ObjData);
            if (described == null) { WriteGeneric(run); return; }
            switch (described.Kind)
            {
                case MarkdownObjectKind.Picture when run.Text == Chars.ObjectReplacement.ToString() && described.Guid != null:
                    WritePicture(run, described);
                    return;
                case MarkdownObjectKind.InternalLink:
                    WriteInternalLink(run, described);
                    return;
                case MarkdownObjectKind.ExternalLink when described.Url != null:
                    WriteExternalLink(run, described);
                    return;
                default:
                    WriteGeneric(run);
                    return;
            }
        }

        void WriteTextRun(Run run, bool lastInCell)
        {
            bool star = _mode.StarMarks && !_mode.Numeric && StarEligible(run.Text) && (IsOn(run, TextPropertyNumbers.Bold) || IsOn(run, TextPropertyNumbers.Italic));
            bool strong = star && IsOn(run, TextPropertyNumbers.Bold);
            bool emphasis = star && IsOn(run, TextPropertyNumbers.Italic);
            var items = RunItems(run, strong, emphasis, null, generic: false);
            if (star && items.Count > 0)
            {
                // The run has other attributes, so it is bracketed anyway, and bold or italic is one more attribute inside the braces
                // ([x]{v bold=on}). Stars inside the brackets would be a spelling the spec does not show, and stars around the brackets
                // need the neighbours to be spaces or punctuation (a**[x]{v}**b does not read as bold).
                star = strong = emphasis = false;
                items = RunItems(run, false, false, null, generic: false);
            }
            bool bracket = items.Count > 0;
            if (star) UsedStar = true;
            if (bracket) Raw("[");
            string marks = strong && emphasis ? "***" : strong ? "**" : emphasis ? "*" : string.Empty;
            if (marks.Length > 0) Raw(marks);
            Text(run.Text, allowBreak: !bracket && marks.Length == 0, endsLine: lastInCell && !bracket && marks.Length == 0);
            if (marks.Length > 0) Raw(marks);
            if (bracket) Raw("]" + Block(items));
        }

        static bool IsOn(Run run, int number)
            => run.Props.IntProps.TryGetValue(number, out var v) && v.Variation == PropertyKeys.VarEnum && v.Value == TextPropertyNumbers.ToggleOn;

        /// <summary>CommonMark accepts a <c>*</c> run only when its first and last characters are neither white space nor punctuation
        /// (spec 3.2); a line separator or any control character keeps the attribute form too.</summary>
        static bool StarEligible(string text)
        {
            if (text.Length == 0) return false;
            if (text.IndexOf(Chars.LineSeparator) >= 0) return false;
            return Plain(text, 0) && Plain(text, text.Length - 1);
        }

        static bool Plain(string text, int index)
        {
            if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
            var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
            switch (category)
            {
                case UnicodeCategory.UppercaseLetter:
                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.TitlecaseLetter:
                case UnicodeCategory.ModifierLetter:
                case UnicodeCategory.OtherLetter:
                case UnicodeCategory.DecimalDigitNumber:
                case UnicodeCategory.LetterNumber:
                case UnicodeCategory.OtherNumber:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Object data the host does not describe: <c>[text]{s6="..."}</c>.</summary>
        void WriteGeneric(Run run)
        {
            var items = RunItems(run, false, false, null, generic: true);
            Raw("[");
            Text(run.Text, allowBreak: false, endsLine: false);
            Raw("]" + Block(items));
        }

        void WritePicture(Run run, MarkdownObject picture)
        {
            var items = RunItems(run, false, false, null, generic: false);
            Raw("![");
            Text(picture.Caption ?? string.Empty, allowBreak: false, endsLine: false);
            Raw("](fw:picture/" + picture.Guid!.Value.ToString("D") + ")");
            if (items.Count > 0) Raw(Block(items));
        }

        void WriteInternalLink(Run run, MarkdownObject link)
        {
            string kind = link.LinkKind ?? "obj";
            var linkItems = new List<PropertyKeys.Item>();
            bool nameForm = link.Name != null && run.Text == link.Name && _options.Objects.IsKnownLinkKind(kind);
            if (!nameForm) linkItems.Add(new PropertyKeys.Item("link", kind + ":" + (link.Name ?? string.Empty)));
            if (link.Guid != null) linkItems.Add(new PropertyKeys.Item("guid", link.Guid.Value.ToString("D")));
            if (link.Tool != null) linkItems.Add(new PropertyKeys.Item("tool", link.Tool));
            if (link.Url != null) linkItems.Add(new PropertyKeys.Item("url", link.Url));
            if (link.Broken) linkItems.Add(new PropertyKeys.Item("broken", null));
            var items = RunItems(run, false, false, linkItems, generic: false, isLink: true);
            if (nameForm)
            {
                Raw("[[" + kind + ":");
                Text(link.Name!, allowBreak: false, endsLine: false, bare: true);
                Raw("]]");
                if (items.Count > 0) Raw(Block(items));
            }
            else
            {
                Raw("[");
                Text(run.Text, allowBreak: false, endsLine: false);
                Raw("]" + Block(items));
            }
        }

        void WriteExternalLink(Run run, MarkdownObject link)
        {
            string url = link.Url!;
            bool paren = _mode.ParenLinks && UrlIsPlain(url, _context.InTable);
            var leading = new List<PropertyKeys.Item>();
            if (!paren) leading.Add(new PropertyKeys.Item("url", url));
            var items = RunItems(run, false, false, leading, generic: false, isLink: true);
            Raw("[");
            Text(run.Text, allowBreak: false, endsLine: false);
            if (paren)
            {
                UsedParenLink = true;
                Raw("](" + url + ")");
                if (items.Count > 0) Raw(Block(items));
            }
            else Raw("]" + Block(items));
        }

        /// <summary>A destination Markdown reads back as written: visible ASCII with no bracket, parenthesis, angle bracket, backslash,
        /// backtick or entity-looking ampersand.</summary>
        static bool UrlIsPlain(string url, bool inTable)
        {
            if (url.Length == 0) return false;
            for (int i = 0; i < url.Length; i++)
            {
                char c = url[i];
                if (c <= ' ' || c > '~') return false;
                if ("()<>[]\\`".IndexOf(c) >= 0) return false;
                if (c == '|' && inTable) return false;
                if (c == '&')
                {
                    int j = i + 1;
                    while (j < url.Length && (char.IsLetterOrDigit(url[j]) || url[j] == '#')) j++;
                    if (j > i + 1 && j < url.Length && url[j] == ';') return false;
                }
            }
            return true;
        }

        // ---------------------------------------------------------------- attributes

        /// <summary>The attribute items of a run in canonical order (spec 3.3): writing system, <c>cs</c>, the link items, then the
        /// properties. <paramref name="strong"/> and <paramref name="emphasis"/> are the toggles written as marks instead.</summary>
        List<PropertyKeys.Item> RunItems(Run run, bool strong, bool emphasis, List<PropertyKeys.Item>? linkItems, bool generic, bool isLink = false)
        {
            var items = new List<PropertyKeys.Item>();
            var ws = _options.WritingSystems;
            if (run.Ws != ws.DefaultAnalysis)
                items.Add(run.Ws == ws.DefaultVernacular ? new PropertyKeys.Item("v", null) : new PropertyKeys.Item("ws", run.Ws));
            var bag = run.Props.Clone();
            bool hasStyle = bag.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out string? style);
            bool styleAsCs = false;
            if (isLink)
            {
                if (!hasStyle) { items.Add(new PropertyKeys.Item("cs", "none")); }
                else if (style == "none") { /* spelled s133="none" below, in the numbered group */ }
                else if (style != Names.Hyperlink) { items.Add(new PropertyKeys.Item("cs", style)); styleAsCs = true; }
                else styleAsCs = true;
            }
            else if (hasStyle && style != "none")
            {
                items.Add(new PropertyKeys.Item("cs", style));
                styleAsCs = true;
            }
            if (linkItems != null) items.AddRange(linkItems);
            if (strong) bag.IntProps.Remove(TextPropertyNumbers.Bold);
            if (emphasis) bag.IntProps.Remove(TextPropertyNumbers.Italic);
            if (generic) bag.StrProps[6] = run.ObjData!;
            if (styleAsCs) bag.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            items.AddRange(PropertyKeys.ToItems(bag));
            return items;
        }

        string Block(List<PropertyKeys.Item> items)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(items[i].Key);
                if (items[i].Value != null) sb.Append('=').Append(AttributeText.Value(items[i].Value!, _context.InTable));
            }
            return sb.Append('}').ToString();
        }

        // ---------------------------------------------------------------- text

        void Raw(string text)
        {
            if (text.Length == 0) return;
            _out.Append(text);
            _lineStart = false;
        }

        static string Ref(int codePoint) => "&#x" + codePoint.ToString("X", CultureInfo.InvariantCulture) + ";";

        /// <summary>Writes text with the escapes of spec 3.4. <paramref name="bare"/> is text inside <c>[[kind:...]]</c>, where only
        /// backslash and numeric references mean anything.</summary>
        void Text(string text, bool allowBreak, bool endsLine, bool bare = false)
        {
            int n = text.Length;
            // Where the spaces that end the line start: found once, so a long run of spaces is not asked about space by space.
            int trailingFrom = n;
            if (endsLine) while (trailingFrom > 0 && text[trailingFrom - 1] == ' ') trailingFrom--;
            for (int k = 0; k < n; k++)
            {
                char c = text[k];
                if (_mode.Numeric)
                {
                    if (char.IsHighSurrogate(c) && k + 1 < n && char.IsLowSurrogate(text[k + 1])) { Raw(Ref(char.ConvertToUtf32(c, text[k + 1]))); k++; }
                    else Raw(Ref(c));
                    continue;
                }
                if (c == Chars.LineSeparator)
                {
                    int at = _cellOffset + k;
                    bool backslash = allowBreak && _context.AllowsContinuation && at > 0 && at < _cellText.Length - 1
                        && _cellText[at - 1] != Chars.LineSeparator && _cellText[at + 1] != Chars.LineSeparator;
                    if (backslash) { _out.Append("\\\n").Append(_context.ContinuationPrefix); _lineStart = true; }
                    else Raw(Ref(c));
                    continue;
                }
                if (c == ' ')
                {
                    bool run = (k > 0 && text[k - 1] == ' ') || (k + 1 < n && text[k + 1] == ' ');
                    bool trailing = k >= trailingFrom;
                    if (_lineStart || run || trailing) Raw(Ref(' ')); else Raw(" ");
                    continue;
                }
                if (char.IsHighSurrogate(c) && k + 1 < n && char.IsLowSurrogate(text[k + 1])) { _out.Append(c).Append(text[k + 1]); _lineStart = false; k++; continue; }
                if (c < ' ' || (c >= '\x7F' && c <= '\x9F') || char.IsWhiteSpace(c) || char.IsSurrogate(c) || c == Chars.ObjectReplacement || c == Chars.ByteOrderMark)
                {
                    Raw(Ref(c));
                    continue;
                }
                if (c == '<' || c == '`' || (c == '|' && _context.InTable)) { Raw("\\" + c); continue; }
                if (c == '\\' && (k == n - 1 || text[k + 1] == Chars.LineSeparator) && !bare) { Raw("\\\\"); continue; } // a literal trailing backslash is \\, so it never reads as a line end (spec 3.4)
                if (Candidates.IndexOf(c) >= 0 && !bare)
                {
                    int index = _candidate++;
                    if (_mask != null && _mask[index]) Raw("\\" + c); else Raw(c.ToString());
                    continue;
                }
                if (bare && (c == '\\' || c == ']' || c == '&'))
                {
                    int index = _candidate++;
                    if ((_mask != null && _mask[index]) || c == '\\') Raw("\\" + c); else Raw(c.ToString());
                    continue;
                }
                Raw(c.ToString());
            }
        }

    }
}
