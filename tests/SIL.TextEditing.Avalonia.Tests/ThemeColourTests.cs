using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>The colours a field takes from its theme when the host has not set them: the text and border colour and the page colour. A
    /// theme change (dark and light, high contrast) must redraw them, not only the caret and selection.</summary>
    public class ThemeColourTests
    {
        static (Window Window, RichTextField Field) Themed()
        {
            var doc = OneParagraph("HHHHHHHH");
            doc.Paragraphs[0].Props.SetInt(TextPropertyNumbers.BorderTop, 4000, 1);
            var field = new RichTextField(doc);
            field.Resources.ThemeDictionaries[ThemeVariant.Light] = new ResourceDictionary
            {
                ["TextControlForeground"] = new SolidColorBrush(Color.Parse("#FF0000")),
                ["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#FFFFE0")),
            };
            field.Resources.ThemeDictionaries[ThemeVariant.Dark] = new ResourceDictionary
            {
                ["TextControlForeground"] = new SolidColorBrush(Color.Parse("#0000FF")),
                ["SystemControlBackgroundAltHighBrush"] = new SolidColorBrush(Color.Parse("#202020")),
            };
            var window = new Window { Width = 300, Height = 400, Content = field, RequestedThemeVariant = ThemeVariant.Light };
            window.Show();
            Headless.Pump();
            return (window, field);
        }

        /// <summary>The most saturated pixel of the first rows of the field: the text and the border have no other colour here.</summary>
        static Color Ink(Fixtures.Shot shot, int bottom)
        {
            Color best = Colors.Transparent;
            int bestScore = -1;
            for (int y = 0; y < bottom; y++)
                for (int x = 0; x < shot.Width; x++)
                {
                    var c = shot.Pixel(x, y);
                    int score = System.Math.Abs(c.R - c.B) + System.Math.Abs(c.R - c.G) + System.Math.Abs(c.B - c.G) + (255 - (c.R + c.G + c.B) / 3) / 4;
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            return best;
        }

        [Fact]
        public void A_theme_change_redraws_the_border_and_text_in_the_new_foreground_and_the_page_in_the_new_background() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            var light = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#FFFFE0"), light.Pixel(290, 40));
            var border = light.Pixel(100, 1);
            Assert.True(border.R > 200 && border.B < 60, "the border has no colour of its own: it takes the foreground, " + border);
            var text = Ink(light, 30);
            Assert.True(text.R > 200 && text.B < 60, "light text " + text);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            var dark = Fixtures.Render(field);
            Assert.Equal(Color.Parse("#202020"), dark.Pixel(290, 40));
            border = dark.Pixel(100, 1);
            Assert.True(border.B > 200 && border.R < 60, "dark border " + border);
            text = Ink(dark, 30);
            Assert.True(text.B > 200 && text.R < 60, "dark text " + text);
            window.Close();
        });

        [Fact]
        public void A_foreground_or_background_the_host_set_is_not_changed_by_the_theme() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            field.Background = Brushes.White;
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Headless.Pump();
            var shot = Fixtures.Render(field);
            Assert.Equal(Colors.White, shot.Pixel(290, 40));
            var border = shot.Pixel(100, 1);
            Assert.True(border.G > 100 && border.R < 60 && border.B < 60, "the border stays green: " + border);
            window.Close();
        });

        [Fact]
        public void Setting_the_foreground_redraws_the_text_at_once() => Headless.Run(() =>
        {
            var (window, field) = Themed();
            field.Foreground = Brushes.Green;
            Headless.Pump();
            var border = Fixtures.Render(field).Pixel(100, 1);
            Assert.True(border.G > 100 && border.R < 60, "green border " + border);
            window.Close();
        });
    }
}
