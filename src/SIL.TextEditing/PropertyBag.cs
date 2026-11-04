using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>An integer text property value: a variation (how to read the value) and the value itself,
    /// as an ITsTextProps integer property carries them.</summary>
    public readonly struct IntPropValue : IEquatable<IntPropValue>
    {
        public IntPropValue(int variation, int value)
        {
            Variation = variation;
            Value = value;
        }

        public int Variation { get; }
        public int Value { get; }

        public bool Equals(IntPropValue other) => Variation == other.Variation && Value == other.Value;
        public override bool Equals(object? obj) => obj is IntPropValue o && Equals(o);
        public override int GetHashCode() => Variation * 397 ^ Value;
        public override string ToString() => Variation + ":" + Value;
    }

    /// <summary>
    /// The opaque property bag of a run, keyed by FieldWorks text property numbers (see
    /// <see cref="TextPropertyNumbers"/>). Integer and string properties live in separate number spaces, as in
    /// ITsTextProps. The editor never interprets a bag; it copies the whole bag through every edit, and the
    /// text surface reads only the few numbers it draws.
    /// </summary>
    public sealed class PropertyBag
    {
        public SortedDictionary<int, IntPropValue> IntProps { get; } = new SortedDictionary<int, IntPropValue>();
        public SortedDictionary<int, string> StrProps { get; } = new SortedDictionary<int, string>();

        public int Count => IntProps.Count + StrProps.Count;

        public PropertyBag Clone()
        {
            var copy = new PropertyBag();
            foreach (var kv in IntProps) copy.IntProps[kv.Key] = kv.Value;
            foreach (var kv in StrProps) copy.StrProps[kv.Key] = kv.Value;
            return copy;
        }

        /// <summary>Sets an integer property with the default variation.</summary>
        public PropertyBag SetInt(int property, int value, int variation = 0)
        {
            IntProps[property] = new IntPropValue(variation, value);
            return this;
        }

        public PropertyBag SetStr(int property, string value)
        {
            StrProps[property] = value;
            return this;
        }

        public bool TryGetInt(int property, out int value)
        {
            if (IntProps.TryGetValue(property, out var v))
            {
                value = v.Value;
                return true;
            }
            value = 0;
            return false;
        }

        public bool TryGetStr(int property, out string value)
        {
            if (StrProps.TryGetValue(property, out var v))
            {
                value = v;
                return true;
            }
            value = string.Empty;
            return false;
        }

        public bool ContentEquals(PropertyBag other)
        {
            if (IntProps.Count != other.IntProps.Count || StrProps.Count != other.StrProps.Count) return false;
            foreach (var kv in IntProps)
                if (!other.IntProps.TryGetValue(kv.Key, out var v) || !v.Equals(kv.Value)) return false;
            foreach (var kv in StrProps)
                if (!other.StrProps.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
            return true;
        }
    }

    /// <summary>FieldWorks text property numbers (the ktpt* values of ITsTextProps) that this library names.
    /// Any other number is carried unchanged. Integer and string numbers overlap (1 to 7), because they are
    /// separate spaces.</summary>
    public static class TextPropertyNumbers
    {
        // Integer properties.
        public const int Italic = 2;
        public const int Bold = 3;
        public const int Superscript = 4;
        public const int Underline = 5;
        /// <summary>Baseline offset (ktptOffset), in 1/1000 point; positive raises the text.</summary>
        public const int Offset = 7;
        /// <summary>Font size; with variation <see cref="VariationMilliPoint"/> the value is in 1/1000 point.</summary>
        public const int FontSize = 6;
        public const int ForeColor = 8;
        public const int BackColor = 9;
        /// <summary>ktptEditable (155): whether the run may be edited, one of the <c>Editable*</c> values (ADR 0019). Absent
        /// means editable.</summary>
        public const int Editable = 155;
        /// <summary>Underline colour (ktptUnderColor); default variation, a BGR colour.</summary>
        public const int UnderColor = 10;
        public const int Align = 17;
        /// <summary>First-line indent (ktptFirstIndent); negative is a hanging indent.</summary>
        public const int FirstIndent = 18;
        /// <summary>ktptLeadingIndent = ktptMarginLeading.</summary>
        public const int LeadingIndent = 19;
        /// <summary>ktptTrailingIndent = ktptMarginTrailing.</summary>
        public const int TrailingIndent = 20;
        /// <summary>ktptSpaceBefore = ktptMswMarginTop: added to the space after the paragraph above.</summary>
        public const int SpaceBefore = 21;
        /// <summary>ktptSpaceAfter = ktptMarginBottom.</summary>
        public const int SpaceAfter = 22;
        /// <summary>Line height: positive is at least, negative is exact; with <see cref="VariationRelative"/> a fraction (10000 = 1) of 1.4 times the font size.</summary>
        public const int LineHeight = 24;
        /// <summary>Paragraph background colour (ktptParaColor; FieldWorks treats it as the back colour).</summary>
        public const int ParaColor = 25;
        /// <summary>ktptSpellCheck (26): <see cref="SpellCheckNormal"/>, <see cref="SpellCheckDoNot"/> or <see cref="SpellCheckForce"/> (ADR 0020). Absent means normal.</summary>
        public const int SpellCheck = 26;
        public const int SpellCheckNormal = 0, SpellCheckDoNot = 1, SpellCheckForce = 2;
        /// <summary>ktptMarginTop: the CSS-style top margin (the gap between paragraphs is the larger of it and the sum of space after and space before).</summary>
        public const int MarginTop = 50;
        public const int PadLeading = 134;
        public const int PadTrailing = 135;
        public const int PadTop = 136;
        public const int PadBottom = 137;
        public const int BorderTop = 138;
        public const int BorderBottom = 139;
        public const int BorderLeading = 140;
        public const int BorderTrailing = 141;
        public const int BorderColor = 142;
        public const int BulNumScheme = 143;
        public const int BulNumStartAt = 144;
        public const int MaxLines = 151;

        // String properties.
        public const int FontFamily = 1;
        /// <summary>Legacy: FieldWorks keeps character and paragraph style names in <see cref="NamedStyle"/>.</summary>
        public const int CharStyle = 2;
        /// <summary>Legacy: FieldWorks keeps character and paragraph style names in <see cref="NamedStyle"/>.</summary>
        public const int ParaStyle = 3;
        /// <summary>The style name (ktptNamedStyle); one property for both character and paragraph styles.</summary>
        public const int NamedStyle = 133;
        public const int FontVariations = 130;
        public const int BulNumTxtBefore = 145;
        public const int BulNumTxtAfter = 146;
        /// <summary>Packed font properties of a bullet or number: for each integer property its number and the low and high 16 bits of the value, ending with <see cref="FontFamily"/> and the family name.</summary>
        public const int BulNumFontInfo = 147;

        // Underline types (ktptUnderline values).
        public const int UnderlineNone = 0;
        public const int UnderlineDotted = 1;
        public const int UnderlineDashed = 2;
        public const int UnderlineSingle = 3;
        public const int UnderlineDouble = 4;
        public const int UnderlineStrikethrough = 5;
        public const int UnderlineSquiggle = 6;

        // Variations of an integer property.
        public const int VariationDefault = 0;
        public const int VariationMilliPoint = 1;
        /// <summary>A fraction of the inherited value; the denominator is 10000.</summary>
        public const int VariationRelative = 2;
        /// <summary>The value comes from an enumeration (font size names, toggles).</summary>
        public const int VariationEnum = 3;

        // Values of Editable (the TptEditable enum).
        public const int EditableNo = 0;
        public const int EditableYes = 1;
        public const int EditableSemi = 2;

        // Toggle values (ktpt Bold and Italic): 0 off, 1 force on.
        public const int ToggleOff = 0;
        public const int ToggleOn = 1;
        public const int ToggleInvert = 2;
    }
}
