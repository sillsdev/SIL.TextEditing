using System;
using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Avalonia 11.3 throws "Cannot split: requested length N consumes entire run" for more than a zero width joiner: for a space followed by a combining
    /// mark or a variation selector in a paragraph with Arabic or Hebrew in it. Typed text must not crash the layout (issue 112, follow-up of its review).</summary>
    public class SplitFaultTests
    {
        static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

        const string Arabic = "جمل";
        const string Hebrew = "שלום";

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0x300) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + " ", "en", C(0x300) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " ", "en", C(0x300) + "x" } };
            yield return new object[] { false, new[] { "en", "abc def " + C(0x300) + Arabic } };
            yield return new object[] { true, new[] { "he", Hebrew + " " + C(0x300) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + "  " + C(0x300) + "1" } };
            yield return new object[] { true, new[] { "ar", Arabic + "  " + C(0x300) + "1" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0xFE0F) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0xE0100) + "x" } };
            yield return new object[] { true, new[] { "ar", Arabic + " " + C(0x34F) + "x" } };
            yield return new object[] { false, new[] { "ar", Arabic + " " + C(0x34F) + C(0x300) + "x " + C(0xFE0F) + " " + C(0x301) + "y" } };
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_space_followed_by_a_mark_or_a_variation_selector_in_a_paragraph_with_Arabic_or_Hebrew_lays_out_at_every_width_though_Avalonia_11_3_throws_Cannot_split(bool rtl, string[] wsAndText) => Headless.Run(() =>
        {
            int laid = 0;
            for (double width = 15; width <= 330; width += 1)
            {
                var p = Para(wsAndText[0], wsAndText);
                p.Rtl = rtl;
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
                Assert.Equal(p.Length, pl.Lines[pl.Lines.Count - 1].End);
                for (int i = 0; i <= p.Length; i++) pl.CaretRect(i);   // the text is the real text: every offset has a caret
                laid++;
            }
            Assert.Equal(316, laid);
        });

        [Fact]
        public void A_label_with_a_zero_width_joiner_and_Arabic_and_a_mark_after_a_space_does_not_crash_the_layout() => Headless.Run(() =>
        {
            for (double width = 40; width <= 200; width += 5)
            {
                var p = Para("en", "en", "abc def ghi jkl");
                p.Props.SetInt(TextPropertyNumbers.BulNumScheme, BulletNumbers.Arabic);
                p.Props.SetStr(TextPropertyNumbers.BulNumTxtBefore, Arabic + " " + C(0x300) + "‍");
                var pl = Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
                Assert.True(pl.Lines.Count >= 1);
            }
        });

        [Fact]
        public void A_line_limit_ellipsis_beside_such_text_does_not_crash_the_layout() => Headless.Run(() =>
        {
            for (double width = 40; width <= 200; width += 5)
            {
                var p = Para("ar", "ar", Arabic + " " + C(0x300) + "x " + Arabic + " " + C(0x300) + "y " + Arabic + " " + Arabic + " " + Arabic);
                p.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
                Assert.True(Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0).Lines.Count >= 1);
            }
        });
    }
}
