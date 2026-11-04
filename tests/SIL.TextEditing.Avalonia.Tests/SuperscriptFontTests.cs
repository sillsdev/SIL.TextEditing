using System;
using System.IO;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Superscript and subscript text is sized and raised or lowered as the font's own OS/2 table says (its size and its vertical offset for each),
    /// as FieldWorks does, and only where the font gives nothing is it two thirds of the size (issue 110). The expected values are read here from the font
    /// files themselves, by a reader of its own.</summary>
    public class SuperscriptFontTests
    {
        /// <summary>The values of a font file's OS/2 and hhea tables that matter here, in design units.</summary>
        sealed class FontFile
        {
            public int SubSize, SubOffset, SuperSize, SuperOffset, Height;

            public static FontFile Read(string name)
            {
                var data = File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", name));
                int Short(int at) => (short)((data[at] << 8) | data[at + 1]);
                int Long(int at) => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
                int count = (data[4] << 8) | data[5], os2 = -1, hhea = -1;
                for (int i = 0; i < count; i++)
                {
                    string tag = new string(new[] { (char)data[12 + 16 * i], (char)data[13 + 16 * i], (char)data[14 + 16 * i], (char)data[15 + 16 * i] });
                    if (tag == "OS/2") os2 = Long(12 + 16 * i + 8);
                    if (tag == "hhea") hhea = Long(12 + 16 * i + 8);
                }
                return new FontFile
                {
                    SubSize = Short(os2 + 12), SubOffset = Short(os2 + 16), SuperSize = Short(os2 + 20), SuperOffset = Short(os2 + 24),
                    Height = Short(hhea + 4) - Short(hhea + 6) + Short(hhea + 8),
                };
            }
        }

        public static readonly object[][] Fonts =
        {
            new object[] { "Segoe UI", "segoeui.ttf" },
            new object[] { "Times New Roman", "times.ttf" },
            new object[] { "Arial", "arial.ttf" },
            new object[] { "Calibri", "calibri.ttf" },
        };

        static (double Size, double Offset) ScriptRun(string family, int superscript, double size, int ownOffset = 0)
        {
            var p = Para("en", "en", "ab ", "en", "12", "en", " cd");
            foreach (var r in p.Runs) r.Props.SetStr(TextPropertyNumbers.FontFamily, family).SetInt(TextPropertyNumbers.FontSize, (int)(size * 1000), TextPropertyNumbers.VariationMilliPoint);
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Superscript, superscript);
            if (ownOffset != 0) p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, ownOffset, TextPropertyNumbers.VariationMilliPoint);
            var li = Fixtures.Field(Doc(p), 400).ParagraphLayoutFor(0).Lines[0];
            // The size is what the run is shaped at; the offset is what the run is drawn raised by (the layout's own, Avalonia's runs do not carry it).
            return (li.Line.TextRuns[1].Properties!.FontRenderingEmSize, li.Pieces(3, 5).Single().Value);
        }

        [Theory]
        [MemberData(nameof(Fonts))]
        public void A_superscript_and_a_subscript_take_their_size_and_offset_from_the_fonts_OS2_table(string family, string file) => Headless.Run(() =>
        {
            var font = FontFile.Read(file);
            Assert.True(Math.Abs((double)font.SuperSize / font.Height - 2.0 / 3.0) > 0.02, family + " gives a superscript size that is not the fallback");
            var sup = ScriptRun(family, 1, 20);
            Assert.Equal(20.0 * font.SuperSize / font.Height, sup.Size, 2);
            Assert.Equal(20.0 * font.SuperOffset / font.Height, sup.Offset, 2);
            var sub = ScriptRun(family, 2, 20);
            Assert.Equal(20.0 * font.SubSize / font.Height, sub.Size, 2);
            Assert.Equal(-20.0 * font.SubOffset / font.Height, sub.Offset, 2);
        });

        [Fact]
        public void A_run_adds_the_raise_to_the_baseline_offset_it_has_of_its_own() => Headless.Run(() =>
        {
            var font = FontFile.Read("segoeui.ttf");
            var sup = ScriptRun("Segoe UI", 1, 16, 3000);
            Assert.Equal(3 + 16.0 * font.SuperOffset / font.Height, sup.Offset, 2);
        });

        [Theory]
        [InlineData(0, 0, 0, true, 2.0 / 3.0, 1.0 / 3.0)]       // no values, no height: FieldWorks' fallback: two thirds, raised by the rest of the size
        [InlineData(0, 0, 0, false, 2.0 / 3.0, 2.0 / 15.0)]     // and a subscript lowered by a fifth of its own size
        [InlineData(1000, 700, 2000, true, 0.5, 0.35)]          // the font's own values over the font's height
        [InlineData(1000, 400, 2000, false, 0.5, 0.2)]
        [InlineData(1000, 0, 2000, true, 0.5, 0.5)]             // a size and no offset: the fallback offset, which is what is left of the size
        [InlineData(1000, 0, 2000, false, 0.5, 0.1)]
        [InlineData(0, 700, 2000, true, 2.0 / 3.0, 0.35)]       // an offset and no size: the fallback size
        [InlineData(1000, 700, 0, true, 2.0 / 3.0, 1.0 / 3.0)]  // no height to say them over: all of it falls back
        public void The_size_and_the_offset_are_the_fonts_values_over_its_height_and_FieldWorks_values_where_it_has_none(int size, int offset, int height, bool superscript, double scale, double shift)
        {
            var place = ScriptPlacement.From(size, offset, height, superscript);
            Assert.Equal(scale, place.SizeScale, 9);
            Assert.Equal(shift, place.OffsetScale, 9);
        }

        [Theory]
        [InlineData("Times New Roman", "times.ttf")]
        [InlineData("Segoe UI", "segoeui.ttf")]
        public void Pixels_put_the_baseline_of_a_superscript_and_a_subscript_where_the_fonts_offset_says(string family, string file) => Headless.Run(() =>
        {
            var font = FontFile.Read(file);
            // How far the digit one's ink ends above the ink of the plain H beside it: both stand on their baselines, so that is how far the run's baseline is raised.
            int Raised(int superscript)
            {
                var p = Para("en", "en", "H", "en", "1", "en", "H");
                foreach (var r in p.Runs) r.Props.SetStr(TextPropertyNumbers.FontFamily, family).SetInt(TextPropertyNumbers.FontSize, 60000, TextPropertyNumbers.VariationMilliPoint);
                p.Runs[1].Props.SetInt(TextPropertyNumbers.Superscript, superscript);
                var field = Fixtures.Field(Doc(p), 400);
                var pl = field.ParagraphLayoutFor(0);
                var shot = Fixtures.Render(field);
                var rects = new[] { pl.SelectionRects(0, 1).Single(), pl.SelectionRects(1, 2).Single() };
                int Lowest(global::Avalonia.Rect box)
                {
                    for (int y = shot.Height - 1; y >= 0; y--)
                        for (int x = (int)box.X; x < (int)box.Right; x++)
                            if (shot.IsInk(x, y, 160)) return y;
                    return -1;
                }
                int h = Lowest(rects[0]), digit = Lowest(rects[1]);
                Assert.True(h > 0 && digit > 0);
                return h - digit;
            }
            double raise = 60.0 * font.SuperOffset / font.Height, drop = 60.0 * font.SubOffset / font.Height;
            Assert.InRange(Raised(0), -1, 1);
            Assert.InRange(Raised(1), raise - 1.5, raise + 1.5);
            Assert.InRange(-Raised(2), drop - 1.5, drop + 1.5);
        });
    }
}
