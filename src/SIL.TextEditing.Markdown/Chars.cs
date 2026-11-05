namespace SIL.TextEditing.Markdown
{
    /// <summary>Characters the notation treats specially, named so no source line holds an invisible one.</summary>
    internal static class Chars
    {
        public const char LineSeparator = (char)0x2028;
        public const string LineSeparatorText = "\x2028";
        public const char ParagraphSeparator = (char)0x2029;
        public const char ByteOrderMark = (char)0xFEFF;
        public const char Tab = (char)9;
        public const char ObjectReplacement = (char)0xFFFC;
    }
}
