using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using SIL.TextEditing.Avalonia;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The paragraph drawing properties of the "Paragraph formatting" scenario, read back from the layouts the field holds and from
    /// its rendered pixels. Lengths in points are drawn as device-independent pixels, as font sizes are.</summary>
    public class DrawingParagraphTests
    {
        // Paragraph indexes in Scenarios.ParagraphsDoc.
        const int Plain = 1, Centred = 2, Right = 3, Trailing = 4, Justified = 5, FirstLine = 6, Hanging = 7, Quote = 8, OwnCentred = 9,
            Double = 10, Exact = 11, Boxed = 12, TwoLines = 13, Number1 = 14, Number3 = 16, Roman1 = 17, Letter1 = 19, Bullet1 = 21, Arabic = 23, ArabicCentred = 24,
            ArabicNumber1 = 25, ArabicNumber2 = 26, Flush = 27, ArabicLong = 28, ArabicBullet = 29;

        static double Left(LineInfo li) => li.X + li.Line.Start;

        [Fact]
        public void Alignment_leading_centre_right_trailing_and_justify_place_the_lines() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var plain = s.Field.ParagraphLayoutFor(Plain);
                Assert.True(plain.Lines.Count > 1);
                Assert.Equal(0, Left(plain.Lines[0]), 1);
                foreach (int index in new[] { Centred, OwnCentred })
                {
                    var c = s.Field.ParagraphLayoutFor(index);
                    var last = c.Lines[c.Lines.Count - 1];
                    double slack = c.ContentWidth - last.Line.Width;
                    Assert.True(slack > 20, "paragraph " + index);
                    Assert.Equal(c.ContentLeft + slack / 2, Left(last), 1);
                }
                foreach (int index in new[] { Right, Trailing })
                {
                    var r = s.Field.ParagraphLayoutFor(index);
                    var last = r.Lines[r.Lines.Count - 1];
                    Assert.Equal(r.ContentLeft + r.ContentWidth, Left(last) + last.Line.WidthIncludingTrailingWhitespace, 1);
                }
            }
        });

        [Fact]
        public void Justify_is_not_drawn_because_Avalonia_exposes_no_public_justifier_so_lines_stay_at_the_leading_edge() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var j = s.Field.ParagraphLayoutFor(Justified);
                Assert.Equal(5, j.Format.Align);
                Assert.True(j.Lines.Count > 2);
                foreach (var li in j.Lines) Assert.Equal(0, Left(li), 1);
                // When Avalonia makes InterWordJustification public (or the formatter justifies), lines but the last reach the edge: draw it then.
                Assert.True(j.Lines[0].Line.Width < j.ContentWidth - 1);
            }
        });

        [Fact]
        public void An_arabic_paragraph_leads_from_the_right_and_a_hanging_indent_hangs_on_the_right() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var a = s.Field.ParagraphLayoutFor(Arabic);
                Assert.True(a.Rtl);
                Assert.True(a.Lines.Count > 2);
                double right = a.LayoutWidth;
                // Leading indent 36 and hanging 36: the first line's right edge is 36 in, the others 72.
                // The visible text ends at the right edge; a full line's hanging space can push it a few pixels past.
                Assert.InRange(Left(a.Lines[0]) + a.Lines[0].Line.WidthIncludingTrailingWhitespace, right - 36 - 0.5, right - 36 + 6);
                Assert.InRange(Left(a.Lines[1]) + a.Lines[1].Line.WidthIncludingTrailingWhitespace, right - 72 - 0.5, right - 72 + 6);
                var c = s.Field.ParagraphLayoutFor(ArabicCentred);
                Assert.Equal(c.LayoutWidth / 2, Left(c.Lines[0]) + c.Lines[0].Line.Width / 2, 1);
            }
        });

        [Fact]
        public void First_line_and_hanging_indents_and_the_two_margins_move_the_text_area() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var f = s.Field.ParagraphLayoutFor(FirstLine);
                Assert.Equal(24, f.Lines[0].X - f.Lines[1].X, 3);
                Assert.True(f.Lines[0].Line.Width <= f.ContentWidth - 24 + 0.5);
                var h = s.Field.ParagraphLayoutFor(Hanging);
                Assert.Equal(36, h.Lines[0].X, 3);
                Assert.Equal(72, h.Lines[1].X, 3);
                var q = s.Field.ParagraphLayoutFor(Quote);
                Assert.Equal(36, q.ContentLeft, 3);
                Assert.Equal(q.LayoutWidth - 72, q.ContentWidth, 3);
                foreach (var li in q.Lines) Assert.True(li.Line.Width <= q.ContentWidth + 0.5);
            }
        });

        [Fact]
        public void Space_before_and_after_add_to_the_paragraph_height_and_move_the_next_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var heading = s.Field.ParagraphLayoutFor(0);
                Assert.Equal(12, heading.TopInset, 3);
                Assert.Equal(6, heading.BottomInset, 3);
                Assert.Equal(heading.TopInset, heading.Lines[0].Y, 3);
                Assert.Equal(heading.TopInset + heading.Lines.Sum(l => l.Height) + heading.BottomInset, heading.Height, 3);
                Assert.Equal(heading.Height, s.Field.ParagraphTop(1), 3);
            }
        });

        [Fact]
        public void Line_height_is_at_least_the_text_for_relative_and_exact_for_a_negative_value() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var d = s.Field.ParagraphLayoutFor(Double);
                Assert.True(d.Lines.Count > 1);
                // Relative 200 per cent of the 1.4 line: 2 x 1.4 x 16 points.
                foreach (var li in d.Lines) Assert.Equal(44.8, li.Height, 3);
                var e = s.Field.ParagraphLayoutFor(Exact);
                foreach (var li in e.Lines) Assert.Equal(30, li.Height, 3);
                foreach (var li in e.Lines) Assert.Equal(li.Y + 30 - li.Line.Height, li.TextY, 3);
                // Hit testing and the caret follow the slot: a point low in the first slot is still the first line.
                var hit = e.HitTestDetailed(new Point(10, e.Lines[0].Y + 29));
                Assert.True(hit.Offset < e.Lines[1].Start);
                Assert.Equal(e.Lines[0].TextY, e.CaretRect(0).Y, 3);
            }
        });

        [Fact]
        public void A_line_limit_drops_the_lines_beyond_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                var t = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.Equal(2, t.Lines.Count);
                Assert.True(t.Truncated);
                Assert.False(s.Field.ParagraphLayoutFor(Plain).Truncated);
            }
        });

        // Text a line limit hides cannot be reached: the caret stops at the end of the last line shown, as FieldWorks puts an
        // insertion point that would land in truncated text at the last visible position. The text stays in the model.

        [Fact]
        public void A_caret_set_into_text_a_line_limit_hides_is_drawn_at_the_end_of_the_last_visible_line() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.True(pl.Truncated);
                int visible = pl.VisibleEnd, length = pl.Paragraph.Length;
                Assert.InRange(visible, pl.Lines[1].Start + 1, length - 20);
                Assert.Equal(pl.Lines[1].End, visible);
                foreach (int hidden in new[] { visible + 1, length - 1, length })
                {
                    s.Caret(TwoLines, hidden);
                    Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                    var r = s.Field.CaretRect();
                    var end = pl.CaretRect(visible);
                    Assert.Equal(end.X, r.X, 3);
                    Assert.Equal(s.Field.ParagraphTop(TwoLines) + pl.Lines[1].TextY, r.Y, 3);
                }
                Assert.Equal(length, s.Editor.Document.Paragraphs[TwoLines].Length);
            }
        });

        [Fact]
        public void Right_and_End_stop_at_the_end_of_the_visible_text_and_Right_there_goes_to_the_next_paragraph() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                int visible = pl.VisibleEnd;
                s.Caret(TwoLines, visible - 3);
                s.Press(global::Avalonia.Input.Key.Right, times: 3);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Right);
                Assert.Equal(new TextPosition(TwoLines + 1, 0), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Left);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Caret(TwoLines, 5);
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.End);
                Assert.Equal(new TextPosition(TwoLines, visible), s.Editor.Caret);
                s.Press(global::Avalonia.Input.Key.Right, global::Avalonia.Input.KeyModifiers.Control);
                Assert.Equal(new TextPosition(TwoLines + 1, 0), s.Editor.Caret);
            }
        });

        [Fact]
        public void A_selection_into_hidden_text_ends_at_the_visible_end_and_draws_only_the_visible_lines() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                s.Select(TwoLines, 0, pl.Paragraph.Length);
                Assert.Equal(new TextPosition(TwoLines, 0), s.Editor.Anchor);
                Assert.Equal(new TextPosition(TwoLines, pl.VisibleEnd), s.Editor.Caret);
                var rects = pl.SelectionRects(0, pl.VisibleEnd);
                Assert.Equal(2, rects.Count);
                Assert.True(rects.All(r => r.Bottom <= pl.Height - pl.BottomInset + 0.01));
                Assert.Empty(pl.SelectionRects(pl.VisibleEnd, pl.Paragraph.Length));
                // Shift+Down from the last visible line extends into the next paragraph, not into the hidden text.
                s.Caret(TwoLines, 5);
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.Down, global::Avalonia.Input.KeyModifiers.Shift);
                Assert.Equal(TwoLines + 1, s.Editor.Caret.Paragraph);
            }
        });

        [Fact]
        public void Up_and_Down_move_between_the_visible_lines_and_the_paragraphs_around_a_line_limited_one() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 700, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(TwoLines);
                s.Caret(TwoLines + 1, 3);
                s.Press(global::Avalonia.Input.Key.Up);
                Assert.Equal(TwoLines, s.Editor.Caret.Paragraph);
                Assert.Equal(1, pl.LineIndexOf(s.Editor.Caret.Offset));
                Assert.True(s.Editor.Caret.Offset <= pl.VisibleEnd);
                s.Press(global::Avalonia.Input.Key.Up);
                Assert.Equal(0, pl.LineIndexOf(s.Editor.Caret.Offset));
                s.Press(global::Avalonia.Input.Key.Down);
                s.Press(global::Avalonia.Input.Key.Down);
                Assert.Equal(TwoLines + 1, s.Editor.Caret.Paragraph);
                // Typing at the end of the visible text never leaves the caret in the hidden part.
                s.Caret(TwoLines, pl.VisibleEnd);
                s.Type("xyz");
                var after = s.Field.ParagraphLayoutFor(TwoLines);
                Assert.True(s.Editor.Caret.Offset <= after.VisibleEnd);
                Assert.Equal(TwoLines, s.Editor.Caret.Paragraph);
            }
        });

        /// <summary>The caret edge of an offset (not the middle of a character), in window coordinates.</summary>
        static Point Edge(Session s, int paragraph, int offset)
        {
            var r = s.Field.ParagraphLayoutFor(paragraph).CaretRect(offset);
            return s.Field.TranslatePoint(new Point(r.X, s.Field.ParagraphTop(paragraph) + r.Y + r.Height / 2), TopLevel.GetTopLevel(s.Field)!)!.Value;
        }

        /// <summary>The field's rendered pixels, read once.</summary>
        internal sealed class Shot
        {
            readonly byte[] _buffer;
            readonly int _stride;
            public Shot(Session s)
            {
                var field = s.Field;
                var size = new PixelSize((int)Math.Ceiling(field.Bounds.Width), (int)Math.Ceiling(field.Bounds.Height));
                Width = size.Width;
                Height = size.Height;
                using (var bitmap = new RenderTargetBitmap(size))
                {
                    bitmap.Render(field);
                    _stride = size.Width * 4;
                    _buffer = new byte[_stride * size.Height];
                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(_buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), _buffer.Length, _stride); }
                    finally { handle.Free(); }
                }
            }

            public int Width { get; }
            public int Height { get; }

            public Color At(double x, double y)
            {
                int o = (int)y * _stride + (int)x * 4;
                return Color.FromRgb(_buffer[o + 2], _buffer[o + 1], _buffer[o]);
            }

            /// <summary>True when the pixel is dark: text ink on a white or pale page.</summary>
            public bool IsInk(double x, double y, int threshold = 140) { var c = At(x, y); return c.R < threshold && c.G < threshold && c.B < threshold; }

            /// <summary>The lowest row with ink inside a box (its inner columns), or -1.</summary>
            public int InkBottom(Rect box)
            {
                for (int y = Math.Min(Height - 1, (int)Math.Ceiling(box.Bottom) + 4); y >= (int)box.Y; y--)
                    for (int x = (int)Math.Floor(box.X) + 1; x < (int)Math.Ceiling(box.Right) - 1; x++)
                        if (IsInk(x, y)) return y;
                return -1;
            }

            /// <summary>The columns in [from, to) that have dark ink anywhere in the rows [top, bottom).</summary>
            public List<int> InkColumns(double from, double to, double top, double bottom)
            {
                var result = new List<int>();
                for (int x = Math.Max(0, (int)Math.Floor(from)); x < Math.Min(Width, (int)Math.Ceiling(to)); x++)
                    for (int y = Math.Max(0, (int)Math.Floor(top)); y < Math.Min(Height, (int)Math.Ceiling(bottom)); y++)
                        if (IsInk(x, y)) { result.Add(x); break; }
                return result;
            }
        }

        internal static Color Pixel(Session s, double x, double y) => new Shot(s).At(x, y);

        [Fact]
        public void A_paragraph_colour_fills_the_box_and_borders_are_drawn_in_the_border_colour() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(Boxed);
                double top = s.Field.ParagraphTop(Boxed);
                Assert.Equal(1 + 4, pl.TopInset, 3);
                Assert.Equal(12 + 3 + 6, pl.ContentLeft, 3);
                double mid = top + pl.Height / 2;
                // Left margin 12, then a 3 point blue border, 6 points of padding.
                Assert.Equal(Color.FromRgb(0, 0, 255), Pixel(s, 13, mid));
                Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), Pixel(s, 12 + 3 + 2, mid));
                Assert.Equal(Color.FromRgb(255, 255, 255), Pixel(s, 6, mid));
                var topEdge = Pixel(s, 200, top + 0.5);   // the one point border lies across two pixel rows
                Assert.True(topEdge.B > 200 && topEdge.R < 128, "top border " + topEdge);
                Assert.Equal(Color.FromRgb(0xEE, 0xEE, 0xEE), Pixel(s, 200, top + 3));
            }
        });

        [Fact]
        public void Bullets_and_numbers_count_runs_of_paragraphs_and_use_the_before_and_after_text() => Headless.Run(() =>
        {
            Assert.Equal(4, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = BulletNumbers.Arabic }, 3));
            Assert.Equal(2, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = i == 2 ? 0 : BulletNumbers.Arabic }, 4));
            Assert.Equal(8, BulletNumbers.Sequence(i => new ParaFormat { BulNumScheme = BulletNumbers.Arabic, BulNumStartAt = i >= 2 ? 7 : (int?)null }, 3));
            Assert.Equal("(iv)", BulletNumbers.Label(new ParaFormat { BulNumScheme = BulletNumbers.RomanLower, BulNumTextBefore = "(", BulNumTextAfter = ")" }, 4));
            Assert.Equal("MCMXCIV", BulletNumbers.Number(BulletNumbers.RomanUpper, 1994));
            Assert.Equal("AA", BulletNumbers.Number(BulletNumbers.LetterUpper, 27));
            Assert.Equal("z", BulletNumbers.Number(BulletNumbers.LetterLower, 26));
            Assert.Equal("07", BulletNumbers.Number(BulletNumbers.Arabic01, 7));
            Assert.Equal("•", BulletNumbers.Label(new ParaFormat { BulNumScheme = 101 }, 0));
            Assert.Equal("", BulletNumbers.Label(new ParaFormat(), 1));

            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var pl = s.Field.ParagraphLayoutFor(Number3);
                Assert.Equal(BulletNumbers.Arabic, pl.Format.BulNumScheme);
                Assert.Equal(3, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Number3));
                Assert.Equal(1, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Roman1));
                Assert.Equal(1, BulletNumbers.Sequence(i => s.Field.ParagraphLayoutFor(i).Format, Letter1));
                Assert.True(BulletNumbers.IsBullet(s.Field.ParagraphLayoutFor(Bullet1).Format.BulNumScheme));
                Assert.Equal("3.", BulletNumbers.Label(pl.Format, 3));
                // List Number: leading 36, hanging 24: the label sits at 36, in the hanging space; every line of text starts at 60.
                Assert.Equal(60, pl.ContentLeft, 3);
                Assert.Equal(60, pl.Lines[0].X, 3);
                Assert.Equal(36, pl.LabelRect.X, 3);
            }
        });

        static double TextLeft(ParagraphLayout pl) => Left(pl.Lines[0]);
        static double TextRight(ParagraphLayout pl) => Left(pl.Lines[0]) + pl.Lines[0].Line.Width;

        [Fact]
        public void The_label_of_a_list_paragraph_ends_before_its_first_line_of_text_starts_in_left_to_right_and_right_to_left_lists() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                foreach (int p in new[] { Number1, Number1 + 1, Number3, Roman1, Letter1, Bullet1, Bullet1 + 1, Flush })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    var label = pl.LabelRect;
                    Assert.False(pl.Rtl);
                    Assert.True(label.Right + 3 <= TextLeft(pl) + 0.01, "paragraph " + p + ": label " + label + " text from " + TextLeft(pl));
                    Assert.True(label.X >= 0);
                }
                foreach (int p in new[] { ArabicNumber1, ArabicNumber2, ArabicBullet })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    var label = pl.LabelRect;
                    Assert.True(pl.Rtl);
                    Assert.True(label.X >= TextRight(pl) + 3 - 0.01, "paragraph " + p + ": label " + label + " text to " + TextRight(pl));
                    Assert.True(label.Right <= pl.LayoutWidth + 0.01);
                }
                // With room in the hanging indent the first line lines up with the others; with none, the text starts after the label.
                var hang = s.Field.ParagraphLayoutFor(Number1);
                Assert.Equal(hang.ContentLeft, TextLeft(hang), 1);
                Assert.Equal(hang.ContentLeft, Left(hang.Lines[1]), 1);
                var flush = s.Field.ParagraphLayoutFor(Flush);
                Assert.Equal(36, flush.LabelRect.X, 3);
                Assert.Equal(flush.LabelRect.Right + 3, TextLeft(flush), 1);
                Assert.Equal(36, Left(flush.Lines[1]), 1);
                var arabic = s.Field.ParagraphLayoutFor(ArabicNumber1);
                Assert.Equal(arabic.LayoutWidth - 36, arabic.LabelRect.Right, 3);
            }
        });

        [Fact]
        public void Pixels_show_the_label_clear_of_the_first_letter_in_left_to_right_and_right_to_left_lists() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 4500))
            {
                var shot = new Shot(s);
                Assert.True(s.Field.ParagraphTop(ArabicBullet) < shot.Height, "the arabic list is inside the rendered area");
                foreach (int p in new[] { Number3, Roman1, Letter1, Bullet1, Flush })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    double top = s.Field.ParagraphTop(p) + pl.Lines[0].TextY, bottom = top + pl.Lines[0].Line.Height;
                    var label = pl.LabelRect;
                    var labelInk = shot.InkColumns(label.X - 1, label.Right + 1, top, bottom);
                    var textInk = shot.InkColumns(TextLeft(pl) - 1, TextLeft(pl) + 60, top, bottom);
                    Assert.NotEmpty(labelInk);
                    Assert.NotEmpty(textInk);
                    Assert.True(labelInk.Max() < textInk.Min(), "paragraph " + p + ": label ink to " + labelInk.Max() + ", text ink from " + textInk.Min());
                    // The columns between the label's ink and the text's ink are empty, and nothing is left of the label.
                    Assert.Empty(shot.InkColumns(labelInk.Max() + 1, textInk.Min(), top, bottom));
                    Assert.Empty(shot.InkColumns(0, label.X - 1, top, bottom));
                }
                foreach (int p in new[] { ArabicNumber1, ArabicBullet })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    double top = s.Field.ParagraphTop(p) + pl.Lines[0].TextY, bottom = top + pl.Lines[0].Line.Height;
                    var label = pl.LabelRect;
                    var labelInk = shot.InkColumns(label.X - 1, label.Right + 1, top, bottom);
                    var textInk = shot.InkColumns(TextRight(pl) - 60, TextRight(pl) + 8, top, bottom);
                    Assert.NotEmpty(labelInk);
                    Assert.NotEmpty(textInk);
                    Assert.True(labelInk.Min() > textInk.Max(), "paragraph " + p + ": label ink from " + labelInk.Min() + ", text ink to " + textInk.Max());
                    Assert.Empty(shot.InkColumns(textInk.Max() + 1, labelInk.Min(), top, bottom));
                }
            }
        });

        [Fact]
        public void The_label_of_every_paragraph_is_its_place_in_the_run_and_Enter_in_a_list_renumbers_the_paragraphs_below() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                var count = s.Editor.Document.Paragraphs.Count;
                string[] before = new string[count];
                for (int i = 0; i < count; i++)
                {
                    var pl = s.Field.ParagraphLayoutFor(i);
                    int number = BulletNumbers.IsNumber(pl.Format.BulNumScheme) ? BulletNumbers.Sequence(k => s.Field.ParagraphLayoutFor(k).Format, i) : 0;
                    Assert.Equal(BulletNumbers.Label(pl.Format, number), pl.LabelText);
                    before[i] = pl.LabelText;
                }
                Assert.Equal(new[] { "1.", "2.", "3." }, new[] { before[Number1], before[Number1 + 1], before[Number3] });
                s.Caret(Number1, s.Editor.Document.Paragraphs[Number1].Length);
                s.Press(global::Avalonia.Input.Key.Enter);
                Assert.Equal(count + 1, s.Editor.Document.Paragraphs.Count);
                Assert.Equal(new[] { "1.", "2.", "3.", "4." }, new[] { s.Field.ParagraphLayoutFor(Number1).LabelText, s.Field.ParagraphLayoutFor(Number1 + 1).LabelText, s.Field.ParagraphLayoutFor(Number1 + 2).LabelText, s.Field.ParagraphLayoutFor(Number1 + 3).LabelText });
                Assert.Equal("(i)", s.Field.ParagraphLayoutFor(Roman1 + 1).LabelText);
            }
        });

        [Fact]
        public void The_paragraph_style_box_restyles_the_caret_paragraph_and_the_layout_follows() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs))
            {
                s.Caret(Plain, 3);
                var before = s.Field.ParagraphLayoutFor(Plain);
                int beforeLines = before.Lines.Count;
                Assert.Equal(0, before.Lines[0].X, 3);
                s.Find<ComboBox>(PlaygroundView.Names.ParaStyle).SelectedItem = "Block Quote";
                Headless.Pump();
                var after = s.Field.ParagraphLayoutFor(Plain);
                Assert.Equal(36, after.Lines[0].X, 3);
                Assert.True(after.Lines.Count >= beforeLines);
                s.MouseClick(Edge(s, Plain, 3));
                Assert.Equal(3, s.Editor.Caret.Offset);
            }
        });

        [Fact]
        public void Mouse_hits_and_vertical_movement_agree_with_indents_and_line_slots() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Paragraphs, 1100, 3000))
            {
                foreach (int p in new[] { FirstLine, Hanging, Double, Exact, Centred, Right })
                {
                    var pl = s.Field.ParagraphLayoutFor(p);
                    foreach (int offset in new[] { 5, pl.Lines[1].Start + 3, pl.Paragraph.Length - 2 })
                    {
                        s.MouseClick(Edge(s, p, offset));
                        Assert.Equal(new TextPosition(p, offset), s.Editor.Caret);
                    }
                    s.Caret(p, pl.Lines[0].Start + 4);
                    s.Press(global::Avalonia.Input.Key.Down);
                    Assert.Equal(p, s.Editor.Caret.Paragraph);
                    Assert.Equal(1, pl.LineIndexOf(s.Editor.Caret.Offset));
                    s.Press(global::Avalonia.Input.Key.Up);
                    Assert.Equal(0, pl.LineIndexOf(s.Editor.Caret.Offset));
                }
            }
        });
    }
}
