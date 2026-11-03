using Avalonia;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The hard line break (U+2028, Shift+Enter) ends a line for good: the text after it starts a new line, the break
    /// itself is not drawn, and a caret before it sits at the end of its line, not at the start of the next.</summary>
    public class HardLineBreakLayoutTests
    {
        static readonly string Break = DocumentEditor.LineSeparator.ToString();
        static RichTextField Field(string text, int caret, bool rtl = false, System.Action<Paragraph>? shape = null)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            shape?.Invoke(p);
            var field = Fixtures.Field(Doc(p), 520);
            field.Editor.SetCaret(new TextPosition(0, caret));
            return field;
        }

        static (double X, double Y) Caret(RichTextField field) { var r = field.CaretRect(); return (r.X, r.Y); }

        [Fact]
        public void Text_after_the_break_starts_a_new_line_and_the_break_is_not_drawn() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(3, pl.Lines[1].Start);
            Assert.Equal(2, pl.TextEnd(pl.Lines[0]));   // the line's last caret place is before the break
            var plain = Field("ab", 2).ParagraphLayoutFor(0);
            Assert.Equal(plain.CaretRect(2).X, pl.CaretRect(2).X, 3);   // the break has no width
            Assert.Equal(pl.Lines[0].TextY, pl.CaretRect(2).Y, 3);
            Assert.Equal(pl.Lines[1].TextY, pl.CaretRect(3).Y, 3);
            Assert.Equal(plain.CaretRect(0).X, pl.CaretRect(3).X, 3);
        });

        [Fact]
        public void Shift_Enter_at_the_end_of_a_paragraph_puts_the_caret_on_a_new_empty_line() => Headless.Run(() =>
        {
            var field = Field("ab", 2);
            var before = Caret(field);
            Assert.True(field.HandleKey(Key.Enter, KeyModifiers.Shift));
            Assert.Equal("ab" + Break, field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Fixtures.Layout(field, 520);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            var after = Caret(field);
            Assert.True(after.Y > before.Y + 5, "the caret moved down a line");
            Assert.Equal(pl.Lines[1].TextY, after.Y, 3);
            Assert.True(after.X < before.X, "and to the start of the line");
            Assert.True(field.DesiredSize.Height >= pl.Lines[1].Y + pl.Lines[1].Height);   // the empty line takes room
        });

        [Fact]
        public void Shift_Enter_at_the_start_of_a_paragraph_leaves_an_empty_first_line() => Headless.Run(() =>
        {
            var field = Field("ab", 0);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Fixtures.Layout(field, 520);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(new TextPosition(0, 1), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);   // before the break, on the empty first line
        });

        [Fact]
        public void End_on_the_line_before_the_break_puts_the_caret_before_it() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 1);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.Equal(pl.CaretRect(2).X, Caret(field).X, 3);
            field.Press(Key.Home);
            Assert.Equal(new TextPosition(0, 0), field.Editor.Caret);
            field.Editor.SetCaret(new TextPosition(0, 4));
            field.Press(Key.Home);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
        });

        [Fact]
        public void Right_and_left_cross_the_break_one_position_at_a_time_and_draw_the_caret_on_the_right_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 1);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            double endX = Caret(field).X;
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            Assert.Equal(pl.CaretRect(3).X, Caret(field).X, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 4), field.Editor.Caret);

            field.Editor.SetCaret(new TextPosition(0, 4));
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.Equal(endX, Caret(field).X, 3);
            field.Press(Key.Left);
            Assert.Equal(new TextPosition(0, 1), field.Editor.Caret);
        });

        [Fact]
        public void Up_and_down_move_between_the_lines_either_side_of_the_break() => Headless.Run(() =>
        {
            var field = Field("abcd" + Break + "efgh", 2);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Down);
            Assert.Equal(new TextPosition(0, 7), field.Editor.Caret);   // same x on the line after the break
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Up);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);

            field.Editor.SetCaret(new TextPosition(0, 4));   // end of the first line
            field.Press(Key.Down);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            Assert.True(field.Editor.Caret.Offset >= 5);
            field.Editor.SetCaret(new TextPosition(0, 9));   // end of the second line
            field.Press(Key.Up);
            Assert.Equal(new TextPosition(0, 4), field.Editor.Caret);   // before the break, not after it
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
        });

        [Fact]
        public void Down_from_the_line_before_a_trailing_break_reaches_the_empty_last_line_and_up_returns() => Headless.Run(() =>
        {
            var field = Field("abcd" + Break, 2);
            var pl = field.ParagraphLayoutFor(0);
            field.Press(Key.Down);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Up);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            Assert.True(field.Editor.Caret.Offset <= 4);
        });

        [Fact]
        public void A_click_beside_the_line_before_the_break_puts_the_caret_before_it() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.HitTest(new Point(400, pl.Lines[0].Y + 2)));
            Assert.Equal(5, pl.HitTest(new Point(400, pl.Lines[1].Y + 2)));
            Assert.Equal(3, pl.HitTest(new Point(0, pl.Lines[1].Y + 2)));
        });

        [Fact]
        public void A_click_on_the_empty_last_line_puts_the_caret_after_the_trailing_break() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 0);
            var pl = field.ParagraphLayoutFor(0);
            var at = pl.Lines[1].Y + 2;
            Assert.Equal(3, pl.HitTest(new Point(100, at)));
            var located = field.PointToPosition(new Point(100, field.ParagraphTop(0) + at));
            Assert.Equal(new TextPosition(0, 3), located);
        });

        [Fact]
        public void The_selection_across_a_break_covers_the_text_either_side_and_nothing_for_the_break() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 0);
            var pl = field.ParagraphLayoutFor(0);
            var rects = pl.SelectionRects(0, 5);
            Assert.Equal(2, rects.Count);
            Assert.Equal(pl.Lines[0].TextY, rects[0].Y, 3);
            Assert.Equal(pl.Lines[1].TextY, rects[1].Y, 3);
        });

        [Fact]
        public void A_right_to_left_paragraph_breaks_and_moves_across_the_break_the_same_way() => Headless.Run(() =>
        {
            var field = Field("عر" + Break + "بي", 1, rtl: true);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            field.Press(Key.End);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
            field.Press(Key.Left);   // forward in a right-to-left paragraph
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 2), field.Editor.Caret);
            Assert.Equal(pl.Lines[0].TextY, Caret(field).Y, 3);
        });

        [Fact]
        public void A_line_limit_still_counts_hard_lines_and_a_caret_never_rests_in_the_hidden_ones() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd" + Break + "ef", 0, shape: p => p.Props.SetInt(TextPropertyNumbers.MaxLines, 2));
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Truncated);
            Assert.Equal(2, pl.Lines.Count);
            Assert.Equal(5, pl.VisibleEnd);   // before the second break
            field.Editor.SetCaret(new TextPosition(0, 8));
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
            field.Press(Key.Right);
            Assert.Equal(new TextPosition(0, 5), field.Editor.Caret);
        });

        [Fact]
        public void A_bulleted_paragraph_with_a_break_labels_only_its_first_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break + "cd", 3, shape: p => p.Props.SetInt(TextPropertyNumbers.BulNumScheme, 101).SetInt(TextPropertyNumbers.LeadingIndent, 36000, 1).SetInt(TextPropertyNumbers.FirstIndent, -18000, 1));
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.Lines.Count);
            Assert.True(pl.LabelRect.Width > 0);
            Assert.Equal(pl.Lines[0].TextY, pl.LabelRect.Y, 3);
            Assert.Equal(pl.Lines[1].TextY, Caret(field).Y, 3);
        });

        static RichTextField Limited(string text, int caret, int maxLines) =>
            Field(text, caret, shape: p => p.Props.SetInt(TextPropertyNumbers.MaxLines, maxLines));

        static string Text(RichTextField field) => field.Editor.Document.Paragraphs[0].Text;

        [Theory]
        [InlineData("ab", 2, 1)]
        [InlineData("ab", 1, 1)]
        [InlineData("ab\u2028cd", 5, 2)]
        [InlineData("ab\u2028cd", 1, 2)]
        public void Shift_Enter_is_refused_when_the_new_line_would_be_hidden(string text, int caret, int maxLines) => Headless.Run(() =>
        {
            var field = Limited(text, caret, maxLines);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Assert.Equal(text, Text(field));
            Assert.Equal(new TextPosition(0, caret), field.Editor.Caret);
        });

        [Fact]
        public void Shift_Enter_still_works_when_the_new_line_fits_under_the_limit() => Headless.Run(() =>
        {
            var field = Limited("ab", 2, 2);
            field.HandleKey(Key.Enter, KeyModifiers.Shift);
            Assert.Equal("ab" + Break, Text(field));
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            Assert.Equal(2, field.ParagraphLayoutFor(0).Lines.Count);
            Assert.False(field.ParagraphLayoutFor(0).Truncated);
        });

        [Theory]
        [InlineData(1, "ab")]
        [InlineData(2, "ab" + "\u2028" + "cd")]
        public void A_trailing_break_with_nothing_after_it_is_not_hidden_text_and_the_caret_stays_before_it(int maxLines, string text) => Headless.Run(() =>
        {
            var field = Limited(text + Break, 0, maxLines);
            var pl = field.ParagraphLayoutFor(0);
            Assert.False(pl.Truncated);
            Assert.Equal(text.Length, pl.VisibleEnd);
            field.Editor.SetCaret(new TextPosition(0, text.Length + 1));
            Assert.Equal(new TextPosition(0, text.Length), field.Editor.Caret);   // pulled back before the break
        });

        [Theory]
        [InlineData(1, "ab")]
        [InlineData(2, "ab" + "\u2028" + "cd")]
        public void The_keyboard_recovers_from_a_hidden_trailing_break(int maxLines, string text)
        {
            // Typing before the break, Delete (removes the break), and Backspace all work, so the field is never locked.
            Headless.Run(() =>
            {
                var typing = Limited(text + Break, text.Length, maxLines);
                typing.Type("x");
                Assert.Equal(text + "x" + Break, Text(typing));
            });
            Headless.Run(() =>
            {
                var delete = Limited(text + Break, text.Length, maxLines);
                delete.Press(Key.Delete);
                Assert.Equal(text, Text(delete));
            });
            Headless.Run(() =>
            {
                var back = Limited(text + Break, text.Length, maxLines);
                back.Press(Key.Back);
                Assert.Equal(text.Substring(0, text.Length - 1) + Break, Text(back));
                for (int i = 0; i < 6; i++) back.Press(Key.Back);   // Backspace can empty the field, but typing then still works
                back.Type("z");
                Assert.Contains("z", Text(back));
            });
        }

        [Fact]
        public void A_paragraph_that_is_only_a_break_in_a_one_line_field_takes_typing_and_Delete() => Headless.Run(() =>
        {
            var typing = Limited(Break, 0, 1);
            typing.Type("x");
            Assert.Equal("x" + Break, Text(typing));
            var delete = Limited(Break, 0, 1);
            delete.Press(Key.Delete);
            Assert.Equal(string.Empty, Text(delete));
        });

        [Fact]
        public void Text_after_a_break_that_the_limit_hides_still_refuses_Delete_and_typing_at_the_visible_end() => Headless.Run(() =>
        {
            var field = Limited("ab" + Break + "cd", 2, 1);
            Assert.True(field.ParagraphLayoutFor(0).Truncated);
            field.Press(Key.Delete);
            field.Type("x");
            Assert.Equal("ab" + Break + "cd", Text(field));
        });

        [Fact]
        public void Backspace_from_the_next_paragraph_is_refused_when_the_one_above_ends_in_a_hidden_break() => Headless.Run(() =>
        {
            var p0 = Para("en", "en", "ab" + Break);
            p0.Props.SetInt(TextPropertyNumbers.MaxLines, 1);
            var field = Fixtures.Field(Doc(p0, Para("en", "en", "cd")), 520);
            field.Editor.SetCaret(new TextPosition(1, 0));
            field.Press(Key.Back);
            Assert.Equal(2, field.Editor.Document.Paragraphs.Count);
        });

        [Fact]
        public void A_preedit_before_a_trailing_break_draws_its_caret_after_the_preedit_on_the_same_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 2);
            field.SetPreedit("xy", 2);
            var pl = field.ParagraphLayoutFor(0);
            Assert.Equal(2, pl.PreeditAt);
            var caret = Caret(field);
            Assert.Equal(pl.Lines[0].TextY, caret.Y, 3);
            Assert.Equal(pl.CaretRect(4, true).X, caret.X, 3);   // after "abxy"
        });

        [Fact]
        public void Selecting_only_a_hard_break_draws_a_small_block_at_the_end_of_its_line() => Headless.Run(() =>
        {
            var field = Field("ab" + Break, 0);
            var pl = field.ParagraphLayoutFor(0);
            var rects = pl.SelectionRects(2, 3);
            var block = Assert.Single(rects);
            Assert.Equal(pl.CaretRect(2).X, block.X, 3);
            Assert.True(block.Width >= 2 && block.Height > 5);
            Assert.Equal(pl.Lines[0].TextY, block.Y, 3);
            // text plus the break draws the text's rectangle only, as before
            Assert.Single(pl.SelectionRects(1, 3));
            // a break-only middle line
            var mid = Field("a" + Break + Break + "b", 0).ParagraphLayoutFor(0);
            Assert.Single(mid.SelectionRects(2, 3));
        });
    }
}
