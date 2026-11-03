using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SIL.TextEditing.Avalonia.Tests
{
    static class Fixtures
    {
        /// <summary>A field laid out at the given width, not in a window.</summary>
        public static RichTextField Field(Document doc, double width = 520, Action<RichTextField>? configure = null)
        {
            var field = new RichTextField(doc);
            configure?.Invoke(field);
            Layout(field, width);
            return field;
        }

        public static void Layout(RichTextField field, double width)
        {
            field.Measure(new Size(width, 4000));
            field.Arrange(new Rect(0, 0, width, field.DesiredSize.Height));
        }

        /// <summary>A field shown in a headless window, so it has a TopLevel (clipboard, focus).</summary>
        public static (Window Window, RichTextField Field) InWindow(Document doc, double width = 520)
        {
            var field = new RichTextField(doc);
            var window = new Window { Width = width, Height = 400, Content = field };
            window.Show();
            Headless.Pump();
            return (window, field);
        }

        /// <summary>A 32 by 24 bitmap standing in for a picture object.</summary>
        public static IImage Picture(string data)
        {
            var wb = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = wb.Lock())
            {
                var px = new int[32 * 24];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = unchecked((int)0xFF4060D0);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return wb;
        }
    }
}
