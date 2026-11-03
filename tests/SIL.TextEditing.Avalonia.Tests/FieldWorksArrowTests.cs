using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Input;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0018 on the surface: arrows are logical by paragraph direction (also inside mixed lines), Shift extends
    /// the same way, the caret is drawn at its logical offset, and normalisation and the NFC clipboard work through the
    /// field. Caret offsets are asserted, not hit tests (Avalonia 11.3 hit-tests right-to-left text wrongly).</summary>
    public class FieldWorksArrowTests
    {
        const string Mixed = "ab عربي cd";   // Latin, four Arabic letters, Latin: 10 characters

        static RichTextField Field(string text, bool rtl)
        {
            var p = Para(rtl ? "ar" : "en", "en", text);
            p.Rtl = rtl;
            return Fixtures.Field(Doc(p), 520);
        }

        public static IEnumerable<object[]> Lines()
        {
            yield return new object[] { "abcdefg", false };
            yield return new object[] { "عربية", true };
            yield return new object[] { Mixed, false };
            yield return new object[] { Mixed, true };
        }

        [Theory, MemberData(nameof(Lines))]
        public void Right_and_left_walk_the_logical_order_by_paragraph_direction(string text, bool rtl) => Headless.Run(() =>
        {
            var f = Field(text, rtl);
            var forward = rtl ? Key.Left : Key.Right;
            var back = rtl ? Key.Right : Key.Left;
            f.Editor.SetCaret(new TextPosition(0, 0));
            for (int k = 1; k <= text.Length; k++)
            {
                f.Press(forward);
                Assert.Equal(k, f.Editor.Caret.Offset);
            }
            f.Press(forward);
            Assert.Equal(text.Length, f.Editor.Caret.Offset);
            for (int k = text.Length - 1; k >= 0; k--)
            {
                f.Press(back);
                Assert.Equal(k, f.Editor.Caret.Offset);
            }
        });

        [Theory, MemberData(nameof(Lines))]
        public void Shift_arrows_extend_in_the_same_logical_direction(string text, bool rtl) => Headless.Run(() =>
        {
            var f = Field(text, rtl);
            f.Editor.SetCaret(new TextPosition(0, 3));
            f.Press(rtl ? Key.Left : Key.Right, KeyModifiers.Shift);
            f.Press(rtl ? Key.Left : Key.Right, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, 3), f.Editor.Anchor);
            Assert.Equal(new TextPosition(0, 5), f.Editor.Caret);
            f.Press(rtl ? Key.Right : Key.Left, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, 4), f.Editor.Caret);
            f.Press(rtl ? Key.Right : Key.Left, KeyModifiers.Shift);
            f.Press(rtl ? Key.Right : Key.Left, KeyModifiers.Shift);
            Assert.Equal(new TextPosition(0, 2), f.Editor.Caret);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void An_arrow_collapses_a_selection_to_its_logical_end(bool rtl) => Headless.Run(() =>
        {
            var f = Field(Mixed, rtl);
            f.Editor.Anchor = new TextPosition(0, 2);
            f.Editor.SetCaret(new TextPosition(0, 6), true);
            f.Press(rtl ? Key.Left : Key.Right);
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
            Assert.False(f.Editor.HasSelection);
            f.Editor.Anchor = new TextPosition(0, 6);
            f.Editor.SetCaret(new TextPosition(0, 2), true);
            f.Press(rtl ? Key.Left : Key.Right);
            Assert.Equal(new TextPosition(0, 6), f.Editor.Caret);
        });

        [Fact]
        public void Arrows_cross_paragraphs_logically() => Headless.Run(() =>
        {
            var rtlPara = Para("ar", "ar", "عر");
            rtlPara.Rtl = true;
            var f = Fixtures.Field(Doc(rtlPara, Para("en", "en", "ab")), 520);
            f.Editor.SetCaret(new TextPosition(0, 2));
            f.Press(Key.Left);                                  // forward in an RTL paragraph: into the next one
            Assert.Equal(new TextPosition(1, 0), f.Editor.Caret);
            f.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 2), f.Editor.Caret);
        });

        [Fact]
        public void Ctrl_arrows_stay_logical_by_word_in_a_right_to_left_paragraph() => Headless.Run(() =>
        {
            var f = Field("عر ab بي", true);
            f.Editor.SetCaret(new TextPosition(0, 0));
            f.Press(Key.Left, KeyModifiers.Control);            // RTL: Ctrl+Left goes to the next word
            Assert.Equal(3, f.Editor.Caret.Offset);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_caret_is_drawn_at_the_visual_place_of_its_logical_offset_on_either_side_of_a_bidi_boundary(bool rtl) => Headless.Run(() =>
        {
            var f = Field(Mixed, rtl);
            var line = f.ParagraphLayoutFor(0).Lines[0].Line;
            // A bidi boundary: the offset is one place on screen when approached from the left and another from the right.
            // LTR paragraph: between the space (left-to-right) and the first Arabic letter, offset 3. RTL paragraph: the space
            // takes the paragraph direction, so the boundary is between "b" and the space, offset 2.
            int at = rtl ? 2 : 3;
            double trailingBefore = line.GetDistanceFromCharacterHit(new CharacterHit(at - 1, 1));
            double leadingAfter = line.GetDistanceFromCharacterHit(new CharacterHit(at, 0));
            Assert.NotEqual(trailingBefore, leadingAfter, 1);
            f.Editor.SetCaret(new TextPosition(0, at - 1));
            f.Press(rtl ? Key.Left : Key.Right);
            Assert.Equal(at, f.Editor.Caret.Offset);
            Assert.Equal(trailingBefore, f.CaretRect().X, 1);
            f.Editor.SetCaret(new TextPosition(0, at + 1));
            f.Press(rtl ? Key.Right : Key.Left);
            Assert.Equal(at, f.Editor.Caret.Offset);
            Assert.Equal(leadingAfter, f.CaretRect().X, 1);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Backspace_and_delete_through_the_field_follow_the_rules(bool rtl) => Headless.Run(() =>
        {
            var f = Field("abعé̂x", rtl);
            f.Editor.SetCaret(new TextPosition(0, 6));
            f.Press(Key.Back);
            Assert.Equal("abعéx", f.Editor.Document.Paragraphs[0].Text);
            f.Editor.SetCaret(new TextPosition(0, 3));
            f.Press(Key.Delete);
            Assert.Equal("abعx", f.Editor.Document.Paragraphs[0].Text);
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Typed_and_composed_text_is_normalised_to_the_form_the_host_sets(bool rtl) => Headless.Run(() =>
        {
            var f = Field("ab", rtl);
            f.Normalization = NormalizationForm.FormD;
            f.Editor.SetCaret(new TextPosition(0, 1));
            f.Type("éأ");
            Assert.Equal("aéأb", f.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(1 + 4, f.Editor.Caret.Offset);
        });

        [Fact]
        public void Plain_text_on_the_clipboard_is_nfc_and_the_internal_format_keeps_the_stored_form() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("café"));
            try
            {
                field.Normalization = NormalizationForm.FormD;
                field.Editor.Anchor = new TextPosition(0, 0);
                field.Editor.SetCaret(new TextPosition(0, 5), true);
                await field.CopyAsync(false);
                var top = global::Avalonia.Controls.TopLevel.GetTopLevel(field);
                Assert.Equal("café", await ClipboardCompat.GetTextAsync(top));
                field.Editor.SetCaret(new TextPosition(0, 5));
                await field.PasteAsync();                       // our own format: stored form
                Assert.Equal("cafécafé", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });
    }
}
