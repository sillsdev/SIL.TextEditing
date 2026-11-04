using System;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The spaces a wrapped line ends with hang outside its text, so a line is aligned by its text: the visible text of a right-aligned line
    /// ends at the right edge of the text area, that of a left-aligned one starts at the left edge and that of a centred one is centred, in either
    /// direction, whatever room the spaces need. As in FieldWorks, which leaves them out of the width a line is aligned by.</summary>
    public class HangingSpaceAlignmentTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        static string Spaced(string text, int spaces) => string.Join(new string(' ', spaces), text.Split(' '));

        static ParagraphLayout Layout(string text, bool rtl, int align, double width, int spaces = 1)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", Spaced(text, spaces));
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        /// <summary>Where a wrapped line's text, without the spaces it ends with, is: its left and right edges in paragraph coordinates.</summary>
        internal static (double Left, double Right) Ink(ParagraphLayout pl, LineInfo li)
        {
            int core = pl.TextEnd(li);
            while (core > li.Start && pl.EffectiveText[core - 1] == ' ') core--;
            var rects = pl.SelectionRects(li.Start, core);
            return (rects.Min(r => r.X), rects.Max(r => r.Right));
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false, 1 },
            new object[] { English, false, 2 },
            new object[] { English, false, 3 },
            new object[] { English, false, 4 },
            new object[] { Arabic, true, 1 },
            new object[] { Arabic, true, 2 },
            new object[] { Arabic, true, 3 },
            new object[] { Arabic, true, 4 },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_wrapped_line_is_aligned_by_its_text_not_by_the_spaces_it_ends_with(string text, bool rtl, int align) => Headless.Run(() =>
        {
            // Where the alignment goes: Left and Right as they say, Trailing at the end of the paragraph's direction.
            bool left = align == 1 || (align == 4 && rtl);
            bool centre = align == 2;
            int checkedLines = 0;
            foreach (int spaces in new[] { 1, 3 })
                for (double width = 200; width <= 260; width += 7)
                {
                    var pl = Layout(text, rtl, align, width, spaces);
                    double leftEdge = pl.ContentLeft, rightEdge = pl.ContentLeft + pl.ContentWidth;
                    for (int i = 0; i < pl.Lines.Count - 1; i++)
                    {
                        var li = pl.Lines[i];
                        // Only lines with room for their spaces, and for as many again beside a centred one: one whose spaces do not fit is the other test's.
                        double hang = li.Line.WidthIncludingTrailingWhitespace - li.Line.Width;
                        if (li.Line.WidthIncludingTrailingWhitespace > pl.ContentWidth || (centre && li.Line.Width + 2 * hang > pl.ContentWidth)) continue;
                        var (a, b) = Ink(pl, li);
                        string where = "width " + width + ", " + spaces + " spaces, line " + i + ": text " + a + " to " + b + " in " + leftEdge + " to " + rightEdge;
                        if (centre) Assert.True(Math.Abs((a + b) / 2 - (leftEdge + rightEdge) / 2) < 0.75, where);
                        else if (left) Assert.True(Math.Abs(a - leftEdge) < 0.75, where);
                        else Assert.True(Math.Abs(b - rightEdge) < 0.75, where);
                        checkedLines++;
                    }
                }
            Assert.True(checkedLines > 30, "lines checked: " + checkedLines);
        });

        [Fact]
        public void The_text_of_a_right_aligned_wrapped_line_reaches_the_right_edge_in_the_picture() => Headless.Run(() =>
        {
            // Three spaces between the words, so each wrapped line ends with three that hang: a dozen pixels the line used to stop short by.
            var p = Para("en", "en", Spaced(English, 3));
            p.Props.SetInt(TextPropertyNumbers.Align, 3);
            const double width = 330;
            var field = Fixtures.Field(Doc(p), width);
            var pl = field.ParagraphLayoutFor(0);
            Assert.True(pl.Lines.Count > 2);
            var shot = Fixtures.Render(field);
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var li = pl.Lines[i];
                int right = -1;
                for (int x = shot.Width - 1; x >= 0 && right < 0; x--)
                    if (shot.ColumnHasInk(x, li.TextY + 2, li.TextY + li.Line.Height - 2)) right = x;
                Assert.True(right >= width - 3, "line " + i + ": the last ink is at x " + right + " in a field " + width + " wide");
            }
        });
    }
}
