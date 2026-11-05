namespace SIL.TextEditing.LCModel
{
    /// <summary>A property the adapter left out of the string or StyleRules it built, because liblcm's own XML reader refuses it and a
    /// project holding it would be reported as a corrupted record (ADR 0016, amended). The run model's bag still holds it.</summary>
    public sealed class DroppedProperty
    {
        /// <summary>The stable id of this finding, for a host that counts or filters findings.</summary>
        public const string DiagnosticId = "textediting.lcm.unstorable-property";

        public DroppedProperty(int number, bool isString, int? paragraphIndex, int? runIndex)
        {
            Number = number;
            IsString = isString;
            ParagraphIndex = paragraphIndex;
            RunIndex = runIndex;
        }

        public string Id => DiagnosticId;

        /// <summary>The FieldWorks property number.</summary>
        public int Number { get; }

        public bool IsString { get; }

        /// <summary>The paragraph, when the conversion was of a document; otherwise null.</summary>
        public int? ParagraphIndex { get; }

        /// <summary>The run, or null for a paragraph's StyleRules.</summary>
        public int? RunIndex { get; }

        public string Message => (IsString ? "String" : "Integer") + " property " + Number
            + " is not stored: liblcm refuses it when it loads the project, which would report a corrupted record.";

        public override string ToString() => Id + " " + Message;
    }
}
