using System.Linq;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>An IME composition is drawn in the character formatting the committed text will get (the run before the caret, as typing
    /// does), with the composition underline.</summary>
    public class PreeditFormattingTests
    {
        static TextRunProperties PreeditProperties(RichTextField field, string preedit)
        {
            field.SetPreedit(preedit, null);
            Fixtures.Layout(field, 400);
            var line = field.ParagraphLayoutFor(0).Lines[0].Line;
            return line.TextRuns.First(r => r.Text.ToString().Contains(preedit)).Properties!;
        }

        static Document BoldRedRun(string ws = "en")
        {
            var doc = Doc(Para("en", ws, "abc def"));
            doc.Paragraphs[0].Runs[0].Props
                .SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn)
                .SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);   // BGR: red
            return doc;
        }

        [Fact]
        public void A_composition_inside_bold_coloured_text_is_bold_and_coloured() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(FontWeight.Bold, properties.Typeface.Weight);
            Assert.Equal(Colors.Red, ((ISolidColorBrush)properties.ForegroundBrush!).Color);
        });

        [Fact]
        public void A_composition_at_the_end_of_a_run_takes_that_runs_formatting_and_gets_the_composition_underline() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 7));
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(FontWeight.Bold, properties.Typeface.Weight);
            Assert.NotNull(properties.TextDecorations);
            Assert.Single(properties.TextDecorations!);
        });

        [Fact]
        public void A_composition_in_a_superscript_run_is_superscript() => Headless.Run(() =>
        {
            var doc = OneParagraph("abc def");
            doc.Paragraphs[0].Runs[0].Props.SetInt(TextPropertyNumbers.Superscript, 1);
            var field = Fixtures.Field(doc, 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            var properties = PreeditProperties(field, "xyz");
            Assert.Equal(BaselineAlignment.Superscript, properties.BaselineAlignment);
        });

        [Fact]
        public void A_composition_in_another_writing_system_takes_nothing_from_the_neighbouring_run() => Headless.Run(() =>
        {
            var field = Fixtures.Field(BoldRedRun(), 400);
            field.Editor.SetCaret(new TextPosition(0, 3));
            field.Editor.SetActiveWs("fr");
            var properties = PreeditProperties(field, "xyz");
            Assert.NotEqual(FontWeight.Bold, properties.Typeface.Weight);
        });
    }
}
