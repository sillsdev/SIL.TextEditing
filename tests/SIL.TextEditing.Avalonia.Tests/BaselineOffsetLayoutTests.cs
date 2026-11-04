using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A run's baseline offset (raised or lowered, in points drawn as pixels): the line makes room for it, the caret, the selection
    /// and the squiggle follow it, and the lines and offsets of everything else stay where they were.</summary>
    public class BaselineOffsetLayoutTests
    {
        static RichTextField Field(int offsetMilliPoints, bool rtl = false)
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Rtl = rtl;
            if (offsetMilliPoints != 0) p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, offsetMilliPoints, TextPropertyNumbers.VariationMilliPoint);
            return Fixtures.Field(Doc(p), 520);
        }

        static ParagraphLayout Layout(int offset, bool rtl = false) => Field(offset, rtl).ParagraphLayoutFor(0);

        [Fact]
        public void A_raised_run_makes_the_line_taller_above_and_a_lowered_run_makes_it_taller_below() => Headless.Run(() =>
        {
            var plain = Layout(0).Lines[0];
            var raised = Layout(4000).Lines[0];
            var lowered = Layout(-4000).Lines[0];
            Assert.Equal(plain.Height + 4, raised.Height, 2);
            Assert.Equal(plain.Height + 4, lowered.Height, 2);
            Assert.Equal(plain.Line.Height, raised.Line.Height, 2);   // Avalonia's line is not changed, the slot is
            Assert.Equal(raised.Y + 4, raised.TextY, 2);               // the unshifted text sits 4 below the slot's top
            Assert.Equal(lowered.Y, lowered.TextY, 2);                 // and with a lowered run the room is at the bottom
            // A paragraph below is pushed down by the extra room, not overlapped.
            Assert.Equal(Layout(0).Height + 4, Layout(4000).Height, 2);
        });

        [Fact]
        public void The_caret_inside_or_after_a_raised_run_is_raised_and_before_it_is_not() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            double top = pl.Lines[0].TextY;
            Assert.Equal(top, pl.CaretRect(1).Y, 2);       // between a and b: plain
            Assert.Equal(top, pl.CaretRect(2).Y, 2);       // at the start of the raised run, the caret follows the character before it
            Assert.Equal(top - 4, pl.CaretRect(3).Y, 2);   // inside the raised run
            Assert.Equal(top - 4, pl.CaretRect(4).Y, 2);   // at its end, still raised (typing there continues it)
            Assert.Equal(top, pl.CaretRect(5).Y, 2);
            var lowered = Layout(-4000);
            Assert.Equal(lowered.Lines[0].TextY + 4, lowered.CaretRect(3).Y, 2);
            Assert.Equal(pl.CaretRect(3).Height, pl.CaretRect(1).Height, 2);
        });

        [Fact]
        public void The_selection_of_a_raised_run_is_raised_with_it_and_the_rest_is_not() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            var rects = pl.SelectionRects(0, 6).OrderBy(r => r.X).ToList();
            double top = pl.Lines[0].TextY;
            Assert.Contains(rects, r => System.Math.Abs(r.Y - top) < 0.01);
            Assert.Contains(rects, r => System.Math.Abs(r.Y - (top - 4)) < 0.01);
            var only = Assert.Single(pl.SelectionRects(2, 4));
            Assert.Equal(top - 4, only.Y, 2);
            Assert.Equal(pl.CaretRect(2, true).X, only.X, 1);
            Assert.Equal(pl.CaretRect(4, true).X, only.Right, 1);
            var low = Assert.Single(Layout(-4000).SelectionRects(2, 4));
            Assert.Equal(Layout(-4000).Lines[0].TextY + 4, low.Y, 2);
            // The plain runs on both sides keep the line's own text box.
            var before = Assert.Single(pl.SelectionRects(0, 2));
            Assert.Equal(top, before.Y, 2);
        });

        [Fact]
        public void Hit_testing_and_vertical_places_ignore_the_offset_so_the_mouse_finds_the_same_characters() => Headless.Run(() =>
        {
            var pl = Layout(4000);
            var plain = Layout(0);
            foreach (int offset in new[] { 0, 1, 2, 3, 4, 5, 6 })
            {
                double x = plain.CaretRect(offset).X;
                var raisedX = pl.CaretRect(offset).X;
                Assert.Equal(x, raisedX, 2);   // the offset moves the run up, not along
                Assert.Equal(offset, pl.HitTest(new Point(raisedX, pl.Lines[0].Y + 2)));
                Assert.Equal(offset, pl.HitTest(new Point(raisedX, pl.Lines[0].Y + pl.Lines[0].Height - 2)));
            }
        });

        [Fact]
        public void A_right_to_left_paragraph_raises_the_same_way() => Headless.Run(() =>
        {
            var pl = Layout(4000, rtl: true);
            Assert.Equal(Layout(0, rtl: true).Lines[0].Height + 4, pl.Lines[0].Height, 2);
            Assert.Equal(pl.Lines[0].TextY - 4, pl.CaretRect(3).Y, 2);
            Assert.Equal(pl.Lines[0].TextY - 4, Assert.Single(pl.SelectionRects(2, 4)).Y, 2);
        });
    }
}
