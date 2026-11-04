using System;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Big inputs: the parser returns findings and the writer returns text, in time that grows with the input. The checks look at
    /// what comes back, never at the clock; an input that would take minutes in quadratic time is what makes a slow path visible.</summary>
    public class ScaleTests
    {
        [Fact]
        public void ManyFindingsAndManyEmptyMarkersAreReadInOnePass()
        {
            const int count = 20_000;
            string text = string.Concat(Enumerable.Repeat("[x]{wss=1}\n\n", count)) + string.Concat(Enumerable.Repeat("{empty}\n\n", count));
            var result = MarkdownConverter.Parse(text);
            Assert.Equal(count, result.Diagnostics.Count(d => d.Id == "gdr.syntax.unknown-attribute"));
            Assert.Equal(count, result.Document.Paragraphs.Count(p => p.Runs.Count == 0));
            Assert.Equal(2 * count, result.Document.Paragraphs.Count);
        }

        [Fact]
        public void AnEmptyMarkerDropsTheFindingsMarkdigMadeWhileReadingItsQuotedValues()
        {
            // A quoted value that looks like a span or a link draws findings while Markdig reads the line as text; the marker is read from
            // the raw line, so they go, and only the findings of the lines around it stay.
            string text = "[a]{wss=1}\n\n{empty s2=\"[x]{wss=2} [y]{ws=\" v}\n\n[b]{wss=3}\n\n{empty s2=\"[z](q){a,b}\"}\n\n[c]{wss=4}";
            var result = MarkdownConverter.Parse(text);
            Assert.Equal(new[] { 1, 5, 9 }, result.Diagnostics.Select(d => d.Line).ToArray());
            Assert.Equal(5, result.Document.Paragraphs.Count);
        }
    }
}
