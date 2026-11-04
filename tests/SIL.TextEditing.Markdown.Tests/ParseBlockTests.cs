using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Spec 3.1: how Markdown blocks become paragraphs and their styles.</summary>
    public class ParseBlockTests
    {
        static string Dump(string markdown, MarkdownOptions? options = null)
            => ModelDump.Of(MarkdownConverter.Parse(markdown, options ?? Hosts.Project()).Document);

        static string[] Ids(string markdown) => MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics.Select(d => d.Id).ToArray();

        [Theory]
        [InlineData("Hello world", "Normal| en\"Hello world\"")]
        [InlineData("One\n\nTwo", "Normal| en\"One\"\nNormal| en\"Two\"")]
        [InlineData("", "")]
        [InlineData("\n\n", "")]
        public void PlainParagraphsAreNormal(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Fact]
        public void ASoftLineBreakIsOneSpace() => Assert.Equal("Normal| en\"one two\"", Dump("one\ntwo"));

        [Fact]
        public void ABackslashAtLineEndIsALineSeparator() => Assert.Equal("Normal| en\"one\\x2028two\"", Dump("one\\\ntwo"));

        [Fact]
        public void TwoTrailingSpacesAreNotAHardBreak() => Assert.Equal("Normal| en\"one two\"", Dump("one  \ntwo"));

        [Theory]
        [InlineData("# A", "Heading 1| en\"A\"")]
        [InlineData("## B c", "Heading 2| en\"B c\"")]
        [InlineData("### D", "Heading 3| en\"D\"")]
        public void HeadingsMapToHeadingStyles(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Fact]
        public void AHeadingBeyondLevelThreeIsRefused()
        {
            var result = MarkdownConverter.Parse("#### Too deep", Hosts.Project());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("gdr.heading.too-deep", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.True(result.HasErrors);
        }

        [Fact]
        public void BulletsAndNumbersMapToListStyles()
            => Assert.Equal("Bulleted List| en\"a\"\nBulleted List| en\"b\"\nNumbered List| en\"c\"\nNumbered List| en\"d\"",
                Dump("- a\n- b\n\n1. c\n7. d"));

        [Fact]
        public void ABlockQuoteIsBlockQuote() => Assert.Equal("Block Quote| en\"q\"", Dump("> q"));

        [Fact]
        public void EachQuotedParagraphIsOneParagraph() => Assert.Equal("Block Quote| en\"q\"\nBlock Quote| en\"r\"", Dump("> q\n>\n> r"));

        [Fact]
        public void AListItemWithALineSeparatorKeepsIt() => Assert.Equal("Bulleted List| en\"a\\x2028b\"", Dump("- a\\\n  b"));

        [Fact]
        public void ANestedListIsUnsupported() => Assert.Contains("gdr.syntax.unsupported-markup", Ids("- a\n  - b"));

        [Theory]
        [InlineData("{empty}", "Normal| (empty en)")]
        [InlineData("{empty style=\"Heading 2\"}", "Heading 2| (empty en)")]
        [InlineData("{empty v}", "Normal| (empty ceb)")]
        [InlineData("{empty ws=ar}", "Normal| (empty ar)")]
        [InlineData("{empty style=\"\"}", "| (empty en)")]
        public void EmptyParagraphs(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Fact]
        public void EmptyParagraphsAreKeptBetweenParagraphs()
            => Assert.Equal("Normal| en\"a\"\nNormal| (empty en)\nNormal| en\"b\"", Dump("a\n\n{empty}\n\nb"));

        [Fact]
        public void EmptyAlonOnALineMustBeTheWholeLine() => Assert.Equal("Normal| en\"{empty} more\"", Dump("{empty} more"));

        [Fact]
        public void EmptyWithPropertiesKeepsThem()
            => Assert.Equal("Normal| para{p128=1:0} (empty en)", Dump("{empty rightToLeft=1}"));

        // ------------------------------------------------------------ tables

        [Fact]
        public void APipeTableIsOneParagraphPerRowWithTabs()
        {
            const string md = "| Name | Gloss |\n| --- | --- |\n| mang | eat |\n| kaon | |";
            Assert.Equal(
                "GDR Table Header| en\"Name\\x0009Gloss\"\nGDR Table Row| en\"mang\\x0009eat\"\nGDR Table Row| en\"kaon\\x0009\"",
                Dump(md));
        }

        [Fact]
        public void ATableWithOneColumnAndNoBodyIsAHeaderOnly()
            => Assert.Equal("GDR Table Header| en\"A\"", Dump("| A |\n| --- |"));

        [Fact]
        public void ARaggedRowIsReported()
        {
            var ids = Ids("| A | B | C |\n| --- | --- | --- |\n| 1 | 2 |");
            Assert.Contains("gdr.table.ragged", ids);
        }

        [Fact]
        public void ARowWithTooManyCellsIsReported()
            => Assert.Contains("gdr.table.ragged", Ids("| A | B |\n| --- | --- |\n| 1 | 2 | 3 |"));

        // Rows count from the header, which is row 1; the delimiter row is not counted. The finding carries the row's line as well.
        [Theory]
        [InlineData("| A | B | C |\n| --- | --- | --- |\n| 1 | 2 |", "Row 2 has 2 cells, the header has 3. Add an empty cell or remove one.", 3)]
        [InlineData("| A | B | C |\n| --- | --- | --- |\n| 1 |", "Row 2 has 1 cell, the header has 3. Add an empty cell or remove one.", 3)]
        [InlineData("| A | B |\n| --- | --- |\n| 1 | 2 |\n| 1 | 2 | 3 |", "Row 3 has 3 cells, the header has 2. Add an empty cell or remove one.", 4)]
        [InlineData("| A |\n| --- |\n| 1 | 2 |", "Row 2 has 2 cells, the header has 1. Add an empty cell or remove one.", 3)]
        public void ARaggedRowSaysHowManyCellsItHasWithTheHeaderAsRowOne(string markdown, string message, int line)
        {
            var d = Assert.Single(MarkdownConverter.Parse(markdown, Hosts.Project()).Diagnostics, x => x.Id == "gdr.table.ragged");
            Assert.Equal(message, d.Message);
            Assert.Equal(line, d.Line);
        }

        [Fact]
        public void AnAlignmentColonIsReportedNotDropped()
        {
            var result = MarkdownConverter.Parse("| A |\n| :---: |\n| x |", Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "gdr.table.alignment" && d.Replacement == "---");
        }

        [Theory]
        [InlineData("| A |\n| --- |\n| x&#x9;y |")]
        [InlineData("| A |\n| --- |\n| x&#x2028;y |")]
        public void ATabOrLineSeparatorInACellIsReported(string md) => Assert.Contains("gdr.table.tab-in-cell", Ids(md));

        [Fact]
        public void AnEscapedPipeStaysInTheCell()
            => Assert.Equal("GDR Table Header| en\"a|b\"", Dump("| a\\|b |\n| --- |"));

        [Fact]
        public void TwoTablesAreSeparatedByABlankLineAndANewHeader()
            => Assert.Equal("GDR Table Header| en\"A\"\nGDR Table Row| en\"1\"\nGDR Table Header| en\"B\"\nGDR Table Row| en\"2\"",
                Dump("| A |\n| --- |\n| 1 |\n\n| B |\n| --- |\n| 2 |"));

        // ------------------------------------------------------------ fences

        [Fact]
        public void AFenceGivesEveryLineItsStyle()
            => Assert.Equal("Example| en\"one\"\nExample| en\"two\"", Dump("::: {style=\"Example\"}\none\ntwo\n:::"));

        [Fact]
        public void AFenceWithoutAStyleIsNormalWithTheProperties()
            => Assert.Equal("Normal| para{p6=12000:1} en\"x\"", Dump("::: {fontsize=12000}\nx\n:::"));

        [Fact]
        public void BlockMarkersInsideAFenceAreLiteralText()
            => Assert.Equal("Example| en\"# not a heading\"\nExample| en\"- not a list\"\nExample| en\"> not a quote\"\nExample| en\"1. not a number\"\nExample| en\"| not | a table |\"\nExample| en\"---\"",
                Dump("::: {style=\"Example\"}\n# not a heading\n- not a list\n> not a quote\n1. not a number\n| not | a table |\n---\n:::"));

        [Fact]
        public void AFenceLineEndingInABackslashContinuesWithALineSeparator()
            => Assert.Equal("Example| en\"a\\x2028b\"\nExample| en\"c\"", Dump("::: {style=\"Example\"}\na\\\nb\nc\n:::"));

        [Fact]
        public void AFenceCanHoldAnEmptyParagraph()
            => Assert.Equal("Example| (empty en)", Dump("::: {style=\"Example\"}\n{empty}\n:::"));

        [Fact]
        public void AFenceIsSeparatedFromTheParagraphsAroundIt()
            => Assert.Equal("Normal| en\"before\"\nExample| en\"inside\"\nNormal| en\"after\"", Dump("before\n::: {style=\"Example\"}\ninside\n:::\nafter"));

        [Fact]
        public void AnUnclosedFenceIsReported()
            => Assert.Contains("gdr.syntax.unclosed-mark", Ids("::: {style=\"Example\"}\ninside"));

        [Fact]
        public void AnEmptyStyleIsWrittenAsAnEmptyString()
            => Assert.Equal("| en\"x\"", Dump("::: {style=\"\"}\nx\n:::"));

        [Fact]
        public void TableRowsInAFenceAreOrphanRows()
            => Assert.Equal("GDR Table Row| en\"a\\x0009b\"", Dump("::: {style=\"GDR Table Row\"}\na&#x9;b\n:::"));

        // ------------------------------------------------------------ unsupported markup

        [Theory]
        [InlineData("a <b>x</b>")]
        [InlineData("a `code` b")]
        [InlineData("see <http://example.org>")]
        [InlineData("<div>x</div>")]
        [InlineData("```\ncode\n```")]
        [InlineData("    indented")]
        [InlineData("---")]
        [InlineData("[ng]: foo")]
        public void RawHtmlCodeAndOtherUnsupportedMarkupIsReported(string md) => Assert.Contains("gdr.syntax.unsupported-markup", Ids(md));

        [Fact]
        public void TheEscapedFormsAreFine()
        {
            Assert.Empty(Ids("a \\<b> \\`x\\`"));
            Assert.Equal("Normal| en\"a <b> `x`\"", Dump("a \\<b> \\`x\\`"));
        }

        [Fact]
        public void DiagnosticsCarryLineAndColumn()
        {
            var result = MarkdownConverter.Parse("fine\n\nsee [kaon]{ws=ceb-x-old", Hosts.Project());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("gdr.syntax.unclosed-mark", d.Id);
            Assert.Equal(3, d.Line);
            Assert.Equal(11, d.Column);
        }

        [Fact]
        public void ATextBeginningWithAByteOrderMarkReadsTheSame()
            => Assert.Equal(Dump("a"), Dump("\xFEFFa"));
    }
}
