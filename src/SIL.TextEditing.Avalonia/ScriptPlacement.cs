using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
#if NET10_0_OR_GREATER
using GlyphTypefaceType = Avalonia.Media.GlyphTypeface;
#else
using GlyphTypefaceType = Avalonia.Media.IGlyphTypeface;
#endif

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How big superscript and subscript text is and how far it is raised or lowered: the font's own recommendation, from its OS/2 table (the
    /// size and the vertical offset of each), as FieldWorks reads it, and where the font gives nothing (no table, or a value of zero) FieldWorks' own
    /// fallback: two thirds of the size, a superscript raised by the rest of the height and a subscript lowered by a fifth of its own height.</summary>
    internal readonly struct ScriptPlacement
    {
        ScriptPlacement(double sizeScale, double offsetScale)
        {
            SizeScale = sizeScale;
            OffsetScale = offsetScale;
        }

        /// <summary>The size of the text against the size of the text beside it.</summary>
        public double SizeScale { get; }

        /// <summary>How far the baseline moves, as a share of the size of the text beside it: up for a superscript, down for a subscript.</summary>
        public double OffsetScale { get; }

        /// <summary>The fallback size: two thirds.</summary>
        public const double FallbackSizeScale = CharFormat.DefaultSuperscriptScale;

        /// <summary>The placement from the values of a font's OS/2 table (<paramref name="size"/> and <paramref name="offset"/> of the script asked for, in the
        /// font's design units, zero for none) and the height of the font in the same units. A value of zero, or no height, is a font that does not say.</summary>
        public static ScriptPlacement From(int size, int offset, int fontHeight, bool superscript)
        {
            double scale = size != 0 && fontHeight != 0 ? (double)size / fontHeight : FallbackSizeScale;
            double shift = offset != 0 && fontHeight != 0 ? (double)offset / fontHeight : superscript ? 1 - scale : scale / 5;
            return new ScriptPlacement(scale, shift);
        }

        /// <summary>The placement for a font, from its OS/2 and hhea tables; the fallback when it has no OS/2 table. The height of the font is its
        /// ascent and descent and line gap as its hhea table says, or its metrics when it has none.</summary>
        public static ScriptPlacement For(Typeface typeface, bool superscript)
        {
            var glyphs = typeface.GlyphTypeface;
            if (glyphs == null) return From(0, 0, 0, superscript);
            var values = Cache.GetValue(glyphs, g => ReadValues((GlyphTypefaceType)g));
            return From(superscript ? values.SuperscriptSize : values.SubscriptSize, superscript ? values.SuperscriptOffset : values.SubscriptOffset, values.Height, superscript);
        }

        /// <summary>What a font says, read once for each font: its OS/2 values and its height.</summary>
        sealed class FontValues
        {
            public int SubscriptSize, SubscriptOffset, SuperscriptSize, SuperscriptOffset, Height;
        }

        static readonly ConditionalWeakTable<object, FontValues> Cache = new ConditionalWeakTable<object, FontValues>();

        static FontValues ReadValues(GlyphTypefaceType glyphs)
        {
            var values = new FontValues();
            var metrics = glyphs.Metrics;
            values.Height = metrics.Descent - metrics.Ascent + metrics.LineGap;
            if (TryGetTable(glyphs, 'h', 'h', 'e', 'a', out var hhea) && hhea.Length >= 10)
                values.Height = Int16(hhea, 4) - Int16(hhea, 6) + Int16(hhea, 8);
            if (TryGetTable(glyphs, 'O', 'S', '/', '2', out var os2) && os2.Length >= 26)
            {
                values.SubscriptSize = Int16(os2, 12);
                values.SubscriptOffset = Int16(os2, 16);
                values.SuperscriptSize = Int16(os2, 20);
                values.SuperscriptOffset = Int16(os2, 24);
            }
            return values;
        }

        /// <summary>A big-endian signed 16-bit value of a font table.</summary>
        static int Int16(byte[] table, int at) => (short)((table[at] << 8) | table[at + 1]);

#if NET10_0_OR_GREATER
        // Avalonia 12 gives no access to a font's tables, only to its file (on Avalonia 11.3 a typeface gives a table by its tag): a table is found in the file's
        // table directory. A font collection is read as its first font.
        static bool TryGetTable(GlyphTypefaceType glyphs, char a, char b, char c, char d, out byte[] table)
        {
            table = Array.Empty<byte>();
            try
            {
                Stream? stream = null;
                if (!glyphs.PlatformTypeface.TryGetStream(out stream) || stream == null) return false;
                using (stream)
                {
                    // The font's own table directory, then just the table asked for: a whole font file can be many megabytes.
                    Func<long, int, byte[]?> read = stream.CanSeek ? (Func<long, int, byte[]?>)((at, length) => ReadAt(stream, at, length)) : ReadFrom(ReadAll(stream));
                    var header = read(0, 16);
                    if (header == null) return false;
                    long start = 0;
                    if (header[0] == 't' && header[1] == 't' && header[2] == 'c' && header[3] == 'f') start = (uint)Int32(header, 12);
                    var directory = read(start, 12);
                    if (directory == null) return false;
                    int count = (directory[4] << 8) | directory[5];
                    var entries = read(start + 12, 16 * count);
                    if (entries == null) return false;
                    for (int i = 0; i < count; i++)
                    {
                        int entry = 16 * i;
                        if (entries[entry] != a || entries[entry + 1] != b || entries[entry + 2] != c || entries[entry + 3] != d) continue;
                        uint offset = (uint)Int32(entries, entry + 8), length = (uint)Int32(entries, entry + 12);
                        if (length > 1 << 20) return false;
                        var found = read(offset, (int)length);
                        if (found == null) return false;
                        table = found;
                        return true;
                    }
                }
            }
            catch (IOException) { }
            catch (NotSupportedException) { }
            return false;
        }

        static int Int32(byte[] data, int at) => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];

        static byte[]? ReadAt(Stream stream, long at, int length)
        {
            if (at < 0 || at + length > stream.Length) return null;
            stream.Position = at;
            var bytes = new byte[length];
            int done = 0;
            while (done < length)
            {
                int n = stream.Read(bytes, done, length - done);
                if (n <= 0) return null;
                done += n;
            }
            return bytes;
        }

        static Func<long, int, byte[]?> ReadFrom(byte[] data) => (at, length) =>
        {
            if (at < 0 || at + length > data.Length) return null;
            var bytes = new byte[length];
            Array.Copy(data, at, bytes, 0, length);
            return bytes;
        };

        static byte[] ReadAll(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
#else
        static bool TryGetTable(GlyphTypefaceType glyphs, char a, char b, char c, char d, out byte[] table)
        {
            table = Array.Empty<byte>();
            if (!glyphs.TryGetTable(((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d, out var found) || found == null) return false;
            table = found;
            return true;
        }
#endif
    }
}
