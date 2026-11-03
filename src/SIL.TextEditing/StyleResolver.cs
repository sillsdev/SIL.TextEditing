using System;

namespace SIL.TextEditing
{
    /// <summary>
    /// The host's styles (ADR 0004). The field asks for the effective formatting of a named style in a writing system and
    /// draws what comes back; it never reads a stylesheet or gives a style name any meaning. The host answers with the
    /// style's own text properties, keyed by FieldWorks property numbers like a run's bag, with inheritance (based-on) and
    /// the writing system's overrides already applied. A null answer means "no such style".
    /// </summary>
    public interface IStyleResolver
    {
        /// <summary>The properties of the named paragraph or character style for the writing system, or null.</summary>
        PropertyBag? Resolve(string styleName, string ws);
    }

    /// <summary>The character formatting of one run after styles and the run's own properties are applied.</summary>
    public sealed class CharFormat
    {
        public const double DefaultSuperscriptScale = 2.0 / 3.0;

        /// <summary>CSS-style weight 100 to 900; 400 is normal and 700 bold.</summary>
        public int Weight { get; set; } = 400;
        public bool Italic { get; set; }
        /// <summary>0 off, 1 superscript, 2 subscript.</summary>
        public int Superscript { get; set; }
        /// <summary>FieldWorks underline type: 0 none, 1 dotted, 2 dashed, 3 single, 4 double, 5 strikethrough, 6 squiggle.</summary>
        public int Underline { get; set; }
        /// <summary>BGR colour of the underline, or null for the text colour.</summary>
        public int? UnderColor { get; set; }
        /// <summary>Font size in points, before any superscript scaling.</summary>
        public double SizePt { get; set; }
        /// <summary>Baseline offset in points; positive raises the text.</summary>
        public double OffsetPt { get; set; }
        /// <summary>BGR colour, or null for the field's text colour (also for the "transparent" special value).</summary>
        public int? ForeColor { get; set; }
        /// <summary>BGR colour of the run's background, or null for none.</summary>
        public int? BackColor { get; set; }
        /// <summary>A font family name (default-font markup already replaced), or null for the writing system's font.</summary>
        public string? FontFamily { get; set; }
        /// <summary>OpenType feature list ("lnum=1, ss01") added to the writing system's, or null.</summary>
        public string? FontFeatures { get; set; }

        public bool Bold => Weight >= 550;

        internal CharFormat Copy() => (CharFormat)MemberwiseClone();

        /// <summary>The font family a FieldWorks default-font markup stands for; any other name is returned as it is.</summary>
        public static string FontNameFromMarkup(string markup)
        {
            switch (markup)
            {
                case "<default serif>": return "Times New Roman";
                case "<default sans serif>": return "Arial";
                case "<default body>": return "Charis SIL";
                case "<default monospace>": return "Courier New";
                default: return markup;
            }
        }

        /// <summary>A BGR text colour as red, green and blue, or false for a special value (high byte set: transparent).</summary>
        public static bool TryColor(int bgr, out byte r, out byte g, out byte b)
        {
            r = (byte)(bgr & 0xFF);
            g = (byte)((bgr >> 8) & 0xFF);
            b = (byte)((bgr >> 16) & 0xFF);
            return ((bgr >> 24) & 0xFF) == 0;
        }

        /// <summary>Applies one bag's character properties over the current state, as FieldWorks' property store does:
        /// toggles may invert, a relative size or enumerated size is taken against the current size.</summary>
        internal void Apply(PropertyBag bag)
        {
            foreach (var kv in bag.IntProps)
            {
                int v = kv.Value.Value, var = kv.Value.Variation;
                switch (kv.Key)
                {
                    case TextPropertyNumbers.Italic:
                        if (v == TextPropertyNumbers.ToggleInvert) Italic = !Italic;
                        else if (v == TextPropertyNumbers.ToggleOn) Italic = true;
                        else if (v == TextPropertyNumbers.ToggleOff) Italic = false;
                        break;
                    case TextPropertyNumbers.Bold:
                        if (v == TextPropertyNumbers.ToggleOff) Weight = 400;
                        else if (v == TextPropertyNumbers.ToggleOn) Weight = 700;
                        else if (v == TextPropertyNumbers.ToggleInvert) Weight = Weight < 550 ? 700 : 400;
                        else if (v > 0) Weight = Math.Max(100, Math.Min(900, v));
                        break;
                    case TextPropertyNumbers.Superscript:
                        Superscript = v >= 0 && v <= 2 ? v : 0;
                        break;
                    case TextPropertyNumbers.Underline:
                        Underline = v;
                        break;
                    case TextPropertyNumbers.UnderColor:
                        UnderColor = v;
                        break;
                    case TextPropertyNumbers.FontSize:
                        ApplySize(v, var);
                        break;
                    case TextPropertyNumbers.Offset:
                        if (var == TextPropertyNumbers.VariationMilliPoint) OffsetPt = v / 1000.0;
                        else if (var == TextPropertyNumbers.VariationRelative) OffsetPt = SizePt * v / 10000.0;
                        break;
                    case TextPropertyNumbers.ForeColor:
                        ForeColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null;
                        break;
                    case TextPropertyNumbers.BackColor:
                        BackColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null;
                        break;
                }
            }
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.FontFamily, out var family) && family.Length > 0)
                FontFamily = FontNameFromMarkup(family);
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.FontVariations, out var features) && features.Length > 0)
                FontFeatures = string.IsNullOrEmpty(FontFeatures) ? features : FontFeatures + ", " + features;
        }

        // Named sizes, in 1/1000 point: xx-small to xx-large; then "smaller" and "larger" step through the same table.
        static readonly int[] NamedSizes = { 5000, 7000, 9000, 12000, 14000, 18000, 24000 };

        void ApplySize(int v, int variation)
        {
            if (variation == TextPropertyNumbers.VariationMilliPoint || variation == TextPropertyNumbers.VariationDefault)
                SizePt = v / 1000.0;
            else if (variation == TextPropertyNumbers.VariationRelative)
                SizePt = SizePt * v / 10000.0;
            else if (variation == TextPropertyNumbers.VariationEnum)
            {
                int mpt = (int)Math.Round(SizePt * 1000);
                if (v >= 0 && v < NamedSizes.Length) SizePt = NamedSizes[v] / 1000.0;
                else if (v == NamedSizes.Length)
                {
                    // Smaller: the largest named size below the current one, else two thirds.
                    int i = 1;
                    while (i < NamedSizes.Length && NamedSizes[i] < mpt) i++;
                    SizePt = (NamedSizes[i - 1] < mpt ? NamedSizes[i - 1] : mpt * 2 / 3) / 1000.0;
                }
                else if (v == NamedSizes.Length + 1)
                {
                    // Larger: the smallest named size above the current one, else half as much again.
                    int i = 0;
                    while (i < NamedSizes.Length && NamedSizes[i] <= mpt) i++;
                    SizePt = (i < NamedSizes.Length ? NamedSizes[i] : mpt * 3 / 2) / 1000.0;
                }
            }
        }
    }

    /// <summary>The paragraph formatting after the paragraph's style and its own properties are applied. Lengths are in points.</summary>
    public sealed class ParaFormat
    {
        /// <summary>FieldWorks alignment: 0 leading, 1 left, 2 center, 3 right, 4 trailing, 5 justify.</summary>
        public int Align { get; set; }
        public double FirstIndent { get; set; }
        public double LeadingIndent { get; set; }
        public double TrailingIndent { get; set; }
        public double SpaceBefore { get; set; }
        public double SpaceAfter { get; set; }
        public double MarginTop { get; set; }
        /// <summary>The line height in points, or 0 for the font's own.</summary>
        public double LineHeight { get; set; }
        /// <summary>True when <see cref="LineHeight"/> is exact; false is "at least".</summary>
        public bool ExactLineHeight { get; set; }
        public int? ParaColor { get; set; }
        public double PadLeading { get; set; }
        public double PadTrailing { get; set; }
        public double PadTop { get; set; }
        public double PadBottom { get; set; }
        public double BorderTop { get; set; }
        public double BorderBottom { get; set; }
        public double BorderLeading { get; set; }
        public double BorderTrailing { get; set; }
        public int? BorderColor { get; set; }
        /// <summary>0 none, 10 to 15 number schemes, 100 and up bullets.</summary>
        public int BulNumScheme { get; set; }
        public int? BulNumStartAt { get; set; }
        public string BulNumTextBefore { get; set; } = string.Empty;
        public string BulNumTextAfter { get; set; } = string.Empty;
        public string BulNumFontInfo { get; set; } = string.Empty;
        /// <summary>The most lines to show; 0 for all.</summary>
        public int MaxLines { get; set; }

        internal void Apply(PropertyBag bag, double baseSizePt)
        {
            foreach (var kv in bag.IntProps)
            {
                int v = kv.Value.Value, var = kv.Value.Variation;
                double pt = v / 1000.0;
                switch (kv.Key)
                {
                    case TextPropertyNumbers.Align: Align = v; break;
                    case TextPropertyNumbers.FirstIndent: FirstIndent = pt; break;
                    case TextPropertyNumbers.LeadingIndent: LeadingIndent = pt; break;
                    case TextPropertyNumbers.TrailingIndent: TrailingIndent = pt; break;
                    case TextPropertyNumbers.SpaceBefore: SpaceBefore = pt; break;
                    case TextPropertyNumbers.SpaceAfter: SpaceAfter = pt; break;
                    case TextPropertyNumbers.MarginTop: MarginTop = pt; break;
                    case TextPropertyNumbers.LineHeight:
                        if (var == TextPropertyNumbers.VariationRelative)
                        {
                            LineHeight = baseSizePt * 1.4 * v / 10000.0;
                            ExactLineHeight = false;
                        }
                        else
                        {
                            LineHeight = Math.Abs(pt);
                            ExactLineHeight = v < 0;
                        }
                        break;
                    case TextPropertyNumbers.ParaColor: ParaColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null; break;
                    case TextPropertyNumbers.PadLeading: PadLeading = pt; break;
                    case TextPropertyNumbers.PadTrailing: PadTrailing = pt; break;
                    case TextPropertyNumbers.PadTop: PadTop = pt; break;
                    case TextPropertyNumbers.PadBottom: PadBottom = pt; break;
                    case TextPropertyNumbers.BorderTop: BorderTop = pt; break;
                    case TextPropertyNumbers.BorderBottom: BorderBottom = pt; break;
                    case TextPropertyNumbers.BorderLeading: BorderLeading = pt; break;
                    case TextPropertyNumbers.BorderTrailing: BorderTrailing = pt; break;
                    case TextPropertyNumbers.BorderColor: BorderColor = ((v >> 24) & 0xFF) == 0 ? v : (int?)null; break;
                    case TextPropertyNumbers.BulNumScheme: BulNumScheme = v; break;
                    case TextPropertyNumbers.BulNumStartAt: BulNumStartAt = v; break;
                    case TextPropertyNumbers.MaxLines: MaxLines = Math.Max(0, v); break;
                }
            }
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumTxtBefore, out var before)) BulNumTextBefore = before;
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumTxtAfter, out var after)) BulNumTextAfter = after;
            if (bag.StrProps.TryGetValue(TextPropertyNumbers.BulNumFontInfo, out var info)) BulNumFontInfo = info;
        }
    }

    /// <summary>
    /// Works out the formatting of a run or paragraph from the layers FieldWorks applies, in its order: the paragraph style's
    /// properties, the paragraph's own, then for a run its character style and its own properties. Later layers win.
    /// </summary>
    public static class FormatResolver
    {
        public static CharFormat ResolveChar(Paragraph paragraph, Run run, IStyleResolver? styles, double baseSizePt)
        {
            var f = new CharFormat { SizePt = baseSizePt };
            var paraStyle = StyleName(paragraph);
            if (styles != null && paraStyle.Length > 0)
            {
                var bag = styles.Resolve(paraStyle, run.Ws);
                if (bag != null) f.Apply(bag);
            }
            f.Apply(paragraph.Props);
            if (run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var charStyle) && charStyle.Length > 0 && styles != null)
            {
                var bag = styles.Resolve(charStyle, run.Ws);
                if (bag != null) f.Apply(bag);
            }
            f.Apply(run.Props);
            return f;
        }

        public static ParaFormat ResolveParagraph(Paragraph paragraph, IStyleResolver? styles, double baseSizePt)
        {
            var f = new ParaFormat();
            var style = StyleName(paragraph);
            if (styles != null && style.Length > 0)
            {
                var bag = styles.Resolve(style, paragraph.DefaultWs);
                if (bag != null) f.Apply(bag, baseSizePt);
            }
            f.Apply(paragraph.Props, baseSizePt);
            return f;
        }

        static string StyleName(Paragraph p)
        {
            if (p.Style.Length > 0) return p.Style;
            return p.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var s) ? s : string.Empty;
        }
    }
}
