using System;
using System.Collections.Generic;
using System.Text;
using Markdig.Syntax.Inlines;
using SIL.TextEditing.Markdown.Notation;
using MdTable = Markdig.Extensions.Tables;

namespace SIL.TextEditing.Markdown.Reader
{
    internal enum CsState { Unset, None, Explicit }

    /// <summary>The formatting in force while reading inlines. Inner spans and marks override outer ones.</summary>
    internal sealed class Fmt
    {
        public string? Ws;
        public bool Vernacular;
        public CsState Cs;
        public string? CsName;
        public PropertyBag Props = new PropertyBag();
        public string? ObjData;
        public bool IsLink;
        /// <summary>Which object span the text belongs to (0 for none): the pieces of one link are one run.</summary>
        public int Scope;

        public Fmt Clone() => new Fmt { Ws = Ws, Vernacular = Vernacular, Cs = Cs, CsName = CsName, Props = Props.Clone(), ObjData = ObjData, IsLink = IsLink, Scope = Scope };
    }

    /// <summary>Turns the inline tree Markdig built into runs (spec 3.2).</summary>
    internal sealed class InlineReader
    {
        readonly ReaderContext _context;
        readonly MarkdownOptions _options;
        readonly List<Run> _runs = new List<Run>();

        public InlineReader(ReaderContext context)
        {
            _context = context;
            _options = context.Options;
        }

        public List<Run> Runs => _runs;

        /// <summary>Reads one paragraph's inlines. Hard breaks written <c>\</c> become U+2028, soft breaks one space.</summary>
        public static List<Run> Read(ContainerInline? inlines, ReaderContext context)
        {
            var reader = new InlineReader(context);
            if (inlines != null) reader.Walk(inlines, new Fmt());
            return reader._runs;
        }

        int Abs(int offset) => _context.Base + offset;

        void Add(string text, Fmt fmt)
        {
            if (text.Length == 0) return;
            var run = new Run { Text = text, Ws = ResolveWs(fmt), Props = fmt.Props.Clone(), ObjData = fmt.ObjData };
            string? cs = fmt.Cs == CsState.Explicit ? fmt.CsName : fmt.Cs == CsState.None ? null : (fmt.IsLink ? _options.StyleNames.Hyperlink : null);
            if (cs != null) run.Props.StrProps[TextPropertyNumbers.NamedStyle] = cs;
            // Markdig hands over one link's text in pieces (an escaped bracket is a piece of its own). Runs with object data never
            // merge afterwards, so the pieces of one object span are joined here.
            if (fmt.Scope != 0 && fmt.Scope == _lastScope && _runs.Count > 0)
            {
                var last = _runs[_runs.Count - 1];
                if (last.Ws == run.Ws && last.ObjData == run.ObjData && last.Props.ContentEquals(run.Props))
                {
                    last.Text += text;
                    return;
                }
            }
            _lastScope = fmt.Scope;
            _runs.Add(run);
        }

        int _scopes;
        int _lastScope;

        string ResolveWs(Fmt fmt)
        {
            if (fmt.Ws != null) return fmt.Ws;
            return fmt.Vernacular ? _options.WritingSystems.DefaultVernacular : _options.WritingSystems.DefaultAnalysis;
        }

        /// <summary>Reads the inlines under <paramref name="root"/> in document order. The walk keeps its own stack instead of calling itself,
        /// so how deeply the text nests (three thousand open brackets, say) cannot overflow the thread's stack.</summary>
        void Walk(ContainerInline root, Fmt rootFmt)
        {
            var pending = new Stack<KeyValuePair<Inline?, Fmt>>();
            pending.Push(new KeyValuePair<Inline?, Fmt>(root.FirstChild, rootFmt));
            while (pending.Count > 0)
            {
                var entry = pending.Pop();
                Inline? child = entry.Key;
                if (child == null) continue;
                Fmt fmt = entry.Value;
                pending.Push(new KeyValuePair<Inline?, Fmt>(child.NextSibling, fmt)); // what follows this child, after everything inside it
                switch (child)
                {
                    case LiteralInline literal:
                        Add(literal.Content.ToString(), fmt);
                        break;
                    case CharRefInline charRef:
                        Add(charRef.Text, fmt);
                        break;
                    case HtmlEntityInline entity:
                        Add(entity.Transcoded.ToString(), fmt);
                        break;
                    case EmphasisInline emphasis:
                    {
                        var inner = fmt.Clone();
                        int number = emphasis.DelimiterCount >= 2 ? TextPropertyNumbers.Bold : TextPropertyNumbers.Italic;
                        inner.Props.IntProps[number] = new IntPropValue(PropertyKeys.VarEnum, TextPropertyNumbers.ToggleOn);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(emphasis.FirstChild, inner));
                        break;
                    }
                    case LineBreakInline lineBreak:
                        Add(lineBreak.IsHard && lineBreak.IsBackslash ? Chars.LineSeparatorText : " ", fmt);
                        break;
                    case SpanInline span:
                        pending.Push(new KeyValuePair<Inline?, Fmt>(span.FirstChild, ReadSpan(span, fmt)));
                        break;
                    case NameLinkInline nameLink:
                        ReadNameLink(nameLink, fmt);
                        break;
                    case AttrsInline _:
                        break; // consumed by the link before it
                    case LinkInline link:
                    {
                        var inner = ReadLink(link, fmt);
                        if (inner != null) pending.Push(new KeyValuePair<Inline?, Fmt>(link.FirstChild, inner));
                        break;
                    }
                    case Markdig.Extensions.Tables.PipeTableDelimiterInline pipe:
                        Add("|", fmt);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(pipe.FirstChild, fmt));
                        break;
                    case CodeInline code:
                        Unsupported(code.Span.Start, code.Span.End, "A code span is not supported. Escape the backtick as \\`.");
                        Add(Source(code.Span.Start, code.Span.End), fmt);
                        break;
                    case AutolinkInline autolink:
                        Unsupported(autolink.Span.Start, autolink.Span.End, "An autolink is not supported. Escape the < as \\<.");
                        Add(Source(autolink.Span.Start, autolink.Span.End), fmt);
                        break;
                    case HtmlInline html:
                        Unsupported(html.Span.Start, html.Span.End, "Raw HTML is not supported. Escape the < as \\<.");
                        Add(Source(html.Span.Start, html.Span.End), fmt);
                        break;
                    case DelimiterInline delimiter:
                        Add(delimiter.ToLiteral(), fmt);
                        pending.Push(new KeyValuePair<Inline?, Fmt>(delimiter.FirstChild, fmt));
                        break;
                    case ContainerInline other:
                        pending.Push(new KeyValuePair<Inline?, Fmt>(other.FirstChild, fmt));
                        break;
                    default:
                        break;
                }
            }
        }

        string Source(int start, int end)
        {
            int a = Abs(start), b = Abs(end) + 1;
            if (a < 0 || b > _context.Source.Length || b < a) return string.Empty;
            return _context.Source.Substring(a, b - a);
        }

        /// <summary>Reports unsupported inline markup. The fix is the same text with each angle bracket and backtick escaped (or the
        /// first bracket of a reference link).</summary>
        void Unsupported(int start, int end, string message)
        {
            string observed = Source(start, end);
            string? fix = null;
            if (observed.IndexOf('<') >= 0 || observed.IndexOf('`') >= 0) fix = observed.Replace("<", "\\<").Replace("`", "\\`");
            else if (observed.StartsWith("[", StringComparison.Ordinal)) fix = "\\" + observed;
            _context.Report(MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownSeverity.Error, message, Abs(start), Abs(end) + 1, fix);
        }

        // -------------------------------------------------------------- spans and links

        /// <summary>The formatting of the text inside a span.</summary>
        Fmt ReadSpan(SpanInline span, Fmt fmt)
        {
            // The text form [text]{link=...} is placed on the whole span: from the opening bracket to the closing brace.
            return ApplyAttrs(span.Attrs, fmt, new ObjectForm { Start = span.OpenAt, End = span.Attrs.End - 1 });
        }

        void ReadNameLink(NameLinkInline link, Fmt fmt)
        {
            var form = new ObjectForm { Kind = link.Kind, Name = link.Name, IsInternal = true, Start = link.Span.Start, End = link.Span.End };
            var inner = link.Attrs == null ? Finish(fmt.Clone(), form, null) : ApplyAttrs(link.Attrs, fmt, form);
            Add(link.Name, inner);
        }

        /// <summary>Reads a link or picture; returns the formatting of the text inside it, or null when there is none to read.</summary>
        Fmt? ReadLink(LinkInline link, Fmt fmt)
        {
            AttrBlock? attrs = (link.NextSibling as AttrsInline)?.Attrs;
            if (link.IsAutoLink)
            {
                Unsupported(link.Span.Start, link.Span.End, "An autolink is not supported. Escape the < as \\<.");
                Add(Source(link.Span.Start, link.Span.End), fmt);
                return null;
            }
            if (link.IsImage)
            {
                var form = new ObjectForm { IsPicture = true, Url = link.Url, Start = link.Span.Start, End = attrs == null ? link.Span.End : attrs.End - 1 };
                var picture = attrs == null ? Finish(fmt.Clone(), form, null) : ApplyAttrs(attrs, fmt, form);
                Add(Run.ObjectReplacement.ToString(), picture);
                return null;
            }
            if (link.IsShortcut)
            {
                Unsupported(link.Span.Start, link.Span.End, "A reference link is not supported. Escape the [ as \\[.");
                return fmt;
            }
            var linkForm = new ObjectForm { Url = link.Url ?? string.Empty, Start = link.Span.Start, End = attrs == null ? link.Span.End : attrs.End - 1 };
            return attrs == null ? Finish(fmt.Clone(), linkForm, null) : ApplyAttrs(attrs, fmt, linkForm);
        }

        /// <summary>What the syntax around an attribute block says about the object the run carries.</summary>
        sealed class ObjectForm
        {
            public bool IsPicture;
            public bool IsInternal;
            public string? Kind;
            public string? Name;
            public string? Url;
            public Guid? Guid;
            public string? Tool;
            public bool Broken;
            public string? S6;
            public bool HasObject => IsPicture || IsInternal || Url != null || S6 != null;
            public int Start, End;
        }

        Fmt ApplyAttrs(Notation.AttrBlock attrs, Fmt parent, ObjectForm form)
        {
            var fmt = parent.Clone();
            var rest = new List<AttrItem>();
            AttrItem? wsItem = null, vItem = null, csItem = null, linkItem = null, urlItem = null, s6Item = null, s133Item = null;
            foreach (var item in attrs.Items)
            {
                bool handled = true;
                switch (item.Key)
                {
                    case "v" when item.IsFlag: if (vItem != null) Duplicate(item); vItem = vItem ?? item; break;
                    case "broken" when item.IsFlag: form.Broken = true; form.IsInternal = true; break;
                    case "ws" when !item.IsFlag: if (wsItem != null) Duplicate(item); wsItem = wsItem ?? item; break;
                    case "cs" when !item.IsFlag: if (csItem != null) Duplicate(item); csItem = csItem ?? item; break;
                    case "link" when !item.IsFlag: if (linkItem != null) Duplicate(item); linkItem = linkItem ?? item; break;
                    case "url" when !item.IsFlag: if (urlItem != null) Duplicate(item); urlItem = urlItem ?? item; break;
                    case "guid" when !item.IsFlag:
                        if (form.Guid != null) Duplicate(item);
                        else if (System.Guid.TryParse(item.Value, out var g)) { form.Guid = g; form.IsInternal = true; }
                        else Malformed(item, "guid takes a GUID");
                        break;
                    case "tool" when !item.IsFlag: form.Tool = item.Value; form.IsInternal = true; break;
                    case "s6" when !item.IsFlag: if (s6Item != null) Duplicate(item); s6Item = s6Item ?? item; break;
                    case "s133" when !item.IsFlag: s133Item = item; handled = false; break;
                    case "ws" or "cs" or "link" or "guid" or "tool" or "url" or "s6":
                        _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Key + "' is not a flag; it takes a value: " + item.Key + "=...", Abs(item.Start), Abs(item.End));
                        break;
                    case "v" or "broken":
                        _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Key + "' is a flag and takes no value: write " + item.Key + " alone.", Abs(item.Start), Abs(item.End));
                        break;
                    default: handled = false; break;
                }
                if (!handled) rest.Add(item);
            }
            if (wsItem != null && vItem != null) _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "v and ws both name the writing system. Keep one.", Abs(vItem.Start), Abs(vItem.End));
            if (wsItem != null)
            {
                fmt.Ws = wsItem.Value!;
                fmt.Vernacular = false;
                if (!_options.WritingSystems.IsKnown(fmt.Ws))
                    _context.ReportUnknownWritingSystem(fmt.Ws, Abs(wsItem.Start), Abs(wsItem.End));
            }
            else if (vItem != null) { fmt.Ws = null; fmt.Vernacular = true; }
            if (csItem != null)
            {
                if (s133Item != null) Duplicate(s133Item);
                if (csItem.Value == "none") { fmt.Cs = CsState.None; fmt.CsName = null; }
                else
                {
                    fmt.Cs = CsState.Explicit;
                    fmt.CsName = csItem.Value;
                    var catalog = _options.Styles;
                    if (catalog != null && !catalog.IsCharacterStyle(csItem.Value!))
                        _context.ReportUnknownStyle(csItem.Value!, "character", Abs(csItem.Start), Abs(csItem.End));
                }
                fmt.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
            }
            if (linkItem != null)
            {
                string v = linkItem.Value!;
                int colon = v.IndexOf(':');
                if (colon <= 0) Malformed(linkItem, "link takes kind:Name");
                else { form.Kind = v.Substring(0, colon); form.Name = v.Substring(colon + 1); form.IsInternal = true; }
            }
            if (urlItem != null)
            {
                if (form.Url != null && !form.IsInternal) Duplicate(urlItem);
                form.Url = urlItem.Value;
            }
            if (s6Item != null) form.S6 = s6Item.Value;
            PropertyKeys.Apply(rest, fmt.Props, _context, item => ReportUnknownItem(item));
            if (s133Item != null && csItem == null && fmt.Props.StrProps.TryGetValue(TextPropertyNumbers.NamedStyle, out var styleName)) { fmt.Cs = CsState.Explicit; fmt.CsName = styleName; }
            return Finish(fmt, form, attrs);
        }

        void ReportUnknownItem(AttrItem item)
        {
            if (item.Key == "style") { _context.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'style' is for paragraphs. A run's character style is cs.", Abs(item.Start), Abs(item.End)); return; }
            _context.ReportUnknown(item, RunKeys());
        }

        static IEnumerable<string> RunKeys()
        {
            foreach (var k in new[] { "v", "ws", "cs", "link", "guid", "tool", "url", "broken" }) yield return k;
            foreach (var k in PropertyKeys.AllKeys()) yield return k;
        }

        void Duplicate(AttrItem item) => _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'" + item.Key + "' appears twice. Keep one.", Abs(item.Start), Abs(item.End));
        void Malformed(AttrItem item, string message) => _context.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, message, Abs(item.Start), Abs(item.End));

        /// <summary>Gives the formatting its object data once the whole attribute block has been read.</summary>
        Fmt Finish(Fmt fmt, ObjectForm form, AttrBlock? attrs)
        {
            if (!form.HasObject) return fmt;
            int start = Abs(form.Start), end = Abs(form.End) + 1;
            if (form.S6 != null)
            {
                if (form.IsInternal || form.IsPicture || form.Url != null)
                    _context.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "s6 is the object data; it cannot be combined with a link or picture.", start, end);
                fmt.ObjData = form.S6;
                fmt.Scope = ++_scopes;
                return fmt;
            }
            var target = new MarkdownObject();
            if (form.IsPicture)
            {
                target.Kind = MarkdownObjectKind.Picture;
                const string prefix = "fw:picture/";
                string url = form.Url ?? string.Empty;
                if (url.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParse(url.Substring(prefix.Length), out var pictureGuid)) target.Guid = pictureGuid;
                else
                {
                    _context.Report(MarkdownDiagnosticIds.PictureUnknown, MarkdownSeverity.Error, "A picture is written ![caption](fw:picture/<guid>).", start, end);
                    return fmt;
                }
            }
            else if (form.IsInternal)
            {
                target.Kind = MarkdownObjectKind.InternalLink;
                target.LinkKind = form.Kind ?? "obj";
                target.Name = form.Name;
                target.Guid = form.Guid;
                target.Tool = form.Tool;
                target.Url = form.Url;
                target.Broken = form.Broken;
            }
            else
            {
                target.Kind = MarkdownObjectKind.ExternalLink;
                target.Url = form.Url;
            }
            target.Source = _context.RangeOf(start, end);
            _context.AddObject(target);
            bool resolved;
            string objData;
            MarkdownProblem? problem;
            if (_options.Objects is IObjectResolver resolver) resolved = resolver.TryResolve(target, out objData, out problem);
            else
            {
                resolved = _options.Objects.TryCreateObjData(target, out objData, out string? reason);
                problem = reason == null ? null : new MarkdownProblem(reason);
            }
            if (resolved)
            {
                target.ObjData = objData;
                fmt.ObjData = objData;
                fmt.Scope = ++_scopes;
                if (!form.IsPicture) fmt.IsLink = true;
            }
            // A host's warning or suggestion comes with the data (a deleted target); a failure is always an error.
            if (problem != null || !resolved)
            {
                problem = problem ?? new MarkdownProblem("The object cannot be resolved.");
                string id = problem.Id ?? (form.IsPicture ? MarkdownDiagnosticIds.PictureUnknown : MarkdownDiagnosticIds.LinkNotFound);
                _context.Report(id, resolved ? problem.Severity : MarkdownSeverity.Error, problem.Message, start, end, problem.Replacement,
                    problem.Choices.Count > 0 ? problem.Choices : null, problem.Nearest.Count > 0 ? problem.Nearest : null);
            }
            return fmt;
        }
    }
}
