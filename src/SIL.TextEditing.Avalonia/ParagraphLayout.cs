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

        public RunTextProperties(Typeface typeface, double size, IBrush foreground, TextDecorationCollection? decorations, CultureInfo culture, FontFeatureCollection? features)
        {
            _typeface = typeface;
            _size = size;
            _foreground = foreground;
            _decorations = decorations;
            _culture = culture;
            _features = features;
        }

        public override Typeface Typeface => _typeface;
        public override double FontRenderingEmSize => _size;
        public override TextDecorationCollection? TextDecorations => _decorations;
        public override IBrush? ForegroundBrush => _foreground;
        public override IBrush? BackgroundBrush => null;
        public override CultureInfo? CultureInfo => _culture;
        public override FontFeatureCollection? FontFeatures => _features;
    }

    sealed class ParagraphProperties : TextParagraphProperties
    {
        readonly FlowDirection _flowDirection;
        readonly TextRunProperties _default;

        public ParagraphProperties(FlowDirection flowDirection, TextRunProperties defaultProperties)
        {
            _flowDirection = flowDirection;
            _default = defaultProperties;
        }

        public override FlowDirection FlowDirection => _flowDirection;
        public override TextAlignment TextAlignment => TextAlignment.Start;
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

        public TextRun GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= Text.Length) return new TextEndOfParagraph();
            foreach (var s in Segments)
            {
                if (textSourceIndex < s.Start || textSourceIndex >= s.End) continue;
                if (s.IsObject && ObjectAsGlyph)
                    return new TextCharacters(Text.AsMemory(textSourceIndex, 1), new RunTextProperties(s.Properties.Typeface, 28, s.Properties.ForegroundBrush ?? Brushes.Black, null, s.Properties.CultureInfo ?? CultureInfo.InvariantCulture, null));
                if (s.IsObject)
                    return new ObjectRun(s.Data == null ? null : ObjectImage?.Invoke(s.Data), new Size(32, 24), s.Properties);
                return new TextCharacters(Text.AsMemory(textSourceIndex, s.End - textSourceIndex), s.Properties);
            }
            return new TextEndOfParagraph();
        }
    }

    public sealed class LineInfo
    {
        public LineInfo(TextLine line, double y)
        {
            Line = line;
            Y = y;
            Start = line.FirstTextSourceIndex;
            Length = line.Length;
            End = Start + Length;
        }

        public TextLine Line { get; }
        public double Y { get; }
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

        /// <summary>Builds the text run properties for a run: font and size from the writing system, bold, italic,
        /// underline and size overrides from the run's property bag.</summary>
        static RunTextProperties MakeProperties(Run run, WritingSystemRegistry writingSystems, IBrush foreground, bool preedit = false)
        {
            var ws = writingSystems.Get(run.Ws);
            var weight = run.Props.TryGetInt(TextPropertyNumbers.Bold, out var bold) && bold == TextPropertyNumbers.ToggleOn ? FontWeight.Bold : FontWeight.Normal;
            var style = run.Props.TryGetInt(TextPropertyNumbers.Italic, out var italic) && italic == TextPropertyNumbers.ToggleOn ? FontStyle.Italic : FontStyle.Normal;
            double size = ws.Size;
            if (run.Props.IntProps.TryGetValue(TextPropertyNumbers.FontSize, out var fs) && fs.Variation == TextPropertyNumbers.VariationMilliPoint)
                size = fs.Value / 1000.0;
            FontFeatureCollection? features = null;
            if (!string.IsNullOrEmpty(ws.Features))
            {
                features = new FontFeatureCollection();
                foreach (var f in ws.Features!.Split(','))
                    features.Add(FontFeature.Parse(f.Trim()));
            }
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(run.Ws); }
            catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
            TextDecorationCollection? decorations = null;
            if (preedit || (run.Props.TryGetInt(TextPropertyNumbers.Underline, out var underline) && underline != 0))
                decorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } };
            return new RunTextProperties(new Typeface(new FontFamily(ws.Font), style, weight), size, foreground, decorations, culture, features);
        }

        /// <summary>
        /// Lays out a paragraph. <paramref name="objectAsGlyph"/> is the workaround for Avalonia issue 21792 (a text-less
        /// DrawableTextRun in a right-to-left paragraph gets the wrong visual order in 11.3 and 12.1): the object is
        /// laid out as an EM SPACE sized to the picture and the picture is painted over its text bounds.
        /// </summary>
        public static ParagraphLayout Build(Paragraph paragraph, double width, WritingSystemRegistry writingSystems, Func<string, IImage?>? objectImage, IBrush foreground, bool objectAsGlyph = false, int preeditAt = -1, string? preedit = null, Run? preeditTemplate = null)
        {
            var layout = new ParagraphLayout(paragraph, width);
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
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = MakeProperties(r, writingSystems, foreground) });
            }

            void AddPreedit()
            {
                int a = sb.Length;
                sb.Append(preedit);
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = MakeProperties(preeditRun, writingSystems, foreground, true) });
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
                    source.Segments.Add(new ParagraphSource.Segment { Start = s0, End = sb.Length, Properties = MakeProperties(r, writingSystems, foreground), IsObject = true, Data = r.ObjData });
                }
                else AddSegment(r.Text, r);
                pos += r.Text.Length;
            }
            if (!preeditDone) AddPreedit();
            layout.PreeditAt = hasPreedit ? preeditAt : -1;
            layout.PreeditLength = hasPreedit ? preedit!.Length : 0;
            source.Text = sb.ToString();
            layout.EffectiveText = source.Text;

            var defaultRun = paragraph.Runs.Count > 0 ? paragraph.Runs[0] : new Run { Ws = paragraph.DefaultWs };
            var defaultProperties = MakeProperties(defaultRun, writingSystems, foreground);
            var paragraphProperties = new ParagraphProperties(paragraph.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, defaultProperties);
            double w = double.IsInfinity(width) ? 100000 : Math.Max(width, 10);
            if (source.Text.Length == 0)
            {
                // A line cannot be formatted from zero characters through the public API; use the font metrics.
                var glyphTypeface = defaultProperties.Typeface.GlyphTypeface;
                double fontSize = defaultProperties.FontRenderingEmSize;
                double lineHeight = glyphTypeface != null
                    ? (-glyphTypeface.Metrics.Descent - glyphTypeface.Metrics.Ascent + glyphTypeface.Metrics.LineGap) / (double)glyphTypeface.Metrics.DesignEmHeight * fontSize
                    : fontSize * 1.3;
                if (lineHeight <= 0) lineHeight = fontSize * 1.3;
                layout.Height = lineHeight;
                layout.Width = 0;
                return layout;
            }
            int index = 0;
            double y = 0;
            TextLineBreak? previous = null;
            var formatter = TextFormatter.Current;
            while (index < source.Text.Length || layout.Lines.Count == 0)
            {
                var line = formatter.FormatLine(source, index, w, paragraphProperties, previous);
                if (line == null) break;
                layout.Lines.Add(new LineInfo(line, y));
                y += line.Height;
                layout.Width = Math.Max(layout.Width, line.WidthIncludingTrailingWhitespace);
                if (line.Length <= 0) break;
                index += line.Length;
                previous = line.TextLineBreak;
            }
            layout.Height = y;
            return layout;
        }

        // ---- model offset <-> effective offset; they differ only while a preedit is shown ----
        public int ToEffective(int offset) => PreeditAt >= 0 && offset > PreeditAt ? offset + PreeditLength : offset;

        public int ToModel(int effective)
        {
            if (PreeditAt < 0) return effective;
            if (effective >= PreeditAt + PreeditLength) return effective - PreeditLength;
            return effective > PreeditAt ? PreeditAt : effective;
        }

        /// <summary>The last caret offset of a line (effective): the formatter counts the end-of-paragraph marker (+1) and
        /// any newline in the line length.</summary>
        public int TextEnd(LineInfo line) => Math.Min(line.End - line.Line.NewLineLength, EffectiveText.Length);

        LineInfo? LineOf(int effective)
        {
            if (Lines.Count == 0) return null;
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i];
                bool last = i == Lines.Count - 1;
                if (effective >= l.Start && (effective < l.End || (last && effective <= l.End))) return l;
            }
            return Lines[Lines.Count - 1];
        }

        public Rect CaretRect(int modelOffset, bool effective = false, CharacterHit? hit = null)
        {
            int e = effective ? modelOffset : ToEffective(modelOffset);
            var li = LineOf(e);
            if (li == null) return new Rect(Rtl ? LayoutWidth : 0, 0, 1, Height);
            var h = hit.HasValue && hit.Value.FirstCharacterIndex + hit.Value.TrailingLength == e ? hit.Value : new CharacterHit(e);
            double x = li.Line.GetDistanceFromCharacterHit(h);
            return new Rect(x, li.Y, 1, li.Line.Height);
        }

        /// <summary>The model offset at a point in paragraph coordinates.</summary>
        public int HitTest(Point point)
        {
            if (Lines.Count == 0) return 0;
            var li = Lines[Lines.Count - 1];
            foreach (var l in Lines)
            {
                if (point.Y < l.Y + l.Line.Height) { li = l; break; }
            }
            var h = li.Line.GetCharacterHitFromDistance(point.X);
            int e = h.FirstCharacterIndex + h.TrailingLength;
            int max = TextEnd(li);
            if (e > max && li != Lines[Lines.Count - 1]) e = max;
            int m = ToModel(e);
            return Math.Max(0, Math.Min(Paragraph.Length, m));
        }

        public List<Rect> SelectionRects(int from, int to)
        {
            var result = new List<Rect>();
            if (to <= from) return result;
            int ef = ToEffective(from), et = ToEffective(to);
            foreach (var li in Lines)
            {
                int a = Math.Max(ef, li.Start), b = Math.Min(et, TextEnd(li));
                if (b <= a) continue;
                foreach (var tb in li.Line.GetTextBounds(a, b - a))
                    result.Add(new Rect(tb.Rectangle.X, li.Y + tb.Rectangle.Y, tb.Rectangle.Width, tb.Rectangle.Height));
            }
            return result;
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
            var candidates = new List<Stop>();
            var h = new CharacterHit(line.Start);
            int guard = 0;
            int max = TextEnd(line);
            while (guard++ < 10000)
            {
                int o = h.FirstCharacterIndex + h.TrailingLength;
                candidates.Add(new Stop { Hit = new CharacterHit(o), Offset = o, X = line.Line.GetDistanceFromCharacterHit(new CharacterHit(o)) });
                if (h.TrailingLength > 0) candidates.Add(new Stop { Hit = h, Offset = o, X = line.Line.GetDistanceFromCharacterHit(h) });
                var n = line.Line.GetNextCaretCharacterHit(h);
                int no = n.FirstCharacterIndex + n.TrailingLength;
                if (no <= o || no > max) break;
                h = n;
            }
            var result = new List<Stop>();
            foreach (var c in candidates.OrderBy(s => s.X))
                if (result.Count == 0 || Math.Abs(result[result.Count - 1].X - c.X) > 0.5) result.Add(c);
            return result;
        }

        /// <summary>Model offsets and x of the visual stops of a line, left to right.</summary>
        public List<KeyValuePair<int, double>> Stops(LineInfo line) => VisualStops(line).Select(s => new KeyValuePair<int, double>(s.Offset, s.X)).ToList();

        public int LineIndexOf(int modelOffset)
        {
            var li = LineOf(ToEffective(modelOffset));
            return li == null ? 0 : Lines.IndexOf(li);
        }

        /// <summary>Visual movement: gives the new model offset and caret hit (affinity), or false when the edge of the
        /// paragraph is reached.</summary>
        public bool MoveVisual(int modelOffset, CharacterHit? currentHit, bool toLeft, out int newOffset, out CharacterHit newHit)
        {
            newOffset = -1;
            newHit = default;
            int e = ToEffective(modelOffset);
            var li = LineOf(e);
            if (li == null) return false;
            var h = currentHit.HasValue && currentHit.Value.FirstCharacterIndex + currentHit.Value.TrailingLength == e ? currentHit.Value : new CharacterHit(e);
            double cx = li.Line.GetDistanceFromCharacterHit(h);
            Stop? best = null;
            foreach (var s in VisualStops(li))
            {
                if (toLeft) { if (s.X < cx - 0.5 && (best == null || s.X > best.Value.X)) best = s; }
                else { if (s.X > cx + 0.5 && (best == null || s.X < best.Value.X)) best = s; }
            }
            if (best != null)
            {
                newOffset = ToModel(best.Value.Offset);
                newHit = best.Value.Hit;
                return true;
            }
            int index = Lines.IndexOf(li);
            bool forward = Rtl ? toLeft : !toLeft;
            int ni = index + (forward ? 1 : -1);
            if (ni < 0 || ni >= Lines.Count) return false;
            var stops = VisualStops(Lines[ni]);
            if (stops.Count == 0) return false;
            var pick = toLeft ? stops[stops.Count - 1] : stops[0];
            newOffset = ToModel(pick.Offset);
            newHit = pick.Hit;
            return true;
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
                        foreach (var tb in li.Line.GetTextBounds(e, 1))
                            result.Add(new KeyValuePair<Rect, string>(new Rect(tb.Rectangle.X, li.Y + tb.Rectangle.Y, tb.Rectangle.Width, tb.Rectangle.Height), r.ObjData));
                }
                pos += r.Text.Length;
            }
            return result;
        }

        public void Dispose()
        {
            foreach (var l in Lines) l.Line.Dispose();
            Lines.Clear();
        }
    }
}
