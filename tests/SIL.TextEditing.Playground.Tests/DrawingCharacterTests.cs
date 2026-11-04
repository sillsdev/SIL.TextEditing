using System.Collections.Generic;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>The character drawing properties of the "Drawing properties" scenario, read back from the text runs the field's layout
    /// actually holds (the font, size, brushes and decorations Avalonia was given).</summary>
    public class DrawingCharacterTests
    {
        internal static TextRunProperties RunWith(Session s, int paragraph, string token)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            foreach (var li in pl.Lines)
                foreach (var run in li.Line.TextRuns)
                    if (run.Text.Span.ToString().Contains(token)) return run.Properties!;
            throw new System.InvalidOperationException("No text run containing \"" + token + "\" in paragraph " + paragraph);
        }

        static Color ColorOf(IBrush? brush) => ((ISolidColorBrush)brush!).Color;

        [Fact]
        public void Bold_weights_italic_and_style_driven_weights_reach_the_typeface() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "bold").Typeface.Weight);
                Assert.Equal(FontWeight.Light, RunWith(s, 1, "weight 300").Typeface.Weight);
                Assert.Equal(FontWeight.Black, RunWith(s, 1, "weight 900").Typeface.Weight);
                Assert.Equal(FontStyle.Italic, RunWith(s, 1, "italic").Typeface.Style);
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "bold italic").Typeface.Weight);
                Assert.Equal(FontWeight.Bold, RunWith(s, 1, "Strong (a style)").Typeface.Weight);
                Assert.Equal(FontWeight.Normal, RunWith(s, 1, "Weights: normal").Typeface.Weight);
            }
        });

        [Fact]
        public void The_host_style_resolver_is_asked_per_writing_system_and_its_answer_is_drawn() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                double english = RunWith(s, 1, "Strong (a style)").FontRenderingEmSize;
                double arabic = RunWith(s, 1, "قوي").FontRenderingEmSize;
                Assert.Equal(16, english);
                Assert.Equal(18 * 1.1, arabic, 3);
                Assert.Equal(22, RunWith(s, 0, "Drawing").FontRenderingEmSize);
                Assert.Equal(FontWeight.Bold, RunWith(s, 0, "Drawing").Typeface.Weight);

                var before = s.Field.LayoutCount;
                s.Field.StyleResolver = null;
                s.Field.ParagraphLayoutFor(0);
                Assert.True(s.Field.LayoutCount > before);
                Assert.Equal(16, RunWith(s, 0, "Drawing").FontRenderingEmSize);
                Assert.Equal(FontWeight.Normal, RunWith(s, 0, "Drawing").Typeface.Weight);
            }
        });

        [Fact]
        public void Font_sizes_absolute_relative_and_named_use_the_current_size() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(24, RunWith(s, 2, "24 point").FontRenderingEmSize);
                Assert.Equal(8, RunWith(s, 2, "half size").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "named large").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "named smaller").FontRenderingEmSize);
                Assert.Equal(14, RunWith(s, 2, "Large (a style)").FontRenderingEmSize);
            }
        });

        [Fact]
        public void Font_families_resolve_the_default_font_markup_and_feature_lists_add_to_the_writing_systems() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal("Times New Roman", RunWith(s, 2, "<default serif>").Typeface.FontFamily.Name);
                Assert.Equal("Arial", RunWith(s, 2, "<default sans serif>").Typeface.FontFamily.Name);
                Assert.Equal("Courier New", RunWith(s, 2, "<default monospace>").Typeface.FontFamily.Name);
                Assert.Equal("Comic Sans MS", RunWith(s, 2, "Comic Sans MS").Typeface.FontFamily.Name);
                Assert.Null(RunWith(s, 6, "(default)").FontFeatures);
                Assert.Contains(RunWith(s, 6, "(tnum)").FontFeatures!, f => f.Tag == "tnum");
            }
        });

        [Fact]
        public void Fore_and_back_colours_are_BGR_and_a_special_value_means_the_default() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(RunWith(s, 3, "red text").ForegroundBrush));
                var green = RunWith(s, 3, "green on pale yellow");
                Assert.Equal(Color.FromRgb(0, 128, 0), ColorOf(green.ForegroundBrush));
                Assert.Same(s.Field.Foreground, RunWith(s, 3, "transparent colour").ForegroundBrush);
                var warning = RunWith(s, 3, "Warning (a style)");
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(warning.ForegroundBrush));
            }
        });

        /// <summary>The rectangle of the first occurrence of <paramref name="token"/> in a paragraph, in the field's coordinates (one line of it).</summary>
        internal static Rect BoxOf(Session s, int paragraph, string token, int skip = 0, int length = -1)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            int at = s.ParagraphText(paragraph).IndexOf(token, System.StringComparison.Ordinal) + skip;
            Assert.True(at >= skip, "no " + token);
            var li = pl.Lines[pl.LineIndexOf(at)];
            int n = length >= 0 ? length : token.Length - skip;
            var bounds = li.Line.GetTextBounds(at, System.Math.Min(n, li.End - at));
            double x0 = bounds.Min(b => b.Rectangle.X), x1 = bounds.Max(b => b.Rectangle.Right);
            double top = s.Field.ParagraphTop(paragraph) + li.TextY;
            return new Rect(li.X + x0, top, x1 - x0, li.Line.Height);
        }

        static readonly Color PaleYellowRgb = Color.FromRgb(255, 255, 153);

        [Fact]
        public void A_run_background_is_painted_and_a_selected_run_stays_visibly_selected_over_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var box = BoxOf(s, 3, "green on pale yellow");
                var other = BoxOf(s, 3, "transparent colour");
                var shot = new DrawingParagraphTests.Shot(s);
                Assert.Equal(PaleYellowRgb, shot.At(box.X + 1, box.Y + 1));
                Assert.Equal(PaleYellowRgb, shot.At(box.Right - 2, box.Y + 1));
                Assert.Equal(Color.FromRgb(255, 255, 255), shot.At(other.X + 1, other.Y + 1));
                int at = s.ParagraphText(3).IndexOf("green on pale yellow", System.StringComparison.Ordinal);
                s.Select(3, at, at + "green on pale yellow".Length);
                var selected = new DrawingParagraphTests.Shot(s);
                var pixel = selected.At(box.X + 1, box.Y + 1);
                Assert.NotEqual(PaleYellowRgb, pixel);
                Assert.NotEqual(Color.FromRgb(255, 255, 255), pixel);
                Assert.NotEqual(PaleYellowRgb, selected.At(box.Right - 2, box.Y + 1));
                // The unselected neighbour keeps its look.
                Assert.Equal(Color.FromRgb(255, 255, 255), selected.At(other.X + 1, other.Y + 1));
            }
        });

        [Fact]
        public void A_run_background_from_a_style_is_painted_too() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var box = BoxOf(s, 3, "Warning (a style)");
                Assert.Equal(PaleYellowRgb, new DrawingParagraphTests.Shot(s).At(box.X + 1, box.Y + 1));
            }
        });

        [Fact]
        public void Pixels_put_a_superscript_above_and_a_subscript_below_the_baseline_of_the_text_beside_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                int bottomH = shot.InkBottom(BoxOf(s, 5, "H2O", 0, 1));
                int bottomSub = shot.InkBottom(BoxOf(s, 5, "H2O", 1, 1));
                int bottomC = shot.InkBottom(BoxOf(s, 5, "mc2", 1, 1));
                int bottomSup = shot.InkBottom(BoxOf(s, 5, "mc2", 2, 1));
                Assert.True(bottomH >= 0 && bottomSub >= 0 && bottomC >= 0 && bottomSup >= 0);
                Assert.True(bottomSup <= bottomC - 3, "superscript ink ends at " + bottomSup + ", the text beside it at " + bottomC);
                Assert.True(bottomSub >= bottomH + 2, "subscript ink ends at " + bottomSub + ", the text beside it at " + bottomH);
            }
        });

        /// <summary>The ink runs (start, length) along the row below the letters of a run, where its underline is.</summary>
        static List<int> UnderlineSegments(DrawingParagraphTests.Shot shot, Rect box)
        {
            int bestRow = -1, best = 0;
            for (int y = (int)(box.Y + box.Height * 0.75); y < box.Bottom + 3; y++)
            {
                int n = 0;
                for (int x = (int)box.X + 1; x < box.Right - 1; x++) if (shot.IsInk(x, y, 220)) n++;
                if (n > best) { best = n; bestRow = y; }
            }
            var runs = new List<int>();
            if (bestRow < 0) return runs;
            int current = 0;
            for (int x = (int)box.X + 1; x < box.Right - 1; x++)
            {
                if (shot.IsInk(x, bestRow, 220)) current++;
                else if (current > 0) { runs.Add(current); current = 0; }
            }
            if (current > 0) runs.Add(current);
            return runs;
        }

        [Fact]
        public void Pixels_draw_a_dashed_underline_in_dashes_a_dotted_one_in_dots_and_a_double_one_unbroken() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                var dashed = UnderlineSegments(shot, BoxOf(s, 4, "dashed"));
                var dotted = UnderlineSegments(shot, BoxOf(s, 4, "dotted"));
                var dbl = UnderlineSegments(shot, BoxOf(s, 4, "double"));
                Assert.True(dashed.Count >= 3, "dashed: " + string.Join(",", dashed));
                Assert.Contains(dashed, n => n >= 5);
                Assert.True(dotted.Count >= dashed.Count, "dotted: " + string.Join(",", dotted));
                Assert.All(dotted, n => Assert.True(n <= 3));
                Assert.Single(dbl);
            }
        });

        [Fact]
        public void Underline_types_draw_dotted_dashed_single_double_and_strikethrough_in_the_under_colour() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var dotted = RunWith(s, 4, "dotted").TextDecorations!.Single();
                Assert.Equal(TextDecorationLocation.Underline, dotted.Location);
                Assert.NotNull(dotted.StrokeDashArray);
                Assert.Equal(new double[] { 2, 2 }, dotted.StrokeDashArray!.ToArray());
                Assert.Equal(new double[] { 6, 3 }, RunWith(s, 4, "dashed").TextDecorations!.Single().StrokeDashArray!.ToArray());
                var single = RunWith(s, 4, "single").TextDecorations!.Single();
                Assert.Equal(TextDecorationLocation.Underline, single.Location);
                Assert.Null(single.StrokeDashArray);
                var dbl = RunWith(s, 4, "double").TextDecorations!;
                Assert.Equal(2, dbl.Count);
                Assert.NotEqual(dbl[0].StrokeOffset, dbl[1].StrokeOffset);
                Assert.Equal(TextDecorationLocation.Strikethrough, RunWith(s, 4, "strikethrough").TextDecorations!.Single().Location);
                Assert.Equal(Color.FromRgb(0, 0, 255), ColorOf(RunWith(s, 4, "blue single").TextDecorations!.Single().Stroke));
                var styled = RunWith(s, 4, "Misspelled").TextDecorations!;
                Assert.Equal(2, styled.Count);
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(styled[0].Stroke));
                Assert.Null(RunWith(s, 4, "Lines: ").TextDecorations);
            }
        });

        [Fact]
        public void Superscript_and_subscript_shrink_to_two_thirds_and_shift_the_baseline() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var sup = RunWith(s, 5, "super");
                var sub = RunWith(s, 5, "sub");
                Assert.Equal(16 * 2.0 / 3.0, sup.FontRenderingEmSize, 3);
                Assert.Equal(BaselineAlignment.Superscript, sup.BaselineAlignment);
                Assert.Equal(BaselineAlignment.Subscript, sub.BaselineAlignment);
                Assert.Equal(BaselineAlignment.Baseline, RunWith(s, 5, "Position: H").BaselineAlignment);
            }
        });

        // The three properties below are carried and kept through every edit but not drawn yet (ADR 0005 exceptions, tracked as issues;
        // justification is pinned in DrawingParagraphTests). When one starts being drawn these fail: replace the test with one for the feature.

        /// <summary>How far below the top of its line the lowest ink of a box is.</summary>
        static double Below(DrawingParagraphTests.Shot shot, Rect box) => shot.InkBottom(box) - box.Y;

        [Fact]
        public void Offset_is_not_drawn_yet_so_raised_and_lowered_runs_sit_on_the_text_baseline() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                Assert.Equal(4000, s.Editor.Document.Paragraphs[5].Runs.Single(r => r.Text == "up").Props.IntProps[TextPropertyNumbers.Offset].Value);
                // The p of "up" (raised 4 pt) and the p of "pt" beside it end on the same row; so do the n of "down" (lowered) and the n of "drawn".
                // (Measured from the top of the line, as the two may be on different lines; 1 pixel allows for rounding, a 4 point shift is 5.)
                Assert.InRange(Below(shot, BoxOf(s, 5, ": up", 3, 1)) - Below(shot, BoxOf(s, 5, "pt", 0, 1)), -1, 1);
                Assert.InRange(Below(shot, BoxOf(s, 5, "down", 3, 1)) - Below(shot, BoxOf(s, 5, "drawn", 4, 1)), -1, 1);
            }
        });

        static bool IsRed(DrawingParagraphTests.Shot shot, int x, int y) { var c = shot.At(x, y); return c.R > 200 && c.G < 90 && c.B < 90; }

        /// <summary>For each column of the box that has red ink in the rows below the baseline, the topmost such row; and the number of red
        /// pixels in the rows above the baseline.</summary>
        static (List<int> Tops, int Above, int Columns) Squiggle(DrawingParagraphTests.Shot shot, Session s, int paragraph, int at, int length)
        {
            var pl = s.Field.ParagraphLayoutFor(paragraph);
            var li = pl.Lines[pl.LineIndexOf(at)];
            var bounds = li.Line.GetTextBounds(at, System.Math.Min(length, li.End - at));
            double x0 = bounds.Min(b => b.Rectangle.X) + li.X, x1 = bounds.Max(b => b.Rectangle.Right) + li.X;
            int baseline = (int)System.Math.Round(s.Field.ParagraphTop(paragraph) + li.TextY + li.Line.Baseline);
            var tops = new List<int>();
            int above = 0;
            for (int x = (int)x0 + 1; x < (int)x1 - 1; x++)
            {
                for (int y = baseline - 14; y < baseline; y++) if (IsRed(shot, x, y)) above++;   // (the line above has its own squiggle further up)
                for (int y = baseline; y < baseline + 12; y++) if (IsRed(shot, x, y)) { tops.Add(y - baseline); break; }
            }
            return (tops, above, System.Math.Max(0, (int)x1 - 1 - ((int)x0 + 1)));
        }

        [Fact]
        public void The_squiggle_underline_is_a_zigzag_below_the_baseline_in_the_under_colour_and_none_above_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing))
            {
                var shot = new DrawingParagraphTests.Shot(s);
                int at = s.ParagraphText(4).IndexOf("scribble", System.StringComparison.Ordinal);
                var (tops, above, _) = Squiggle(shot, s, 4, at, "scribble".Length);
                Assert.Equal(0, above);
                Assert.True(tops.Count >= 25, "red ink under most columns of the word: " + tops.Count);
                Assert.True(tops.Min() >= 0, "below the baseline");
                Assert.True(tops.Max() - tops.Min() >= 2, "it rises and falls: rows " + string.Join(",", tops));
                Assert.True(tops.Max() <= 8, "close under the text: " + tops.Max());
                // The letters of the word are still the text colour, and the run before it (a plain one) has no red below it.
                var before = Squiggle(shot, s, 4, s.ParagraphText(4).IndexOf("Lines", System.StringComparison.Ordinal), 5);
                Assert.Empty(before.Tops);
            }
        });

        [Fact]
        public void A_squiggled_run_that_wraps_over_lines_is_squiggled_on_each_of_them() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Drawing, 330, 700))
            {
                var pl = s.Field.ParagraphLayoutFor(4);
                const string Token = "a squiggled run that is long enough to wrap onto a second line";
                int at = s.ParagraphText(4).IndexOf(Token, System.StringComparison.Ordinal);
                Assert.True(at >= 0, "the run is in the paragraph");
                int end = at + Token.Length;
                int first = pl.LineIndexOf(at), last = pl.LineIndexOf(end - 1);
                Assert.True(last > first, "the run is on more than one line");
                var shot = new DrawingParagraphTests.Shot(s);
                int checkedParts = 0;
                for (int i = first; i <= last; i++)
                {
                    int from = System.Math.Max(at, pl.Lines[i].Start), to = System.Math.Min(end, pl.Lines[i].End);
                    var part = Squiggle(shot, s, 4, from, to - from);
                    Assert.Equal(0, part.Above);
                    if (part.Columns < 8) continue;   // a word or two left over at the end of a line
                    checkedParts++;
                    Assert.True(part.Tops.Count >= part.Columns * 0.75, "line " + i + ": red ink under " + part.Tops.Count + " of " + part.Columns + " columns");
                    Assert.True(part.Tops.Max() - part.Tops.Min() >= 2, "line " + i + " rows " + string.Join(",", part.Tops));
                }
                Assert.True(checkedParts >= 2, "at least two lines of the run were checked");
            }
        });
        [Fact]
        public void The_character_style_box_sets_the_named_style_on_the_selection_and_none_clears_it() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Select(0, 4, 8);
                var box = s.Find<ComboBox>(PlaygroundView.Names.CharStyle);
                box.SelectedItem = "Warning";
                Headless.Pump();
                var p0 = s.Editor.Document.Paragraphs[0];
                var run = p0.Runs.Single(r => r.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle));
                Assert.Equal("word", run.Text);
                Assert.Equal("Warning", run.Props.StrProps[TextPropertyNumbers.NamedStyle]);
                Assert.Equal(Color.FromRgb(255, 0, 0), ColorOf(RunWith(s, 0, "word").ForegroundBrush));
                Assert.Null(box.SelectedItem);

                box.SelectedItem = SampleStyles.None;
                Headless.Pump();
                Assert.DoesNotContain(s.Editor.Document.Paragraphs[0].Runs, r => r.Props.StrProps.ContainsKey(TextPropertyNumbers.NamedStyle));
            }
        });
    }
}
