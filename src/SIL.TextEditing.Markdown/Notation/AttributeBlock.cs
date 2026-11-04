using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SIL.TextEditing.Markdown.Notation
{
    /// <summary>One <c>key</c> or <c>key=value</c> item of an attribute block (spec 3.3), with its place in the source.</summary>
    internal sealed class AttrItem
    {
        public AttrItem(string key, string? value, bool quoted, int start, int end)
        {
            Key = key;
            Value = value;
            Quoted = quoted;
            Start = start;
            End = end;
        }

        public string Key { get; }
        /// <summary>The unescaped value; null for a flag.</summary>
        public string? Value { get; }
        public bool Quoted { get; }
        public bool IsFlag => Value == null;
        /// <summary>Offsets in the scanned string, [Start, End).</summary>
        public int Start { get; }
        public int End { get; }
    }

    /// <summary>A scanned <c>{...}</c> block, [Start, End) in the scanned string.</summary>
    internal sealed class AttrBlock
    {
        public AttrBlock(int start, int end, List<AttrItem> items)
        {
            Start = start;
            End = end;
            Items = items;
        }

        public int Start { get; }
        public int End { get; }
        public List<AttrItem> Items { get; }

        /// <summary>The same block with every offset moved by <paramref name="delta"/>: Markdig numbers a table cell's text from the start of the
        /// table, so the parsers shift what they scan to offsets in the source.</summary>
        public AttrBlock Shifted(int delta)
        {
            if (delta == 0) return this;
            var items = new List<AttrItem>(Items.Count);
            foreach (var item in Items) items.Add(new AttrItem(item.Key, item.Value, item.Quoted, item.Start + delta, item.End + delta));
            return new AttrBlock(Start + delta, End + delta, items);
        }
    }

    internal enum ScanStatus { Ok, Unclosed, Malformed }

    /// <summary>The scanner for the attribute grammar of spec 3.3. It reads the whole block in one step, so a <c>}</c>, a space,
    /// a <c>|</c> or an <c>=</c> inside a quoted value never ends it.
    /// <code>
    /// block = "{" item { " " item } "}"      ; "{}" is an error
    /// item  = key | key "=" value
    /// key   = ALPHA { ALPHA | DIGIT }
    /// value = bare | quoted
    /// bare  = 1*( ALPHA | DIGIT | "-" | "_" | "." | "#" )          ; the reader also takes ":"
    /// quoted = DQUOTE { char | "\\" | "\" DQUOTE | "\u" 4HEXDIG } DQUOTE
    /// </code></summary>
    internal static class AttributeScanner
    {
        public static ScanStatus TryScan(string text, int start, int limit, out AttrBlock? block, out int errorPos, out string error)
        {
            string s = text;
            block = null;
            errorPos = start;
            error = string.Empty;
            if (start >= limit || s[start] != '{') { error = "expected {"; return ScanStatus.Malformed; }
            var items = new List<AttrItem>();
            int i = start + 1;
            if (i < limit && s[i] == '}') { errorPos = i; error = "{} is empty"; return ScanStatus.Malformed; }
            while (true)
            {
                while (i < limit && s[i] == ' ') i++;
                if (i >= limit || s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing }"; return ScanStatus.Unclosed; }
                if (s[i] == '}')
                {
                    if (items.Count == 0) { errorPos = i; error = "{} is empty"; return ScanStatus.Malformed; }
                    block = new AttrBlock(start, i + 1, items);
                    return ScanStatus.Ok;
                }
                int itemStart = i;
                if (!IsAlpha(s[i])) { errorPos = i; error = "a key starts with a letter"; return ScanStatus.Malformed; }
                while (i < limit && (IsAlpha(s[i]) || IsDigit(s[i]))) i++;
                string key = s.Substring(itemStart, i - itemStart);
                if (i < limit && s[i] == '=')
                {
                    i++;
                    if (i >= limit || s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing }"; return ScanStatus.Unclosed; }
                    if (s[i] == '"')
                    {
                        var st = ReadQuoted(s, limit, ref i, out var value, out errorPos, out error);
                        if (st != ScanStatus.Ok) return st;
                        items.Add(new AttrItem(key, value, true, itemStart, i));
                    }
                    else
                    {
                        int vs = i;
                        while (i < limit && IsBareOrColon(s[i])) i++;
                        if (i == vs) { errorPos = i; error = "a value is bare letters, digits and - _ . # or quoted"; return ScanStatus.Malformed; }
                        items.Add(new AttrItem(key, s.Substring(vs, i - vs), false, itemStart, i));
                    }
                }
                else
                {
                    items.Add(new AttrItem(key, null, false, itemStart, i));
                }
                if (i < limit && s[i] != ' ' && s[i] != '}')
                {
                    if (s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing }"; return ScanStatus.Unclosed; }
                    errorPos = i;
                    error = "unexpected '" + s[i] + "'";
                    return ScanStatus.Malformed;
                }
            }
        }

        static ScanStatus ReadQuoted(string s, int limit, ref int i, out string value, out int errorPos, out string error)
        {
            value = string.Empty;
            errorPos = i;
            error = string.Empty;
            var sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= limit || s[i] == '\n' || s[i] == '\r') { errorPos = i; error = "no closing quote"; return ScanStatus.Unclosed; }
                char c = s[i];
                if (c == '"') { i++; value = sb.ToString(); return ScanStatus.Ok; }
                if (c == '\\')
                {
                    if (i + 1 >= limit) { errorPos = i; error = "no closing quote"; return ScanStatus.Unclosed; }
                    char n = s[i + 1];
                    if (n == '\\' || n == '"') { sb.Append(n); i += 2; continue; }
                    if (n == 'u' && TryHex4(s, i + 2, limit, out int cp)) { sb.Append((char)cp); i += 6; continue; }
                    errorPos = i;
                    error = "a quoted value escapes only \\\\, \\\" and \\uXXXX";
                    return ScanStatus.Malformed;
                }
                if (char.IsControl(c)) { errorPos = i; error = "a control character in a quoted value is written \\uXXXX"; return ScanStatus.Malformed; }
                sb.Append(c);
                i++;
            }
        }

        static bool TryHex4(string s, int at, int limit, out int value)
        {
            value = 0;
            if (at + 4 > limit) return false;
            for (int k = 0; k < 4; k++)
            {
                int d = HexValue(s[at + k]);
                if (d < 0) return false;
                value = value * 16 + d;
            }
            return true;
        }

        internal static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        static bool IsDigit(char c) => c >= '0' && c <= '9';
        static bool IsBareOrColon(char c) => IsAlpha(c) || IsDigit(c) || c == '-' || c == '_' || c == '.' || c == '#' || c == ':';

        /// <summary>True when the writer may write the value without quotes (spec grammar, no colon).</summary>
        public static bool IsBare(string value)
        {
            if (value.Length == 0) return false;
            foreach (char c in value) if (!(IsAlpha(c) || IsDigit(c) || c == '-' || c == '_' || c == '.' || c == '#')) return false;
            return true;
        }
    }

    /// <summary>Writes attribute values: bare when the grammar allows it, quoted with <c>\"</c>, <c>\\</c> and <c>\uXXXX</c> otherwise.</summary>
    internal static class AttributeText
    {
        /// <param name="escapePipe">In a table row a <c>|</c> would end the cell, so it is written <c>|</c>.</param>
        public static string Value(string value, bool escapePipe)
        {
            if (AttributeScanner.IsBare(value)) return value;
            return Quote(value, escapePipe);
        }

        public static string Quote(string value, bool escapePipe)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (char.IsControl(c) || (escapePipe && c == '|')) AppendU(sb, c);
                else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { sb.Append(c).Append(value[i + 1]); i++; }
                else if (char.IsSurrogate(c)) AppendU(sb, c);
                else sb.Append(c);
            }
            sb.Append('"');
            return sb.ToString();
        }

        static void AppendU(StringBuilder sb, char c) => sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
    }
}
