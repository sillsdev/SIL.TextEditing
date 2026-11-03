using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>
    /// Visual caret stops, arrow walks, hit testing and selection rectangles over mixed left-to-right and right-to-left
    /// text, with expected values derived by hand from the bidi algorithm. Two Avalonia faults are pinned here as known
    /// failures so a fix shows up as a failing test (and the workaround can go): hit testing in a right-to-left
    /// paragraph on Avalonia 11.3, and inline objects in right-to-left text on both generations.
    /// </summary>
    public class BidiTests
    {
        const string Text = "ab عربي cd"; // ab, four Arabic letters, cd

        sealed class Case
        {
            public string Name = "";
            public bool RtlParagraph;
            public int[] Visual = Array.Empty<int>();   // logical cluster index at each visual position, left to right
            public bool[] RtlChar = Array.Empty<bool>();
        }

        static readonly Case Ltr = new Case
        {
            Name = "LTR paragraph",
            RtlParagraph = false,
            Visual = new[] { 0, 1, 2, 6, 5, 4, 3, 7, 8, 9 },
            RtlChar = new[] { false, false, false, true, true, true, true, false, false, false },
        };

        static readonly Case Rtl = new Case
        {
            Name = "RTL paragraph",
            RtlParagraph = true,
            Visual = new[] { 8, 9, 7, 6, 5, 4, 3, 2, 0, 1 },
            RtlChar = new[] { false, false, true, true, true, true, true, true, false, false },
        };

        static RichTextField Build(Case c)
        {
            var p = Para("en", "en", Text);
            p.Rtl = c.RtlParagraph;
            return Fixtures.Field(Doc(p), 520);
        }

        /// <summary>Offsets allowed at each of the n+1 visual positions, left to right.</summary>
        static List<HashSet<int>> Allowed(Case c)
        {
            int n = c.Visual.Length;
            var allowed = new List<HashSet<int>>();
            for (int k = 0; k <= n; k++)
            {
                var set = new HashSet<int>();
                if (k > 0) { int u = c.Visual[k - 1]; set.Add(c.RtlChar[u] ? u : u + 1); }
                if (k < n) { int v = c.Visual[k]; set.Add(c.RtlChar[v] ? v + 1 : v); }
                allowed.Add(set);
            }
            return allowed;
        }

        static List<string> HitTestMismatches(RichTextField field, Case c, List<ParagraphLayout.Stop> stops)
        {
            var li = field.ParagraphLayoutFor(0).Lines[0];
            var bad = new List<string>();
            for (int k = 0; k < c.Visual.Length; k++)
            {
                int i = c.Visual[k];
                double xl = stops[k].X, xr = stops[k + 1].X, y = li.Line.Height / 2;
                int leftExpected = c.RtlChar[i] ? i + 1 : i, rightExpected = c.RtlChar[i] ? i : i + 1;
                var pl = field.PointToPosition(new Point(xl + (xr - xl) * 0.25, y));
                var pr = field.PointToPosition(new Point(xl + (xr - xl) * 0.75, y));
                if (pl.Offset != leftExpected) bad.Add("cluster " + i + " left got " + pl.Offset + " expected " + leftExpected);
                if (pr.Offset != rightExpected) bad.Add("cluster " + i + " right got " + pr.Offset + " expected " + rightExpected);
            }
            return bad;
        }

        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "ltr" };
            yield return new object[] { "rtl" };
        }

        static Case ByName(string n) => n == "ltr" ? Ltr : Rtl;

        [Theory, MemberData(nameof(Cases))]
        public void Caret_stops_are_the_visual_positions_left_to_right(string which) => Headless.Run(() =>
        {
            var c = ByName(which);
            var field = Build(c);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            var allowed = Allowed(c);
            Assert.Equal(c.Visual.Length + 1, stops.Count);
            for (int k = 0; k < stops.Count; k++)
                Assert.True(allowed[k].Contains(stops[k].Offset), c.Name + " stop " + k + " offset " + stops[k].Offset + " not in {" + string.Join("|", allowed[k]) + "}");
        });

        [Theory, MemberData(nameof(Cases))]
        public void Right_and_left_arrows_visit_the_visual_order(string which) => Headless.Run(() =>
        {
            var c = ByName(which);
            var field = Build(c);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            var allowed = Allowed(c);
            int n = c.Visual.Length;

            field.Editor.SetCaret(new TextPosition(0, stops[0].Offset));
            field.SetCaretHit(stops[0].Hit);
            var right = new List<int> { field.Editor.Caret.Offset };
            for (int k = 0; k < n + 2; k++) { field.Press(Key.Right); right.Add(field.Editor.Caret.Offset); }
            for (int k = 0; k <= n; k++) Assert.True(allowed[k].Contains(right[k]), c.Name + " Right press " + k + ": " + string.Join(" ", right));

            field.Editor.SetCaret(new TextPosition(0, stops[n].Offset));
            field.SetCaretHit(stops[n].Hit);
            var left = new List<int> { field.Editor.Caret.Offset };
            for (int k = 0; k < n + 2; k++) { field.Press(Key.Left); left.Add(field.Editor.Caret.Offset); }
            for (int k = 0; k <= n; k++) Assert.True(allowed[n - k].Contains(left[k]), c.Name + " Left press " + k + ": " + string.Join(" ", left));
        });

        [Fact]
        public void Hit_testing_in_a_left_to_right_paragraph_is_exact() => Headless.Run(() =>
        {
            var field = Build(Ltr);
            var layout = field.ParagraphLayoutFor(0);
            Assert.Empty(HitTestMismatches(field, Ltr, layout.VisualStops(layout.Lines[0])));
        });

        [Fact]
        public void Hit_testing_in_a_right_to_left_paragraph() => Headless.Run(() =>
        {
            var field = Build(Rtl);
            var layout = field.ParagraphLayoutFor(0);
            var bad = HitTestMismatches(field, Rtl, layout.VisualStops(layout.Lines[0]));
            if (Headless.AvaloniaMajor >= 12) Assert.Empty(bad);
            else
            {
                // Known Avalonia 11.3.17 fault: a cluster of Latin letters inside the Arabic run maps to offsets +2.
                // Spike measurement: 4 of 20 wrong. When this starts passing, drop the workaround note in the README.
                Assert.NotEmpty(bad);
            }
        });

        [Theory, MemberData(nameof(Cases))]
        public void Selection_rectangles_cover_the_visual_groups_of_a_logical_range(string which) => Headless.Run(() =>
        {
            var c = ByName(which);
            var field = Build(c);
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            foreach (var range in new[] { new[] { 2, 5 }, new[] { 3, 7 }, new[] { 0, 10 }, new[] { 6, 9 } })
            {
                var rects = layout.SelectionRects(range[0], range[1]);
                var visual = c.Visual.Select((ci, k) => new { ci, k }).Where(t => t.ci >= range[0] && t.ci < range[1]).Select(t => t.k).ToList();
                int groups = 0;
                for (int q = 0; q < visual.Count; q++) if (q == 0 || visual[q] != visual[q - 1] + 1) groups++;
                double expectedWidth = visual.Sum(k => stops[k + 1].X - stops[k].X);
                var sorted = rects.OrderBy(r => r.X).ToList();
                int merged = 0;
                for (int q = 0; q < sorted.Count; q++) if (q == 0 || sorted[q].X > sorted[q - 1].Right + 0.5) merged++;
                Assert.True(merged == groups, c.Name + " [" + range[0] + "," + range[1] + ") groups " + merged + " expected " + groups);
                Assert.True(Math.Abs(expectedWidth - rects.Sum(r => r.Width)) < 1.5, c.Name + " [" + range[0] + "," + range[1] + ") width");
            }
        });

        static List<int> OrcStopOffsets(bool glyph, out int hitMismatches)
        {
            var p = new Paragraph { Rtl = true, DefaultWs = "ar" };
            p.Runs.Add(new Run { Ws = "ar", Text = "مرحبا" });
            p.Runs.Add(Run.Object("ar", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "ar", Text = "بال" });
            var field = Fixtures.Field(Doc(p), 520, f => { f.ObjectAsGlyph = glyph; f.ObjectImage = Fixtures.Picture; });
            var layout = field.ParagraphLayoutFor(0);
            var stops = layout.VisualStops(layout.Lines[0]);
            hitMismatches = 0;
            for (int k = 0; k + 1 < stops.Count; k++)
            {
                var pos = field.PointToPosition(new Point((stops[k].X + stops[k + 1].X) / 2, 5));
                if (pos.Offset != Math.Max(stops[k].Offset, stops[k + 1].Offset) && pos.Offset != Math.Min(stops[k].Offset, stops[k + 1].Offset)) hitMismatches++;
            }
            return stops.Select(s => s.Offset).ToList();
        }

        static bool StrictlyDescending(List<int> offsets) => offsets.Count == 10 && offsets.Zip(offsets.Skip(1), (a, b) => a > b).All(x => x);

        [Fact]
        public void Object_in_right_to_left_text_with_the_em_space_workaround_has_ordered_stops() => Headless.Run(() =>
        {
            var offsets = OrcStopOffsets(true, out int bad);
            Assert.True(StrictlyDescending(offsets), string.Join(" ", offsets));
            Assert.Equal(0, bad);
        });

        [Fact]
        public void Object_in_right_to_left_text_as_a_drawable_run_is_a_known_avalonia_fault() => Headless.Run(() =>
        {
            // Avalonia issue 21792: stops by x come out 9 8 6 7 5 4 3 2 1 0, not monotonic. Present in 11.3.17 and 12.1.2.
            // If this fails, Avalonia fixed it: make ObjectAsGlyph default off and delete the workaround.
            var offsets = OrcStopOffsets(false, out _);
            Assert.False(StrictlyDescending(offsets), string.Join(" ", offsets));
        });
    }
}
