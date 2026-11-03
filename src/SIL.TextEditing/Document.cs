using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SIL.TextEditing
{
    /// <summary>A position in a document: paragraph index and UTF-16 offset inside it.</summary>
    public struct TextPosition : IEquatable<TextPosition>, IComparable<TextPosition>
    {
        public TextPosition(int paragraph, int offset)
        {
            Paragraph = paragraph;
            Offset = offset;
        }

        public int Paragraph;
        public int Offset;

        public bool Equals(TextPosition other) => Paragraph == other.Paragraph && Offset == other.Offset;
        public override bool Equals(object? obj) => obj is TextPosition p && Equals(p);
        public override int GetHashCode() => Paragraph * 100003 + Offset;
        public int CompareTo(TextPosition other) => Paragraph != other.Paragraph ? Paragraph.CompareTo(other.Paragraph) : Offset.CompareTo(other.Offset);
        public override string ToString() => Paragraph + ":" + Offset;
    }

    public sealed class Document
    {
        public List<Paragraph> Paragraphs { get; } = new List<Paragraph>();

        /// <summary>The Unicode form the host stores (<see cref="NormalizationForm.FormD"/> for FieldWorks). Typed, composed and
        /// pasted text is converted to it before insertion. Null (the default) leaves text as it arrives.</summary>
        public NormalizationForm? Normalization { get; set; }

        public Document Clone()
        {
            var d = new Document { Normalization = Normalization };
            foreach (var p in Paragraphs) d.Paragraphs.Add(p.Clone());
            return d;
        }

        public string PlainText() => string.Join("\n", Paragraphs.Select(p => p.Text));
    }

    /// <summary>A reversible change: paragraphs [Start, Start + Before.Count) were replaced by After.</summary>
    public sealed class EditDelta
    {
        public int Start { get; set; }
        public List<Paragraph> Before { get; set; } = new List<Paragraph>();
        public List<Paragraph> After { get; set; } = new List<Paragraph>();
        public TextPosition CaretBefore { get; set; }
        public TextPosition CaretAfter { get; set; }
        public string Label { get; set; } = string.Empty;

        public EditDelta Inverse()
        {
            return new EditDelta { Start = Start, Before = After, After = Before, CaretBefore = CaretAfter, CaretAfter = CaretBefore, Label = "undo " + Label };
        }
    }
}
