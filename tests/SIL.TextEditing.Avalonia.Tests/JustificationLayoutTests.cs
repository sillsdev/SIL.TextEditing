using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A justified paragraph (align 5): every line but the last has the slack of the line shared out among its spaces, so its text
    /// reaches both edges of the text area; the last line stays at the leading edge. The caret, the selection and the mouse follow the
    /// stretched text, on both Avalonia lines.</summary>
    public class JustificationLayoutTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";
        const string MixedLtr = "English words then ابج دهو in the middle and more English after that so the lines wrap, زحط يكل and some more plain text at the end of it.";
        const string MixedRtl = "هذا نص عربي with some English words وكلمات عربية أخرى in the middle of the line and نهاية الفقرة هنا جملة طويلة";

        static ParagraphLayout Layout(string text, bool rtl, int align = 5, double width = 320)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            return Fixtures.Field(Doc(p), width).ParagraphLayoutFor(0);
        }

        /// <summary>The left and right edges of the text of a line, without the space a wrapped line ends with.</summary>
        static (double Left, double Right) Ink(ParagraphLayout pl, LineInfo li)
        {
            int end = pl.TextEnd(li);
            while (end > li.Start && pl.EffectiveText[end - 1] == ' ') end--;
            var rects = pl.SelectionRects(pl.ToModel(li.Start), pl.ToModel(end));
            return (rects.Min(r => r.X), rects.Max(r => r.Right));
        }

        public static readonly object[][] Cases =
        {
            new object[] { English, false },
            new object[] { Arabic, true },
            new object[] { MixedLtr, false },
            new object[] { MixedRtl, true },
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Lines_but_the_last_reach_both_edges_of_the_text_area_and_the_last_stays_at_the_leading_edge(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            Assert.True(pl.Lines.Count >= 3, "lines: " + pl.Lines.Count);
            double left = pl.ContentLeft, right = pl.ContentLeft + pl.ContentWidth;
            for (int i = 0; i < pl.Lines.Count - 1; i++)
            {
                var ink = Ink(pl, pl.Lines[i]);
                Assert.True(System.Math.Abs(ink.Left - left) < 1, "line " + i + " starts at " + ink.Left + ", the area at " + left);
                Assert.True(System.Math.Abs(ink.Right - right) < 1, "line " + i + " ends at " + ink.Right + ", the area at " + right);
            }
            var last = Ink(pl, pl.Lines[pl.Lines.Count - 1]);
            if (rtl)
            {
                Assert.True(System.Math.Abs(last.Right - right) < 1, "the last line starts at the right edge: " + last.Right);
                Assert.True(last.Left > left + 10, "and is not stretched to the left edge: " + last.Left);
            }
            else
            {
                Assert.True(System.Math.Abs(last.Left - left) < 1, "the last line starts at the left edge: " + last.Left);
                Assert.True(last.Right < right - 10, "and is not stretched to the right edge: " + last.Right);
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void The_caret_and_the_mouse_follow_the_stretched_text(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            foreach (var li in pl.Lines)
            {
                foreach (var stop in pl.VisualStops(li))
                {
                    int model = pl.ToModel(stop.Offset);
                    // Clicking on a stop puts the caret where the stop is (at a boundary between directions two places share an offset).
                    var hit = pl.HitTestDetailed(new Point(stop.X, li.Y + 2));
                    Assert.True(System.Math.Abs(pl.CaretRect(hit.Offset, false, hit.CaretHit).X - stop.X) < 1, "a click at " + stop.X + " gave offset " + hit.Offset + " for the stop at offset " + stop.Offset);
                    var caret = pl.CaretRect(model, false, stop.Hit);
                    Assert.True(System.Math.Abs(caret.X - stop.X) < 1, "the caret at " + model + " is at " + caret.X + ", its stop at " + stop.X);
                }
                // Stops run in order and stay inside the text area (apart from the space a wrapped line ends with).
                var xs = pl.VisualStops(li).Select(s => s.X).ToList();
                for (int k = 1; k < xs.Count; k++) Assert.True(xs[k] >= xs[k - 1]);
            }
        });

        [Theory]
        [MemberData(nameof(Cases))]
        public void A_selection_across_a_stretched_line_covers_the_stretched_text_and_no_more(string text, bool rtl) => Headless.Run(() =>
        {
            var pl = Layout(text, rtl);
            var li = pl.Lines[0];
            int end = pl.TextEnd(li);
            while (pl.EffectiveText[end - 1] == ' ') end--;
            var rects = pl.SelectionRects(0, end);
            double total = rects.Sum(r => r.Width);
            Assert.True(System.Math.Abs(total - pl.ContentWidth) < 1.5, "the selection of the line's text is " + total + " wide, the area " + pl.ContentWidth);
            // The spaces are stretched, the words are not.
            int word = text.IndexOf(' ');
            var inner = pl.SelectionRects(0, word);
            var natural = Layout(text, rtl, align: 0).SelectionRects(0, word);
            Assert.Equal(natural.Sum(r => r.Width), inner.Sum(r => r.Width), 1);   // inside a word nothing is stretched
        });

        [Fact]
        public void Other_alignments_and_a_single_line_are_not_stretched() => Headless.Run(() =>
        {
            foreach (int align in new[] { 0, 1, 2, 3, 4 })
            {
                var pl = Layout(English, false, align);
                var natural = Ink(pl, pl.Lines[0]);
                Assert.Equal(pl.Lines[0].Line.Width, natural.Right - natural.Left, 1);   // the text is as wide as Avalonia laid it out
            }
            var single = Layout("one short line", false);
            Assert.Single(single.Lines);
            var ink = Ink(single, single.Lines[0]);
            Assert.True(ink.Right < 200, "a single line is the last line: " + ink.Right);
        });
    }
}
