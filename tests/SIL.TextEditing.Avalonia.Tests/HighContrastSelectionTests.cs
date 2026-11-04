using System;
using Avalonia;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>In a Windows high contrast theme selected text is drawn in the system's highlight text colour on its highlight colour, as the system's own
    /// controls draw it, not in the window text colour the rest of the text has.</summary>
    public class HighContrastSelectionTests
    {
        static readonly Color WindowText = Color.Parse("#FFFF00"), Window = Color.Parse("#000000"), Highlight = Color.Parse("#0000C0"), HighlightText = Color.Parse("#00FF00");

        static void InHighContrast(Action body)
        {
            var isOn = SystemColours.Probe;
            var get = SystemColours.Get;
            SystemColours.Probe = () => true;
            SystemColours.Get = kind => kind == SystemColour.WindowText ? WindowText : kind == SystemColour.Window ? Window : kind == SystemColour.Highlight ? Highlight : HighlightText;
            try { body(); }
            finally { SystemColours.Probe = isOn; SystemColours.Get = get; }
        }

        /// <summary>How many pixels of the shot are exactly this colour, in the rows of the field's first line.</summary>
        static int Count(Fixtures.Shot shot, Color colour, int from, int to)
        {
            int n = 0;
            for (int y = 0; y < 30; y++)
                for (int x = from; x < to; x++)
                    if (shot.Pixel(x, y) == colour) n++;
            return n;
        }

        static RichTextField Make(string text, Action<Paragraph>? configure = null)
        {
            var p = Para("en", "en", text);
            configure?.Invoke(p);
            var field = Fixtures.Field(Doc(p), 400);
            field.ShowCaretAlways = false;
            return field;
        }

        [Fact]
        public void Selected_text_is_drawn_in_the_highlight_text_colour_and_the_rest_in_the_window_text_colour() => Headless.Run(() => InHighContrast(() =>
        {
            var field = Make("HHHHHHHH HHHHHHHH");
            var pl = field.ParagraphLayoutFor(0);
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Editor.SetCaret(new TextPosition(0, 8), true);
            field.RefreshSystemColours();
            double split = pl.CaretRect(8).X;
            var shot = Fixtures.Render(field);
            int selectedTextPixels = Count(shot, HighlightText, 0, (int)split), selectedInWindowText = Count(shot, WindowText, 0, (int)split - 1);
            Assert.Equal(Highlight, shot.Pixel(0, 5));   // the selection fill is the highlight colour
            Assert.True(selectedTextPixels > 40, "pixels of the selected text in the highlight text colour: " + selectedTextPixels);
            Assert.Equal(0, selectedInWindowText);
            Assert.True(Count(shot, WindowText, (int)split + 10, 390) > 40, "the text that is not selected stays in the window text colour");
            Assert.Equal(0, Count(shot, HighlightText, (int)split + 10, 390));
        }));

        [Fact]
        public void Outside_high_contrast_selected_text_keeps_its_colour() => Headless.Run(() =>
        {
            var field = Make("HHHHHHHH");
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Editor.SetCaret(new TextPosition(0, 8), true);
            var shot = Fixtures.Render(field);
            Assert.Equal(0, Count(shot, HighlightText, 0, 390));
        });

        [Fact]
        public void The_underline_of_selected_text_is_drawn_in_the_highlight_text_colour_too() => Headless.Run(() => InHighContrast(() =>
        {
            var p = Para("en", "en", "HHHHHHHH");
            p.Runs[0].Props.SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineSingle);
            var field = Fixtures.Field(Doc(p), 400);
            var pl = field.ParagraphLayoutFor(0);
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Editor.SetCaret(new TextPosition(0, 8), true);
            field.RefreshSystemColours();
            var shot = Fixtures.Render(field);
            Assert.Equal(0, Count(shot, WindowText, 0, (int)pl.CaretRect(8).X - 1));
        }));

        [Fact]
        public void Selecting_text_in_high_contrast_does_not_lay_the_paragraphs_out_again() => Headless.Run(() => InHighContrast(() =>
        {
            var field = Make("HHHHHHHH HHHHHHHH");
            field.RefreshSystemColours();
            field.Editor.SetCaret(new TextPosition(0, 0));
            field.Editor.SetCaret(new TextPosition(0, 8), true);
            Fixtures.Render(field);
            long built = field.LayoutCount;
            field.Editor.SetCaret(new TextPosition(0, 5), true);
            Fixtures.Render(field);
            Fixtures.Render(field);
            Assert.Equal(built, field.LayoutCount);
        }));
    }
}
