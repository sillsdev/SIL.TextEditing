using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SIL.TextEditing.Playground
{
    /// <summary>Small generated bitmaps standing in for picture objects: the object data picks the colour.</summary>
    public sealed class SamplePictures
    {
        readonly Dictionary<string, IImage> _cache = new Dictionary<string, IImage>();

        public IImage? Get(string data)
        {
            if (_cache.TryGetValue(data, out var image)) return image;
            uint fill = data.Contains("red") ? 0xFFD04040u : data.Contains("blue") ? 0xFF4060D0u : 0xFF40A060u;
            image = Create(fill);
            _cache[data] = image;
            return image;
        }

        static IImage Create(uint fill)
        {
            var bitmap = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = bitmap.Lock())
            {
                var px = new int[32 * 24];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = (x < 2 || y < 2 || x > 29 || y > 21) ? unchecked((int)0xFF202020) : unchecked((int)fill);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return bitmap;
        }
    }
}
