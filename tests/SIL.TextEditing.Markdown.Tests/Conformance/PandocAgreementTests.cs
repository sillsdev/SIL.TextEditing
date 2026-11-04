using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Pandoc reads every conformance fixture the way this library does (ADR 0028, spec 6.4): the Markdown of each case, and the canonical Markdown the
    /// writer produces, is read with <c>pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json</c>, the result is mapped to the run
    /// model (<see cref="PandocModel"/>), and the paragraphs must equal the ones the fixture expects. A fixture sets <c>"pandoc": false</c> (with a reason) where
    /// Pandoc is not expected to agree: an error case, or text that is not a spelling Pandoc is asked to read.</summary>
    public class PandocAgreementTests
    {
        public static IEnumerable<object[]> Cases()
            => ConformanceLoader.All().Where(c => c.Pandoc && !c.HasErrors).Select(c => new object[] { c.Id });

        [PandocTheory]
        [MemberData(nameof(Cases))]
        public void PandocReadsTheFixtureAsWeDo(string id)
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            var c = ConformanceLoader.Find(id);
            var options = ConformanceTests.OptionsFor(c, out _);
            options.WriteFrontMatter = false;
            // What the library itself reads is the reference when the fixture gives no paragraphs.
            var expected = c.Paragraphs;
            if (c.IsRead)
            {
                string full = c.FullMarkdown(c.Markdown!);
                if (expected == null) expected = MarkdownConverter.Parse(full, options).Document.Paragraphs.ToList();
                Compare(full, expected, options, "the Markdown of the case");
                if (c.Canonical != null) Compare(c.FullMarkdown(c.Canonical), expected, options, "its canonical form");
            }
            else Compare(c.FullMarkdown(c.Canonical!), expected!, options, "the canonical form of the model");
        }

        static void Compare(string markdown, IReadOnlyList<Paragraph> expected, MarkdownOptions options, string what)
        {
            List<Paragraph> actual;
            try { actual = PandocModel.Convert(PandocRunner.ToJson(markdown), options); }
            catch (PandocMismatch e) { throw new Xunit.Sdk.XunitException("Pandoc reads " + what + " as something the profile has no meaning for: " + e.Message + "\n" + markdown); }
            ConformanceTests.AssertParagraphs(expected, actual, "Pandoc, reading " + what + ":\n" + markdown);
        }

        [PandocFact]
        public void ThePinnedPandocIsTheOneTheWorkflowInstalls()
        {
            Assert.True(PandocRunner.Available, "Pandoc is not on the PATH, and this run says it must be (CI)");
            // The workflow pins 3.x; the AST of an older Pandoc differs (table cells, attributes), so say which one this run used.
            Assert.StartsWith("pandoc 3.", PandocRunner.Version);
        }
    }
}
