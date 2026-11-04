using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>End on a line that wraps at a space: the caret stays at the offset after the space (the end of the line, as typing and a
    /// selection see it) but is drawn inside the text area, not one space width past it.</summary>
    public class LineEndCaretTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        static ParagraphLayout Layout(string text, bool rtl, int align, double width)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false, 0 },
            new object[] { English, false, 5 },
            new object[] { Arabic, true, 0 },
            new object[] { Arabic, true, 5 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_end_of_a_wrapped_line_is_drawn_inside_the_text_area_whatever_the_width(string text, bool rtl, int align) => Headless.Run(() =>
        {
            int checkedLines = 0;
            for (double width = 280; width <= 340; width += 1)
            {
                var pl = Layout(text, rtl, align, width);
                double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
                for (int i = 0; i < pl.Lines.Count - 1; i++)
                {
                    int end = pl.LineEnd(i, out var hit);
                    Assert.Equal(pl.Lines[i].End, end);   // the offset is still after the space: the end of the line
                    var caret = pl.CaretRect(end, false, hit);
                    Assert.True(caret.X >= left - 0.5 && caret.X <= right + 0.5, "width " + width + " line " + i + ": the End caret is at " + caret.X + ", the area " + left + " to " + right);
                    var stops = pl.VisualStops(pl.Lines[i]);
                    // The trailing side of the line: its hanging space must not put a stop outside the area. (The leading side is not checked: an
                    // Avalonia right-to-left line whose text fits but not its trailing space starts up to a space width outside the area.)
                    Assert.True(stops.All(s => rtl ? s.X >= left - 0.5 : s.X <= right + 0.5), "width " + width + " line " + i + ": a stop lies outside the area " + left + " to " + right);
                    checkedLines++;
                }
            }
            Assert.True(checkedLines > 60);
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_click_at_the_end_of_a_wrapped_line_stays_on_that_line_and_the_end_caret_is_where_it_would_click(string text, bool rtl, int align) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl, align, 320);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int end = pl.LineEnd(i, out var hit);
                var caret = pl.CaretRect(end, false, hit);
                var clicked = pl.HitTestDetailed(new Point(caret.X, li.Y + 2));
                Assert.True(clicked.Offset == end || clicked.Offset == end - 1, "the click at the End caret gave " + clicked.Offset + ", the end is " + end);
                Assert.True(System.Math.Abs(pl.CaretRect(clicked.Offset, false, clicked.CaretHit).X - caret.X) < 1);
            }
        });

        [Fact]
        public void A_line_with_room_to_spare_keeps_the_end_caret_after_its_space() => Headless.Run(() =>
        {
            var pl = Layout("one two three four five six seven", false, 0, 120);
            Assert.True(pl.Lines.Count > 2);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int core = pl.TextEnd(li);
                while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
                double ink = pl.SelectionRects(li.Start, core).Max(r => r.Right);
                int end = pl.LineEnd(i, out var hit);
                var caret = pl.CaretRect(end, false, hit);
                if (li.Line.WidthIncludingTrailingWhitespace <= pl.ContentWidth)
                    Assert.True(caret.X > ink + 1, "line " + i + ": the caret after the space sits past the last letter when the space fits: " + caret.X + " vs " + ink);
            }
        });
    }
}