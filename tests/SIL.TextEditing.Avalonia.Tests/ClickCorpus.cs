using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Paragraphs of several runs, mixed directions and spaces, found by a fuzz scan, in which a click at the x of a caret stop (or beyond the End of a
    /// wrapped line) gave a caret somewhere else. A case is a paragraph and the width of the field that lays it out.</summary>
    static class ClickCorpus
    {
        internal sealed class Case
        {
            public Case(bool rtl, int align, double width, params (string Ws, bool Bold, string Text)[] runs)
            {
                Rtl = rtl;
                Align = align;
                Width = width;
                Runs = runs;
            }

            public bool Rtl { get; }
            public int Align { get; }
            public double Width { get; }
            public (string Ws, bool Bold, string Text)[] Runs { get; }

            public override string ToString() => (Rtl ? "rtl" : "ltr") + " align " + Align + " width " + Width + " " + Runs.Sum(run => run.Text.Length) + " characters";

            public ParagraphLayout Layout()
            {
                var p = Para("en");
                foreach (var run in Runs)
                {
                    var r = new Run { Ws = run.Ws, Text = run.Text };
                    if (run.Bold) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                    p.Runs.Add(r);
                }
                p.Rtl = Rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, Align);
                return Fixtures.Field(Doc(p), Width).ParagraphLayoutFor(0);
            }
        }

        static (string, bool, string) R(string ws, bool bold, string text) => (ws, bold, text);

        internal static readonly Case[] EdgeCases = new[]
        {
            new Case(true, 5, 349, R("en", true, "ill l   i dog   it هذه it l   طويلة dog ill 12 ill  "), R("en", false, " fox (جمل) i"), R("en", true, "   12 فقرة l 12 12 i it   طويلة فقرة   l   (جمل) il"), R("en", false, "l Wonderful ill ")),
            new Case(true, 5, 111, R("ar", false, "فقرة ill   i ét   fox طويلة ét طويلة ill   dog فقر"), R("ar", false, "ة i jumps l 12 dog i"), R("ar", true, "ll l it   jumps   12   هذه   ét Wonderful هذه   ét ét it "), R("en", false, "  Wonderful Wonderful فقرة هذه fox   it ")),
            new Case(false, 5, 138, R("ar", true, "fox ét fox"), R("en", false, " l هذه "), R("en", false, "  (جمل) هذه   فقرة jumps (جمل) "), R("ar", false, "dog fox (جمل) 12 it ét dog فقرة   fox dog 12 "))
        };

        /// <summary>The numbers of the cases in <paramref name="cases"/>, for a theory (a case itself is not something xunit can carry).</summary>
        public static IEnumerable<object[]> Numbers(Case[] cases) => Enumerable.Range(0, cases.Length).Select(i => new object[] { i });

        public static IEnumerable<object[]> EdgeNumbers() => Numbers(EdgeCases);
    }
}
