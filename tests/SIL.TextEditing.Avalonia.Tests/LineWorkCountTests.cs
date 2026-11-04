using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A layout works out the runs of each line once (and a justified line's natural width once), whatever the line needs them for: its height with
    /// shifted runs, its caret and mouse, its justification, its hanging spaces. Counted, not timed (issues 66 and 44).</summary>
    public class LineWorkCountTests
    {
        const string Text = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent. ";

        [Theory]
        [InlineData(5, false)]   // justified
        [InlineData(5, true)]    // justified, with a raised run on the line
        [InlineData(0, true)]
        [InlineData(3, false)]
        public void Each_line_has_its_runs_worked_out_once(int align, bool raised) => Headless.Run(() =>
        {
            var p = Para("en", "en", Text + Text, "fr", "bonjour le monde " + Text, "en", Text);
            if (raised) p.Runs[1].Props.SetInt(TextPropertyNumbers.Offset, 3000, TextPropertyNumbers.VariationMilliPoint);
            p.Props.SetInt(TextPropertyNumbers.Align, align);
            long before = ParagraphLayout.RunSlotBuilds;
            var pl = Fixtures.Field(Doc(p), 260).ParagraphLayoutFor(0);
            long built = ParagraphLayout.RunSlotBuilds - before;
            Assert.True(pl.Lines.Count > 8);
            Assert.True(built <= pl.Lines.Count, "run slots were worked out " + built + " times for " + pl.Lines.Count + " lines");
        });
    }
}
