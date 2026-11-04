using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A justified line draws each glyph once, in pieces between its spaces, and looks as it did when it was Avalonia's whole line drawn clipped to each
    /// word (issue 44).</summary>
    public class JustifiedDrawTests
    {
        const string English = "The quick brown fox jumps over the lazy dog while the five boxing wizards jump quickly and a mad boxer shot a quick, gloved jab to the jaw of his dizzy opponent.";
        const string Arabic = "هذه فقرة عربية طويلة بما يكفي لتلتف على عدة أسطر عندما تكون النافذة ضيقة وتبين أين تقع الهوامش والمسافات البادئة في الاتجاه من اليمين إلى اليسار وهي تنتهي هنا";

        sealed class Canvas : Control
        {
            public Action<DrawingContext>? Body;
            public override void Render(DrawingContext c) { c.DrawRectangle(Brushes.White, null, new Rect(Bounds.Size)); Body?.Invoke(c); }
        }

        static Fixtures.Shot Shot(double w, double h, Action<DrawingContext> body, double scale = 1)
        {
            var c = new Canvas { Width = w, Height = h, Body = body };
            c.Measure(new Size(w, h));
            c.Arrange(new Rect(0, 0, w, h));
            return new Fixtures.Shot(c, scale);
        }

        /// <summary>The way a justified line was drawn before: the whole of Avalonia's line, once per space, clipped to the word and moved to where it now is.</summary>
        static void WholeLineClipped(DrawingContext context, LineInfo li, double top)
        {
            const double Far = 100000;
            var j = li.Stretch!;
            int n = j.Spaces.Count;
            double previous = -Far;
            for (int k = 0; k <= n; k++)
            {
                double guard = k < n ? j.Guard(k) : 0;
                double end = k < n ? j.Spaces[k].Key + guard : Far;
                if (end > previous)
                    using (context.PushTransform(Matrix.CreateTranslation(j.RightAnchor ? -j.Extra * (n - k) : j.Extra * k, 0)))
                    using (context.PushClip(new Rect(li.X + previous, top + li.TextY - 50000, end - previous, 100000)))
                        li.Line.Draw(context, new Point(li.X, top + li.TextY));
                if (k == n) break;
                previous = end;
            }
        }

        [Theory]
        [InlineData(English, false, false)]
        [InlineData(English, false, true)]
        [InlineData(Arabic, true, false)]
        [InlineData("dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it dog فقرة fox طويلة jumps هذه ill (جمل) 12 it", false, true)]
        public void A_justified_line_looks_as_it_did_when_drawn_whole_once_per_space(string text, bool rtl, bool bold) => Headless.Run(() =>
        {
            foreach (double scale in new[] { 1.0, 1.5, 2.0 }) Compare(text, rtl, bold, scale);
        });

        static void Compare(string text, bool rtl, bool bold, double scale)
        {
            var p = Para(rtl ? "ar" : "en", rtl ? "ar" : "en", text.Substring(0, text.Length / 2), "en", text.Substring(text.Length / 2));
            if (bold) p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
            p.Rtl = rtl;
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var pl = Fixtures.Field(Doc(p), 260).ParagraphLayoutFor(0);
            int justified = 0;
            foreach (var li in pl.Lines)
            {
                if (!li.IsJustified) continue;
                justified++;
                var now = Shot(300, li.Height + 20, c => li.Draw(c, 5 - li.Y), scale);
                var was = Shot(300, li.Height + 20, c => WholeLineClipped(c, li, 5 - li.Y), scale);
                int ink = 0, differ = 0;
                for (int y = 0; y < now.Height; y++)
                    for (int x = 0; x < now.Width; x++)
                    {
                        bool a = now.IsInk(x, y), b = was.IsInk(x, y);
                        if (a || b) ink++;
                        if (a != b) differ++;
                    }
                Assert.True(ink > 100);
                Assert.True(differ * 200 <= ink, "scale " + scale + " line " + li.Start + ": " + differ + " of " + ink + " ink pixels differ");
            }
            Assert.True(justified > 3);
        }

        [Fact]
        public void A_justified_line_hands_each_glyph_to_the_renderer_once_not_once_per_space() => Headless.Run(() =>
        {
            var p = Para("en", "en", English + " " + English);
            p.Props.SetInt(TextPropertyNumbers.Align, 5);
            var pl = Fixtures.Field(Doc(p), 320).ParagraphLayoutFor(0);
            foreach (var li in pl.Lines)
            {
                if (!li.IsJustified) continue;
                int glyphs = 0;
                foreach (var tr in li.Line.TextRuns) if (tr is global::Avalonia.Media.TextFormatting.ShapedTextRun s) glyphs += s.GlyphRun.GlyphInfos.Count;
                long before = ParagraphLayout.GlyphsDrawn;
                Shot(400, li.Height + 20, c => li.Draw(c, 5 - li.Y));
                Assert.True(ParagraphLayout.GlyphsDrawn - before <= glyphs, "line " + li.Start + ": " + (ParagraphLayout.GlyphsDrawn - before) + " glyphs drawn for " + glyphs + " in the line and " + li.Stretch!.Spaces.Count + " spaces");
            }
        });
    }
}
