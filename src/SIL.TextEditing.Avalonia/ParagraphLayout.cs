using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SIL.TextEditing.Avalonia
{
    sealed class RunTextProperties : TextRunProperties
    {
        readonly Typeface _typeface;
        readonly double _size;
        readonly TextDecorationCollection? _decorations;
        readonly IBrush _foreground;
        readonly CultureInfo _culture;
        readonly FontFeatureCollection? _features;
        readonly IBrush? _background;
        readonly BaselineAlignment _baseline;
        readonly IBrush? _squiggle;
        readonly double _offset;

        public RunTextProperties(Typeface typeface, double size, IBrush foreground, TextDecorationCollection? decorations, CultureInfo culture, FontFeatureCollection? features, IBrush? background = null, BaselineAlignment baseline = BaselineAlignment.Baseline, IBrush? squiggle = null, double offset = 0)
        {
            _squiggle = squiggle;
            _offset = offset;
            _background = background;
            _baseline = baseline;
            _typeface = typeface;
            // Avalonia 11 joins neighbouring runs whose properties compare equal into one run, so runs with different offsets (which it is
            // not given) would lose them. A tiny size difference per offset keeps them apart; it is far below anything that can be drawn.
            _size = size + offset * 0.00005;
            _foreground = foreground;
            _decorations = decorations;
            _culture = culture;
            _features = features;
        }

        public override Typeface Typeface => _typeface;
        public override double FontRenderingEmSize => _size;
        public override TextDecorationCollection? TextDecorations => _decorations;
        public override IBrush? ForegroundBrush => _foreground;
        /// <summary>The run's background colour. Avalonia is not given it (it would paint over the selection): the layout paints it itself, below the selection.</summary>
        public IBrush? Back => _background;
        /// <summary>The colour of the run's squiggle underline, or null for none. Avalonia has no wavy decoration: the layout draws it itself, after the text.</summary>
        public IBrush? Squiggle => _squiggle;
        /// <summary>How far the run's baseline is raised above the line's (negative: lowered), in pixels. Avalonia is not given it: the layout shifts the run when it draws it.</summary>
        public double Offset => _offset;
        public override IBrush? BackgroundBrush => null;
        public override BaselineAlignment BaselineAlignment => _baseline;
        public override CultureInfo? CultureInfo => _culture;
        public override FontFeatureCollection? FontFeatures => _features;    }

    sealed class ParagraphProperties : TextParagraphProperties
    {
        readonly FlowDirection _flowDirection;
        readonly TextRunProperties _default;

        readonly TextAlignment _alignment;

        public ParagraphProperties(FlowDirection flowDirection, TextRunProperties defaultProperties, TextAlignment alignment = TextAlignment.Start)
        {
            _flowDirection = flowDirection;
            _default = defaultProperties;
            _alignment = alignment;
        }

        public override FlowDirection FlowDirection => _flowDirection;
        public override TextAlignment TextAlignment => _alignment;
        public override double LineHeight => double.NaN;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => _default;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override double Indent => 0;
    }

    /// <summary>An inline object (an ORC) drawn as an image box; length 1 like the U+FFFC it stands for. It carries the
    /// run properties so the formatter can resolve a bidi level for it (Avalonia issue 21792 concerns text-less
    /// drawable runs in right-to-left paragraphs; see <see cref="ParagraphLayout.Build"/>).</summary>
    sealed class ObjectRun : DrawableTextRun
    {
        readonly IImage? _image;
        readonly Size _size;
        readonly TextRunProperties _properties;

        public ObjectRun(IImage? image, Size size, TextRunProperties properties)
        {
            _image = image;
            _size = size;
            _properties = properties;
        }

        public override Size Size => _size;
        public override double Baseline => _size.Height * 0.85;
        public override TextRunProperties Properties => _properties;

        public override void Draw(DrawingContext context, Point origin)
        {
            var r = new Rect(origin, _size);
            if (_image != null) context.DrawImage(_image, r);
            else context.DrawRectangle(Brushes.Gray, null, r);
        }
    }

    sealed class ParagraphSource : ITextSource
    {
        public struct Segment
        {
            public int Start, End;
            public TextRunProperties Properties;
            public bool IsObject;
            public string? Data;
        }

        public string Text = string.Empty;
        public List<Segment> Segments = new List<Segment>();
        public Func<string, IImage?>? ObjectImage;
        public bool ObjectAsGlyph;

        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= Text.Length) return null;
            foreach (var s in Segments)
            {
                if (textSourceIndex < s.Start || textSourceIndex >= s.End) continue;
                if (s.IsObject && ObjectAsGlyph)
                    return new TextCharacters(Text.AsMemory(textSourceIndex, 1), new RunTextProperties(s.Properties.Typeface, 28, s.Properties.ForegroundBrush ?? Brushes.Black, null, s.Properties.CultureInfo ?? CultureInfo.InvariantCulture, null));
                if (s.IsObject)
                    return new ObjectRun(s.Data == null ? null : ObjectImage?.Invoke(s.Data), new Size(32, 24), s.Properties);
                return new TextCharacters(Text.AsMemory(textSourceIndex, s.End - textSourceIndex), s.Properties);
            }
            return null;
        }
    }

    /// <summary>The runs of a paragraph that have a baseline offset: effective text ranges and how far each is raised in pixels (negative: lowered).</summary>
    sealed class OffsetMap
    {
        readonly List<int> _starts = new List<int>(), _ends = new List<int>();
        readonly List<double> _values = new List<double>();
        public bool IsEmpty => _starts.Count == 0;

        public void Add(int start, int end, double offset)
        {
            _starts.Add(start);
            _ends.Add(end);
            _values.Add(offset);
        }

        public double At(int index)
        {
            for (int i = 0; i < _starts.Count; i++)
                if (index >= _starts[i] && index < _ends[i]) return _values[i];
            return 0;
        }
    }

    public sealed class LineInfo
    {
        public LineInfo(TextLine line, double y, double x = 0, double height = -1, double baselineInSlot = double.NaN)
            : this(line, y, x, height, baselineInSlot, null)
        {
        }

        internal LineInfo(TextLine line, double y, double x, double height, double baselineInSlot, OffsetMap? offsets)
        {
            Line = line;
            Y = y;
            X = x;
            Height = height < 0 ? line.Height : height;
            _baselineInSlot = baselineInSlot;
            _offsets = offsets;
            _runs = ParagraphLayout.RunOffsets(line, offsets, out var raise, out var lower);
            HasOffsets = _runs != null;
            Raise = raise;
            Lower = lower;
            Start = line.FirstTextSourceIndex;
            Length = line.Length;
            End = Start + Length;
        }

        /// <summary>The empty line after a hard line break that ends the paragraph. <paramref name="metrics"/> is a formatted line
        /// that only gives its height; it has no text of this paragraph, so nothing but the caret place is asked of it.</summary>
        internal LineInfo(TextLine metrics, double y, double x, double height, int at, double caretX, double baselineInSlot = double.NaN)
        {
            Line = metrics;
            Y = y;
            X = x;
            Height = height;
            Start = at;
            Length = 0;
            End = at;
            IsEmpty = true;
            EmptyCaretX = caretX;
            _baselineInSlot = baselineInSlot;
        }

        /// <summary>True for the empty last line a trailing hard line break makes: it holds no text, only a caret place.</summary>
        public bool IsEmpty { get; }
        /// <summary>Where the caret sits on an empty line, in paragraph coordinates (following the alignment).</summary>
        internal double EmptyCaretX { get; }

        public TextLine Line { get; }
        /// <summary>The top of the line's slot in paragraph coordinates; a line height rule can make the slot taller (or shorter)
        /// than the text, which sits at the bottom of it.</summary>
        public double Y { get; }
        /// <summary>The left edge of the line's available width in paragraph coordinates; the line's own alignment offset is inside it.</summary>
        public double X { get; }
        /// <summary>The height of the slot.</summary>
        public double Height { get; }
        /// <summary>Where the text of the line starts vertically: at the bottom of the slot, or, under an exact line height, wherever puts the
        /// baseline at the same fraction of the slot as the text's own (the text then overflows the slot both ways when it is taller).</summary>
        public double TextY => double.IsNaN(_baselineInSlot) ? Y + Height - Lower - Line.Height : Y + _baselineInSlot - Line.Baseline;
        readonly double _baselineInSlot;
        /// <summary>True when a run on the line has a baseline offset, so the line is drawn run by run (<see cref="Draw"/>).</summary>
        public bool HasOffsets { get; }
        readonly OffsetMap? _offsets;
        /// <summary>Every run of a line that has shifted runs, in drawing order, with its offset; null for a line with none.</summary>
        internal List<KeyValuePair<DrawableTextRun, double>>? _runs;
        /// <summary>How far the shifted runs reach above the text's own top, and below its bottom: room the slot makes for them.</summary>
        public double Raise { get; }
        public double Lower { get; }

        /// <summary>Draws the line with its text at <paramref name="top"/> plus <see cref="TextY"/>. A line with no shifted run is Avalonia's own
        /// drawing; otherwise each run is drawn where Avalonia would draw it, less the run's offset.</summary>
        public void Draw(DrawingContext context, double top)
        {
            double y = top + TextY;
            if (!HasOffsets) { Line.Draw(context, new Point(X, y)); return; }
            double x = X + Line.Start;
            foreach (var run in _runs!)
            {
                run.Key.Draw(context, new Point(x, y + ParagraphLayout.RunTop(Line, run.Key) - run.Value));
                x += run.Key.Size.Width;
            }
        }

        /// <summary>The boxes of the characters from <paramref name="from"/> to <paramref name="to"/> (effective offsets), one per run piece, with
        /// each piece's baseline offset: x in paragraph coordinates, y from the top of the line's text (before the offset is applied).</summary>
        internal List<KeyValuePair<Rect, double>> Pieces(int from, int to)
        {
            var result = new List<KeyValuePair<Rect, double>>();
            foreach (var tb in Line.GetTextBounds(from, to - from))
            {
                var parts = _offsets != null && HasOffsets ? tb.TextRunBounds : null;
                if (parts == null || parts.Count == 0)
                    result.Add(new KeyValuePair<Rect, double>(new Rect(X + tb.Rectangle.X, tb.Rectangle.Y, tb.Rectangle.Width, tb.Rectangle.Height), 0));
                else
                    foreach (var part in parts)
                        result.Add(new KeyValuePair<Rect, double>(new Rect(X + part.Rectangle.X, part.Rectangle.Y, part.Rectangle.Width, part.Rectangle.Height), _offsets!.At(part.TextSourceCharacterIndex)));
            }
            return result;
        }
        public int Start { get; }
        public int Length { get; }
        public int End { get; }
    }

    /// <summary>The layout of one paragraph with TextFormatter: lines, hit testing, caret, selection and visual caret
    /// stops. Offsets are model offsets unless a name says "effective"; the two differ only while an IME preedit
    /// string is shown inside the paragraph.</summary>
    public sealed class ParagraphLayout : IDisposable
    {
        ParagraphLayout(Paragraph paragraph, double layoutWidth)
        {
            Paragraph = paragraph;
            LayoutWidth = layoutWidth;
        }

        public Paragraph Paragraph { get; }
        public List<LineInfo> Lines { get; } = new List<LineInfo>();
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double LayoutWidth { get; }
        public string EffectiveText { get; private set; } = string.Empty;
        public int PreeditAt { get; private set; } = -1;
        public int PreeditLength { get; private set; }
        public bool Rtl => Paragraph.Rtl;

        /// <summary>The paragraph's resolved formatting (alignment, indents, spacing, borders, numbering).</summary>
        public ParaFormat Format { get; private set; } = new ParaFormat();
        /// <summary>The paragraph box (inside the margins, border included): its left edge, top and width in paragraph coordinates.</summary>
        Rect Box { get; set; }
        /// <summary>The left edge of the text area, and its width.</summary>
        public double ContentLeft { get; private set; }
        public double ContentWidth { get; private set; }
        /// <summary>The bullet or number text this layout was built for (empty for none); the field rebuilds the layout when the number changes.</summary>
        public string LabelText { get; private set; } = string.Empty;
        /// <summary>The space after the paragraph above (0 for the first), which this layout was built against: the gap between two
        /// paragraphs is the larger of the margin top and space after plus space before, not their sum.</summary>
        public double PreviousSpaceAfter { get; private set; }
        /// <summary>True when lines past the style's maximum were left out.</summary>
        public bool Truncated { get; private set; }

        /// <summary>True when the paragraph ends in a hard line break whose empty last line the line limit leaves out. No text is
        /// hidden (so <see cref="Truncated"/> stays false), but the caret cannot sit after the break.</summary>
        public bool HiddenTrailingBreak { get; private set; }

        /// <summary>The last model offset a caret can reach: the end of the text, or the end of the last line shown when the line
        /// limit hid the rest (or the empty line after a trailing break). As in FieldWorks, a caret never sits in hidden text.</summary>
        public int VisibleEnd => !(Truncated || HiddenTrailingBreak) || Lines.Count == 0 ? Paragraph.Length : Math.Min(Paragraph.Length, ToModel(TextEnd(Lines[Lines.Count - 1])));
        double _borderLeft, _borderTop, _borderRight, _borderBottom;
        IBrush? _fill, _borderBrush;
        CharFormat? _labelFormat;
        WritingSystemInfo? _labelWs;
        string _labelWsTag = string.Empty;
        IBrush? _labelForeground;
        string? _labelText;
        TextLine? _label;

        /// <summary>Builds the text run properties for a run: the writing system's font and size, then the paragraph style,
        /// character style and the run's own properties, resolved through <paramref name="styles"/>.</summary>
        static RunTextProperties MakeProperties(Paragraph paragraph, Run run, WritingSystemRegistry writingSystems, IBrush foreground, IStyleResolver? styles, bool preedit = false)
        {
            var ws = writingSystems.Get(run.Ws);
            var f = FormatResolver.ResolveChar(paragraph, run, styles, ws.Size);
            return MakeProperties(f, ws, run.Ws, foreground, preedit);
        }

        static RunTextProperties MakeProperties(CharFormat f, WritingSystemInfo ws, string wsTag, IBrush foreground, bool preedit = false)
        {
            var weight = (FontWeight)f.Weight;
            var style = f.Italic ? FontStyle.Italic : FontStyle.Normal;
            double size = f.SizePt > 0 ? f.SizePt : ws.Size;
            if (f.Superscript != 0) size *= CharFormat.DefaultSuperscriptScale;
            var featureList = new List<string>();
            if (!string.IsNullOrEmpty(ws.Features)) featureList.AddRange(ws.Features!.Split(','));
            if (!string.IsNullOrEmpty(f.FontFeatures)) featureList.AddRange(f.FontFeatures!.Split(','));
            FontFeatureCollection? features = null;
            foreach (var item in featureList)
            {
                if (item.Trim().Length == 0) continue;
                features ??= new FontFeatureCollection();
                features.Add(FontFeature.Parse(item.Trim()));
            }
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(wsTag); }
            catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
            IBrush text = ColorBrush(f.ForeColor) ?? foreground;
            var decorations = preedit ? Decorations(3, null, text) : Decorations(f.Underline, f.UnderColor, text);
            var baseline = f.Superscript == 1 ? BaselineAlignment.Superscript : f.Superscript == 2 ? BaselineAlignment.Subscript : BaselineAlignment.Baseline;
            return new RunTextProperties(new Typeface(new FontFamily(f.FontFamily ?? ws.Font), style, weight), size, text, decorations, culture, features, ColorBrush(f.BackColor), baseline, !preedit && f.Underline == 6 ? ColorBrush(f.UnderColor) ?? text : null, preedit ? 0 : f.OffsetPt);
        }

        /// <summary>Where a run's top is below the top of its line, as Avalonia draws it: baselines aligned, a superscript at the top, a subscript at the bottom.</summary>
        internal static double RunTop(TextLine line, DrawableTextRun run)
        {
            switch (run.Properties?.BaselineAlignment)
            {
                case BaselineAlignment.Superscript: return 0;
                case BaselineAlignment.Subscript: return line.Height - run.Size.Height;
                default: return line.Baseline - run.Baseline;
            }
        }

        /// <summary>The runs of a line with their baseline offsets, or null when the line has no shifted run (<paramref name="raise"/> and
        /// <paramref name="lower"/> are then 0). Otherwise they are how far the shifted runs reach above the line's top and below its bottom.
        /// Avalonia 11 hands back its own copy of each run's properties, so a run is matched to its offset by where it is in the text.</summary>
        internal static List<KeyValuePair<DrawableTextRun, double>>? RunOffsets(TextLine line, OffsetMap? offsets, out double raise, out double lower)
        {
            raise = 0;
            lower = 0;
            if (offsets == null || offsets.IsEmpty || line.Length <= 0) return null;
            var starts = new Dictionary<TextRun, int>();
            foreach (var tb in line.GetTextBounds(line.FirstTextSourceIndex, line.Length))
                foreach (var part in tb.TextRunBounds)
                    if (part.TextRun != null) starts[part.TextRun] = part.TextSourceCharacterIndex;
            var result = new List<KeyValuePair<DrawableTextRun, double>>();
            bool any = false;
            foreach (var run in line.TextRuns)
            {
                if (!(run is DrawableTextRun d)) continue;
                double offset = starts.TryGetValue(run, out int at) ? offsets.At(at) : 0;
                result.Add(new KeyValuePair<DrawableTextRun, double>(d, offset));
                if (offset == 0) continue;
                any = true;
                double top = RunTop(line, d) - offset;
                raise = Math.Max(raise, -top);
                lower = Math.Max(lower, top + d.Size.Height - line.Height);
            }
            return any ? result : null;
        }
        /// <summary>The paragraph's resolved formatting, sized from the writing system of its first run.</summary>
        internal static ParaFormat ResolveFormat(Paragraph paragraph, WritingSystemRegistry writingSystems, IStyleResolver? styles)
        {
            string ws = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : paragraph.DefaultWs;
            return FormatResolver.ResolveParagraph(paragraph, styles, writingSystems.Get(ws).Size);
        }

        /// <summary>A FieldWorks BGR colour as a brush, or null for none or a special value.</summary>
        internal static IBrush? ColorBrush(int? bgr)
        {
            if (bgr == null || !CharFormat.TryColor(bgr.Value, out var r, out var g, out var b)) return null;
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        /// <summary>The decorations of a FieldWorks underline type (1 dotted, 2 dashed, 3 single, 4 double, 5 strikethrough);
        /// 0 and the squiggle (6), which Avalonia cannot draw and the layout draws itself (<see cref="DrawSquiggles"/>), give none.</summary>
        static TextDecorationCollection? Decorations(int type, int? color, IBrush text)
        {
            if (type < 1 || type > 5) return null;
            var colour = ColorBrush(color);
            if (type == 3 && colour == null) return new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } };
            var stroke = colour ?? text;
            TextDecoration Line(TextDecorationLocation where, double offset = 0, params double[] dashes)
            {
                var d = new TextDecoration { Location = where, Stroke = stroke, StrokeThickness = 1, StrokeThicknessUnit = TextDecorationUnit.Pixel, StrokeOffsetUnit = TextDecorationUnit.Pixel, StrokeOffset = offset };
                if (dashes.Length > 0) d.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double>(dashes);
                return d;
            }
            var result = new TextDecorationCollection();
            switch (type)
            {
                case 1: result.Add(Line(TextDecorationLocation.Underline, 0, 2, 2)); break;
                case 2: result.Add(Line(TextDecorationLocation.Underline, 0, 6, 3)); break;
                case 3: result.Add(Line(TextDecorationLocation.Underline)); break;
                case 4: result.Add(Line(TextDecorationLocation.Underline)); result.Add(Line(TextDecorationLocation.Underline, 2)); break;
                case 5: result.Add(Line(TextDecorationLocation.Strikethrough)); break;
            }
            return result;
        }

        /// <summary>
        /// Lays out a paragraph. <paramref name="objectAsGlyph"/> is the workaround for Avalonia issue 21792 (a text-less
        /// DrawableTextRun in a right-to-left paragraph gets the wrong visual order in 11.3 and 12.1): the object is
        /// laid out as an EM SPACE sized to the picture and the picture is painted over its text bounds.
        /// </summary>
        public static ParagraphLayout Build(Paragraph paragraph, double width, WritingSystemRegistry writingSystems, Func<string, IImage?>? objectImage, IBrush foreground, bool objectAsGlyph = false, int preeditAt = -1, string? preedit = null, Run? preeditTemplate = null, IStyleResolver? styles = null, string? label = null, double previousSpaceAfter = 0)
        {
            var layout = new ParagraphLayout(paragraph, width);
            layout.PreviousSpaceAfter = previousSpaceAfter;
            layout.LabelText = label ?? string.Empty;
            var source = new ParagraphSource { ObjectImage = objectImage, ObjectAsGlyph = objectAsGlyph };
            var sb = new StringBuilder();
            int pos = 0;
            bool hasPreedit = !string.IsNullOrEmpty(preedit);
            string firstWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : paragraph.DefaultWs;
            bool preeditDone = !hasPreedit;
            var preeditRun = preeditTemplate ?? new Run { Ws = firstWs };

            void AddSegment(string t, Run r)
            {
                if (t.Length == 0) return;
                int a = sb.Length;
                sb.Append(t);
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = MakeProperties(paragraph, r, writingSystems, foreground, styles) });
            }

            void AddPreedit()
            {
                int a = sb.Length;
                sb.Append(preedit);
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = MakeProperties(paragraph, preeditRun, writingSystems, foreground, styles, true) });
                preeditDone = true;
            }

            foreach (var r in paragraph.Runs)
            {
                if (!preeditDone && preeditAt == pos) AddPreedit();
                if (!preeditDone && !r.IsObject && preeditAt > pos && preeditAt < pos + r.Text.Length)
                {
                    int k = preeditAt - pos;
                    AddSegment(r.Text.Substring(0, k), r);
                    AddPreedit();
                    AddSegment(r.Text.Substring(k), r);
                }
                else if (r.IsObject)
                {
                    int s0 = sb.Length;
                    sb.Append(objectAsGlyph ? " " : r.Text);
                    source.Segments.Add(new ParagraphSource.Segment { Start = s0, End = sb.Length, Properties = MakeProperties(paragraph, r, writingSystems, foreground, styles), IsObject = true, Data = r.ObjData });
                }
                else AddSegment(r.Text, r);
                pos += r.Text.Length;
            }
            if (!preeditDone) AddPreedit();
            layout.PreeditAt = hasPreedit ? preeditAt : -1;
            layout.PreeditLength = hasPreedit ? preedit!.Length : 0;
            source.Text = sb.ToString();
            layout.EffectiveText = source.Text;
            foreach (var seg in source.Segments)
            {
                if (seg.IsObject || !(seg.Properties is RunTextProperties rp)) continue;
                if (rp.Back != null) layout._backgrounds.Add(new KeyValuePair<int, KeyValuePair<int, IBrush>>(seg.Start, new KeyValuePair<int, IBrush>(seg.End, rp.Back)));
                if (rp.Offset != 0) layout._offsets.Add(seg.Start, seg.End, rp.Offset);
                if (rp.Squiggle != null) layout._squiggles.Add(new KeyValuePair<int, KeyValuePair<int, IBrush>>(seg.Start, new KeyValuePair<int, IBrush>(seg.End, rp.Squiggle)));
            }

            var defaultRun = paragraph.Runs.Count > 0 ? paragraph.Runs[0] : new Run { Ws = paragraph.DefaultWs };
            var defaultProperties = MakeProperties(paragraph, defaultRun, writingSystems, foreground, styles);
            var baseWs = writingSystems.Get(defaultRun.Ws);
            var pf = ResolveFormat(paragraph, writingSystems, styles);
            layout.Format = pf;
            layout.ApplyBox(pf, width, paragraph.Rtl, foreground);
            if (pf.BulNumScheme != 0)
            {
                // The label takes the paragraph style's character format and the number font, not the first run's own look (its
                // superscript, underline or background).
                var lf = FormatResolver.ResolveChar(paragraph, new Run { Ws = defaultRun.Ws }, styles, baseWs.Size);
                if (pf.BulNumFontInfo.Length > 0) lf.ApplyOver(BulletNumbers.ParseFontInfo(pf.BulNumFontInfo));
                if (BulletNumbers.IsBullet(pf.BulNumScheme)) lf.FontFamily = BulletNumbers.BulletFont;
                layout._labelFormat = lf;
                layout._labelWs = baseWs;
                layout._labelWsTag = defaultRun.Ws;
                layout._labelForeground = foreground;
            }
            var paragraphProperties = new ParagraphProperties(paragraph.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, defaultProperties, Alignment(pf.Align));
            double w = Math.Max(layout.ContentWidth, 10);
            double firstIndent = pf.FirstIndent;
            layout.PlaceFirstLine(w, firstIndent, source, paragraphProperties);
            double lineHeightRule = pf.LineHeight;
            if (source.Text.Length == 0)
            {
                // A line cannot be formatted from zero characters through the public API; use the font metrics.
                var glyphTypeface = defaultProperties.Typeface.GlyphTypeface;
                double fontSize = defaultProperties.FontRenderingEmSize;
                double lineHeight = glyphTypeface != null
                    ? (-glyphTypeface.Metrics.Descent - glyphTypeface.Metrics.Ascent + glyphTypeface.Metrics.LineGap) / (double)glyphTypeface.Metrics.DesignEmHeight * fontSize
                    : fontSize * 1.3;
                if (lineHeight <= 0) lineHeight = fontSize * 1.3;
                double slot = layout.SlotHeight(lineHeight);
                layout.EmptyLineTop = layout.TopInset + slot - lineHeight;
                if (pf.ExactLineHeight && pf.LineHeight > 0)
                {
                    // An exact line height keeps the baseline at the same share of the slot as on a line with text: a space gives the metrics.
                    var spaceSource = new ParagraphSource { Text = " " };
                    spaceSource.Segments.Add(new ParagraphSource.Segment { Start = 0, End = 1, Properties = defaultProperties });
                    using (var metrics = TextFormatter.Current.FormatLine(spaceSource, 0, w, paragraphProperties, null))
                        if (metrics != null && metrics.Height > 0)
                        {
                            layout._exactAscentFraction = metrics.Baseline / metrics.Height;
                            layout.EmptyLineTop = layout.TopInset + layout.BaselineIn(slot) - metrics.Baseline;
                            lineHeight = metrics.Height;
                        }
                }
                layout.EmptyLineHeight = lineHeight;
                layout.Height = layout.TopInset + slot + layout.BottomInset;
                layout.Width = 0;
                return layout;
            }
            int index = 0;
            double y = layout.TopInset + layout._labelLineHeight;
            TextLineBreak? previous = null;
            var formatter = TextFormatter.Current;
            while (index < source.Text.Length || layout.Lines.Count == 0)
            {
                if (pf.MaxLines > 0 && layout.Lines.Count >= pf.MaxLines) { layout.Truncated = true; break; }
                bool first = layout.Lines.Count == 0;
                // The first line starts where PlaceFirstLine put it; the others use the whole text area.
                double lineWidth = first ? layout._firstWidth : w;
                double lineX = first ? layout._firstX : layout.ContentLeft;
                var line = formatter.FormatLine(source, index, lineWidth, paragraphProperties, previous);
                if (line == null) break;
                double slot = layout.SlotHeight(line.Height + ExtentOf(line, layout._offsets));
                if (first && pf.ExactLineHeight && pf.LineHeight > 0 && line.Height > 0) layout._exactAscentFraction = line.Baseline / line.Height;
                layout.Lines.Add(new LineInfo(line, y, lineX, slot, layout.BaselineIn(slot), layout._offsets));
                y += slot;
                layout.Width = Math.Max(layout.Width, line.WidthIncludingTrailingWhitespace);
                if (line.Length <= 0) break;
                index += line.Length;
                previous = line.TextLineBreak;
            }
            if (!layout.Truncated && layout.Lines.Count > 0 && layout.Lines[layout.Lines.Count - 1].End == source.Text.Length && source.Text[source.Text.Length - 1] == HardLineBreak)
            {
                // A paragraph that ends in a hard line break has one more line, empty, and the caret can sit on it. The formatter
                // makes no line from no text, so a space gives the height and the line is never drawn.
                // At the line limit that line would be hidden, but it holds no text: nothing is truncated, and the caret simply stops
                // before the break, so typing there and Delete (which removes the break) still work.
                if (pf.MaxLines > 0 && layout.Lines.Count >= pf.MaxLines) layout.HiddenTrailingBreak = true;
                else
                {
                    var spaceSource = new ParagraphSource { Text = " " };
                    spaceSource.Segments.Add(new ParagraphSource.Segment { Start = 0, End = 1, Properties = defaultProperties });
                    var metrics = formatter.FormatLine(spaceSource, 0, w, paragraphProperties, null);
                    if (metrics != null)
                    {
                        double slot = layout.SlotHeight(metrics.Height);
                        layout.Lines.Add(new LineInfo(metrics, y, layout.ContentLeft, slot, source.Text.Length, layout.AlignedX(layout.ContentLeft, w), layout.BaselineIn(slot)));
                        y += slot;
                    }
                }
            }
            layout.Height = y + layout.BottomInset;
            return layout;
        }

        /// <summary>The hard line break (Shift+Enter), U+2028: it ends its line, takes no room and is not drawn.</summary>
        const char HardLineBreak = '\u2028';

        // ---- paragraph formatting ----

        double _firstX, _firstWidth, _labelWidth;
        // Runs with a background colour: start, then end and brush (effective offsets).
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _backgrounds = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();

        /// <summary>Paints the runs' background colours behind the text; <paramref name="top"/> is the paragraph's top in the control.
        /// The field paints the selection after this, so a selected run stays visibly selected.</summary>
        public void DrawBackgrounds(DrawingContext context, double top)
        {
            if (_backgrounds.Count == 0) return;
            foreach (var li in Lines)
                foreach (var bg in _backgrounds)
                {
                    int a = Math.Max(bg.Key, li.Start), b = Math.Min(bg.Value.Key, TextEnd(li));
                    if (b <= a) continue;
                    foreach (var piece in li.Pieces(a, b))
                        context.DrawRectangle(bg.Value.Value, null, new Rect(piece.Key.X, top + li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height));
                }
        }

        readonly OffsetMap _offsets = new OffsetMap();

        /// <summary>The room a line needs for its shifted runs, above and below its own height.</summary>
        static double ExtentOf(TextLine line, OffsetMap offsets)
        {
            RunOffsets(line, offsets, out var raise, out var lower);
            return raise + lower;
        }

        // Runs with a squiggle underline: start, then end and brush (effective offsets).
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _squiggles = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();

        /// <summary>Draws the squiggle underlines (underline type 6, spelling marks): a zigzag a little below the baseline under each
        /// run's text on every line it is on, in its under colour. Drawn after the text; <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawSquiggles(DrawingContext context, double top)
        {
            if (_squiggles.Count == 0) return;
            foreach (var li in Lines)
            {
                if (li.IsEmpty) continue;
                foreach (var sq in _squiggles)
                {
                    int a = Math.Max(sq.Key, li.Start), b = Math.Min(sq.Value.Key, TextEnd(li));
                    if (b <= a) continue;
                    double descent = Math.Max(li.Line.Height - li.Line.Baseline, 3);
                    double amplitude = Math.Min(2, Math.Max(1, descent / 2));
                    var pen = new Pen(sq.Value.Value, 1);
                    foreach (var piece in li.Pieces(a, b))
                    {
                        double middle = top + li.TextY + li.Line.Baseline + 1 + amplitude - piece.Value;
                        double x0 = piece.Key.X, x1 = piece.Key.Right;
                        if (x1 - x0 < 1) continue;
                        var wave = new StreamGeometry();
                        using (var c = wave.Open())
                        {
                            bool down = false;
                            c.BeginFigure(new Point(x0, middle - amplitude), false);
                            for (double x = x0 + 2; x < x1; x += 2)
                            {
                                c.LineTo(new Point(x, middle + (down ? -amplitude : amplitude)));
                                down = !down;
                            }
                            c.LineTo(new Point(x1, middle + (down ? -amplitude : amplitude)));
                            c.EndFigure(false);
                        }
                        context.DrawGeometry(null, pen, wave);
                    }
                }
            }
        }

        /// <summary>The extra space a bullet or number needs after it: three points unless it ends with a space.</summary>
        const double LabelGap = 3;

        /// <summary>Works out where the first line's text starts and how wide it may be. The first indent moves it in (or, hanging,
        /// out). A bullet or number sits at the first indent; if the hanging indent is wider than the label and its gap the first
        /// line's text starts where the other lines' do, otherwise it starts after the label, as in FieldWorks. When the label leaves no
        /// room for the first word of the text beside it (or for its first letter, if the word is wider than any line), the label takes
        /// a line of its own and the text starts on the next, on the full text width.</summary>
        // Provenance: this rule follows FieldWorks' observable list layout (a 3 pt gap after the label unless it ends in a space; the label
        // goes in the hanging indent when it fits there, else the text starts after it; a label with no room beside the text is on its own
        // line). The code is ours.
        void PlaceFirstLine(double w, double firstIndent, ParagraphSource source, ParagraphProperties properties)
        {
            _labelWidth = 0;
            _labelLineHeight = 0;
            var label = LabelLine(LabelText);
            if (label != null) _labelWidth = label.WidthIncludingTrailingWhitespace + (LabelText[LabelText.Length - 1] == ' ' ? 0 : LabelGap);
            if (_labelWidth > 0 && firstIndent < 0 && -firstIndent > _labelWidth)
            {
                _firstWidth = w;
                _firstX = ContentLeft;
                return;
            }
            double taken = firstIndent + _labelWidth;
            if (label != null && source.Text.Length > 0 && w - taken < WidthNeededBesideLabel(source, properties, w))
            {
                _labelLineHeight = SlotHeight(label.Height);
                _firstWidth = w;
                _firstX = ContentLeft;
                return;
            }
            _firstWidth = Math.Max(w - taken, 1);
            _firstX = ContentLeft + (Rtl ? 0 : taken);
        }

        /// <summary>How much room the first line of text needs beside the label: the first word, or its first letter when the word is wider
        /// than a whole line (it breaks wherever it is put).</summary>
        static double WidthNeededBesideLabel(ParagraphSource source, ParagraphProperties properties, double lineWidth)
        {
            string text = source.Text;
            int word = 0;
            while (word < text.Length && !char.IsWhiteSpace(text[word]) && text[word] != HardLineBreak) word++;
            if (word == 0) return 0;
            using (var line = TextFormatter.Current.FormatLine(source, 0, 100000, properties, null))
            {
                if (line == null) return 0;
                double Measure(int length)
                {
                    double sum = 0;
                    foreach (var tb in line.GetTextBounds(0, length)) sum += tb.Rectangle.Width;
                    return sum;
                }
                double whole = Measure(word);
                return whole <= lineWidth ? whole : Measure(Math.Min(GraphemeClusters.Next(text, 0), word));
            }
        }

        /// <summary>The height of the line a too-wide bullet or number takes alone above the text, or 0 when it sits beside the first line.</summary>
        double _labelLineHeight;

        /// <summary>The space above the first line (space before, margin, border, padding) and below the last.</summary>
        public double TopInset { get; private set; }
        public double BottomInset { get; private set; }
        /// <summary>Where the caret sits in an empty paragraph, vertically.</summary>
        double EmptyLineTop, EmptyLineHeight;

        static TextAlignment Alignment(int align)
        {
            switch (align)
            {
                case 1: return TextAlignment.Left;
                case 2: return TextAlignment.Center;
                case 3: return TextAlignment.Right;
                case 4: return TextAlignment.End;
                // Justify: Avalonia's TextFormatter does not stretch lines and its justifier (InterWordJustification) is internal, so
                // the lines are laid out against the leading edge, as the last line of a justified paragraph is.
                case 5: return TextAlignment.Start;
                default: return TextAlignment.Start;
            }
        }

        /// <summary>Under an exact line height, the share of the text's height that is above its baseline, taken from the first line and used
        /// for every line so baselines are one slot apart; NaN otherwise.</summary>
        double _exactAscentFraction = double.NaN;

        /// <summary>Where the baseline sits from the top of a slot of the given height: the first line's ascent share of the slot under an
        /// exact line height, else NaN (the text sits at the bottom of the slot).</summary>
        double BaselineIn(double slot) => double.IsNaN(_exactAscentFraction) ? double.NaN : _exactAscentFraction * slot;

        /// <summary>The slot a line of the given natural height takes under the style's line height: exact fixes it, otherwise it is at least that.</summary>
        double SlotHeight(double natural)
        {
            double rule = Format.LineHeight;
            if (rule <= 0) return natural;
            return Format.ExactLineHeight ? rule : Math.Max(rule, natural);
        }

        /// <summary>Works out the margin, border and padding boxes from the format. Leading is the right side in a right-to-left paragraph.
        /// A hanging first indent is added to the leading padding, as FieldWorks does, so the first line starts at the padding edge.</summary>
        void ApplyBox(ParaFormat pf, double width, bool rtl, IBrush foreground)
        {
            double total = double.IsInfinity(width) ? 100000 : Math.Max(width, 10);
            double hang = Math.Max(-pf.FirstIndent, 0);
            double marginLeft = rtl ? pf.TrailingIndent : pf.LeadingIndent, marginRight = rtl ? pf.LeadingIndent : pf.TrailingIndent;
            _borderLeft = rtl ? pf.BorderTrailing : pf.BorderLeading;
            _borderRight = rtl ? pf.BorderLeading : pf.BorderTrailing;
            _borderTop = pf.BorderTop;
            _borderBottom = pf.BorderBottom;
            double padLeft = (rtl ? pf.PadTrailing : pf.PadLeading + hang), padRight = (rtl ? pf.PadLeading + hang : pf.PadTrailing);
            ContentLeft = marginLeft + _borderLeft + padLeft;
            ContentWidth = Math.Max(total - ContentLeft - marginRight - _borderRight - padRight, 10);
            // The paragraph above already left its space after; this paragraph adds what is needed to make the gap the larger of
            // the margin top and (space after + space before).
            double gap = Math.Max(pf.MarginTop - PreviousSpaceAfter, pf.SpaceBefore);
            TopInset = gap + pf.BorderTop + pf.PadTop;
            BottomInset = pf.PadBottom + pf.BorderBottom + pf.SpaceAfter;
            Box = new Rect(marginLeft, gap, Math.Max(total - marginLeft - marginRight, 0), 0);
            _fill = ColorBrush(pf.ParaColor);
            _borderBrush = _borderLeft + _borderTop + _borderRight + _borderBottom > 0 ? (ColorBrush(pf.BorderColor) ?? foreground) : null;
        }

        /// <summary>Paints the paragraph's fill colour and borders; <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawBox(DrawingContext context, double top)
        {
            if (_fill == null && _borderBrush == null) return;
            var box = new Rect(Box.X, top + Box.Y, Box.Width, Height - Format.SpaceAfter - Box.Y);
            if (_fill != null) context.DrawRectangle(_fill, null, box);
            if (_borderBrush == null) return;
            if (_borderTop > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Y, box.Width, _borderTop));
            if (_borderBottom > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Bottom - _borderBottom, box.Width, _borderBottom));
            if (_borderLeft > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Y, _borderLeft, box.Height));
            if (_borderRight > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.Right - _borderRight, box.Y, _borderRight, box.Height));
        }

        /// <summary>The bullet or number label's line, formatted in the number font (the style's number font info over the first run's format).</summary>
        TextLine? LabelLine(string text)
        {
            if (_labelFormat == null || _labelWs == null || text.Length == 0) return null;
            if (_label != null && _labelText == text) return _label;
            _label?.Dispose();
            var props = MakeProperties(_labelFormat, _labelWs, _labelWsTag, _labelForeground ?? Brushes.Black);
            var source = new ParagraphSource { Text = text };
            source.Segments.Add(new ParagraphSource.Segment { Start = 0, End = text.Length, Properties = props });
            _label = TextFormatter.Current.FormatLine(source, 0, 100000, new ParagraphProperties(Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, props, TextAlignment.Left), null);   // Left: drawn from its own x, not from the right of the 100000 wide line
            _labelText = text;
            return _label;
        }

        /// <summary>How the bullet or number is drawn (font, size, decorations), or null when there is none.</summary>
        public TextRunProperties? LabelProperties => LabelLine(LabelText)?.TextRuns[0].Properties;

        /// <summary>The bullet or number's box in paragraph coordinates, or an empty rectangle when the paragraph has none: its text only,
        /// at the first indent on the leading side (the gap that follows it is not part of it).</summary>
        public Rect LabelRect
        {
            get
            {
                var label = LabelLine(LabelText);
                if (label == null) return default;
                double lineTop = _labelLineHeight > 0 ? TopInset + (double.IsNaN(_exactAscentFraction) ? _labelLineHeight - label.Height : BaselineIn(_labelLineHeight) - label.Baseline) : Lines.Count > 0 ? Lines[0].TextY : EmptyLineTop;
                double width = label.WidthIncludingTrailingWhitespace;
                double x = Rtl ? ContentLeft + ContentWidth - Format.FirstIndent - width : ContentLeft + Format.FirstIndent;
                return new Rect(x, lineTop, width, label.Height);
            }
        }

        /// <summary>Draws the paragraph's bullet or number at the start of its first line, on the same baseline.</summary>
        public void DrawLabel(DrawingContext context, double top)
        {
            var label = LabelLine(LabelText);
            var box = LabelRect;
            if (label == null) return;
            double baseline = Lines.Count > 0 && _labelLineHeight <= 0 ? Lines[0].Line.Baseline : label.Baseline;
            label.Draw(context, new Point(box.X, top + box.Y + baseline - label.Baseline));
        }

        // ---- model offset <-> effective offset; they differ only while a preedit is shown ----
        public int ToEffective(int offset) => PreeditAt >= 0 && offset > PreeditAt ? offset + PreeditLength : offset;

        public int ToModel(int effective)
        {
            if (PreeditAt < 0) return effective;
            if (effective >= PreeditAt + PreeditLength) return effective - PreeditLength;
            return effective > PreeditAt ? PreeditAt : effective;
        }

        /// <summary>The last caret offset of a line (effective): any newline is part of the line length (the text source ends by
        /// returning null, not a TextEndOfParagraph run: that run broke caret distances in a final right-to-left run).</summary>
        public int TextEnd(LineInfo line)
        {
            if (line.IsEmpty) return line.Start;
            int end = Math.Min(line.End - line.Line.NewLineLength, EffectiveText.Length);
            // A hard line break ends its line but is not text on it: the caret before the break is at the end of this line, the
            // caret after it at the start of the next.
            if (end > line.Start && EffectiveText[end - 1] == HardLineBreak) end--;
            return end;
        }

        LineInfo? LineOf(int effective, CharacterHit? hit = null)
        {
            if (Lines.Count == 0) return null;
            // Caret affinity at a soft wrap: the end of a wrapped line and the start of the next line are the same
            // offset. A hit that trails the last character of a line (FirstCharacterIndex inside it) means "end of that
            // line"; a plain hit at the offset means "start of the next".
            if (hit.HasValue && hit.Value.TrailingLength > 0 && hit.Value.FirstCharacterIndex + hit.Value.TrailingLength == effective)
            {
                foreach (var l in Lines)
                    if (hit.Value.FirstCharacterIndex >= l.Start && hit.Value.FirstCharacterIndex < l.End) return l;
            }
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i];
                bool last = i == Lines.Count - 1;
                if (effective >= l.Start && (effective < l.End || (last && effective <= l.End))) return l;
            }
            return Lines[Lines.Count - 1];
        }

        /// <summary>The caret hit meaning "after the last character before <paramref name="effective"/>": at a soft wrap it
        /// keeps the caret at the end of the line instead of the start of the next one.</summary>
        CharacterHit TrailingHit(int effective)
        {
            if (effective > 0 && effective <= EffectiveText.Length && EffectiveText[effective - 1] == HardLineBreak) return new CharacterHit(effective);   // after a hard line break the caret starts the next line
            int p = GraphemeClusters.Previous(EffectiveText, effective);
            return new CharacterHit(p, effective - p);
        }

        /// <summary>The caret hit that puts the caret after the character before <paramref name="modelOffset"/>, on the
        /// same line as that character (the end of a selected word at a soft wrap, say).</summary>
        public CharacterHit EndHit(int modelOffset) => modelOffset <= 0 ? new CharacterHit(0) : TrailingHit(ToEffective(modelOffset));

        /// <summary>The model offset and caret hit for the end of a line: at a soft wrap the caret stays on this line.</summary>
        public int LineEnd(int lineIndex, out CharacterHit hit)
        {
            var li = Lines[lineIndex];
            int max = TextEnd(li);
            bool last = lineIndex == Lines.Count - 1;
            hit = !last && max > li.Start ? TrailingHit(max) : new CharacterHit(max);
            return ToModel(max);
        }

        public Rect CaretRect(int modelOffset, bool effective = false, CharacterHit? hit = null)
        {
            int e = effective ? modelOffset : ToEffective(modelOffset);
            var li = LineOf(e, hit);
            if (li == null) return new Rect(EmptyCaretX(), EmptyLineTop, 1, EmptyLineHeight);
            if (li.IsEmpty) return new Rect(li.EmptyCaretX, li.TextY, 1, li.Line.Height);
            var h = hit.HasValue && hit.Value.FirstCharacterIndex + hit.Value.TrailingLength == e ? hit.Value : new CharacterHit(e);
            if (h.TrailingLength == 0 && e > li.Start && e == TextEnd(li) && e < li.End && EffectiveText[e] == HardLineBreak) h = TrailingHit(e);   // before the break: after the last character of the line
            double x = li.Line.GetDistanceFromCharacterHit(h);
            return new Rect(li.X + x, li.TextY - CaretOffset(li, e), 1, li.Line.Height);
        }

        /// <summary>How far a caret at the effective offset is raised: it follows the character before it (the one it would extend), or the one
        /// after it at the start of a line.</summary>
        double CaretOffset(LineInfo li, int effective)
        {
            if (!li.HasOffsets || li.IsEmpty) return 0;
            int probe = effective > li.Start ? effective - 1 : effective;
            if (probe >= EffectiveText.Length) return 0;
            foreach (var piece in li.Pieces(probe, probe + 1)) return piece.Value;
            return 0;
        }

        /// <summary>The model offset at a point in paragraph coordinates.</summary>
        public int HitTest(Point point) => HitTestDetailed(point).Offset;

        /// <summary>The result of hit testing: the caret offset, the caret hit that says on which side of a soft wrap or
        /// bidi boundary the caret sits, and the offset of the character under the point.</summary>
        public struct HitResult
        {
            public int Offset;
            public CharacterHit CaretHit;
            public int Character;
        }

        public HitResult HitTestDetailed(Point point)
        {
            if (Lines.Count == 0) return default;
            var li = Lines[Lines.Count - 1];
            foreach (var l in Lines)
            {
                if (point.Y < l.Y + l.Height) { li = l; break; }
            }
            if (li.IsEmpty)
                return new HitResult { Offset = Paragraph.Length, CaretHit = new CharacterHit(li.Start), Character = Paragraph.Length };   // past the last character: the empty line after a trailing break holds none
            var h = li.Line.GetCharacterHitFromDistance(point.X - li.X);
            int e = h.FirstCharacterIndex + h.TrailingLength;
            int max = TextEnd(li);
            bool last = li == Lines[Lines.Count - 1];
            var caretHit = h;
            if (e > max) { e = max; caretHit = new CharacterHit(e); }
            if (!last && e == max && max > li.Start) caretHit = TrailingHit(max);
            int ch = Math.Max(0, Math.Min(Math.Max(0, EffectiveText.Length - 1), h.FirstCharacterIndex));
            var result = new HitResult
            {
                Offset = Math.Max(0, Math.Min(Paragraph.Length, ToModel(e))),
                Character = Math.Max(0, Math.Min(Math.Max(0, Paragraph.Length - 1), ToModel(ch))),
                CaretHit = PreeditAt >= 0 ? new CharacterHit(e) : caretHit,
            };
            return result;
        }

        public List<Rect> SelectionRects(int from, int to)
        {
            var result = new List<Rect>();
            if (to <= from) return result;
            int ef = ToEffective(from), et = ToEffective(to);
            foreach (var li in Lines)
            {
                int te = TextEnd(li);
                int a = Math.Max(ef, li.Start), b = Math.Min(et, te);
                if (b <= a)
                {
                    // The break is never drawn, so a selection that covers only the break gets a small block where the break is.
                    if (ef <= te && et > te && te < li.End && !li.IsEmpty && EffectiveText[te] == HardLineBreak)
                    {
                        result.Add(BreakBlock(li, te));
                    }
                    continue;
                }
                foreach (var piece in li.Pieces(a, b))
                    result.Add(new Rect(piece.Key.X, li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height));
            }
            return result;
        }

        /// <summary>The small block that stands for a selected hard line break on a line: at the end of the line's text in the paragraph's
        /// direction (right of it in a left-to-right paragraph, left of it in a right-to-left one), whatever direction the last glyph runs
        /// in, so it never covers a glyph. It stays inside the field.</summary>
        Rect BreakBlock(LineInfo li, int textEnd)
        {
            var caret = CaretRect(textEnd, true);
            double w = Math.Max(3, caret.Height * 0.25);
            double edge = caret.X;
            if (textEnd > li.Start)
            {
                bool any = false;
                double left = double.MaxValue, right = double.MinValue;
                foreach (var tb in li.Line.GetTextBounds(li.Start, textEnd - li.Start))
                {
                    any = true;
                    left = Math.Min(left, li.X + tb.Rectangle.X);
                    right = Math.Max(right, li.X + tb.Rectangle.Right);
                }
                if (any) edge = Rtl ? left : right;
            }
            double x = Rtl ? edge - w : edge;
            if (!double.IsInfinity(LayoutWidth)) x = Math.Min(x, LayoutWidth - w);
            return new Rect(Math.Max(x, 0), caret.Y, w, caret.Height);
        }

        public struct Stop
        {
            public CharacterHit Hit;
            public int Offset;
            public double X;
        }

        /// <summary>The distinct visual caret positions of a line, sorted by x. A bidi boundary yields two stops at
        /// different x that share logical offsets: the leading edge of one cluster and the trailing edge of the
        /// previous one are different places there.</summary>
        public List<Stop> VisualStops(LineInfo line)
        {
            if (line.IsEmpty) return new List<Stop> { new Stop { Hit = new CharacterHit(line.Start), Offset = line.Start, X = line.EmptyCaretX } };
            var candidates = new List<Stop>();
            var h = new CharacterHit(line.Start);
            int guard = 0;
            int max = TextEnd(line);
            while (guard++ < 10000)
            {
                int o = h.FirstCharacterIndex + h.TrailingLength;
                candidates.Add(new Stop { Hit = new CharacterHit(o), Offset = o, X = line.X + line.Line.GetDistanceFromCharacterHit(new CharacterHit(o)) });
                if (h.TrailingLength > 0) candidates.Add(new Stop { Hit = h, Offset = o, X = line.X + line.Line.GetDistanceFromCharacterHit(h) });
                var n = line.Line.GetNextCaretCharacterHit(h);
                int no = n.FirstCharacterIndex + n.TrailingLength;
                if (no <= o || no > max) break;
                h = n;
            }
            var result = new List<Stop>();
            foreach (var c in candidates.OrderBy(s => s.X))
                if (result.Count == 0 || Math.Abs(result[result.Count - 1].X - c.X) > 0.5) result.Add(c);
            if (line != Lines[Lines.Count - 1] && max > line.Start)
            {
                // The stop at the end of a soft-wrapped line keeps the caret on this line.
                for (int i = 0; i < result.Count; i++)
                    if (result[i].Offset == max) result[i] = new Stop { Hit = TrailingHit(max), Offset = max, X = result[i].X };
            }
            return result;
        }

        /// <summary>Model offsets and x of the visual stops of a line, left to right.</summary>
        public List<KeyValuePair<int, double>> Stops(LineInfo line) => VisualStops(line).Select(s => new KeyValuePair<int, double>(s.Offset, s.X)).ToList();

        public int LineIndexOf(int modelOffset, CharacterHit? hit = null)
        {
            var li = LineOf(ToEffective(modelOffset), hit);
            return li == null ? 0 : Lines.IndexOf(li);
        }

        /// <summary>Where object runs sit in glyph mode: the bounds of the EM SPACE stand-in, with the object data.</summary>
        public List<KeyValuePair<Rect, string>> ObjectRects()
        {
            var result = new List<KeyValuePair<Rect, string>>();
            int pos = 0;
            foreach (var r in Paragraph.Runs)
            {
                if (r.IsObject && r.ObjData != null)
                {
                    int e = ToEffective(pos);
                    var li = LineOf(e);
                    if (li != null)
                        foreach (var piece in li.Pieces(e, e + 1))
                            result.Add(new KeyValuePair<Rect, string>(new Rect(piece.Key.X, li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height), r.ObjData));
                }
                pos += r.Text.Length;
            }
            return result;
        }

        /// <summary>The caret's x in a paragraph with no text: where the first line would start, following the alignment.</summary>
        double EmptyCaretX() => AlignedX(_firstX, _firstWidth);

        /// <summary>Where an empty line's caret goes in a box of the given left edge and width, following the alignment.</summary>
        double AlignedX(double firstLeft, double width)
        {
            bool right = Format.Align == 3 || (Format.Align == 4 && !Rtl) || ((Format.Align == 0 || Format.Align == 5) && Rtl);
            if (Format.Align == 2) return firstLeft + width / 2;
            return right ? firstLeft + width : firstLeft;
        }

        public void Dispose()
        {
            _label?.Dispose();
            foreach (var l in Lines) l.Line.Dispose();
            Lines.Clear();
        }
    }
}
