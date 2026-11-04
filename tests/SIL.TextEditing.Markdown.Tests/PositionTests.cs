using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Every finding carries its exact line, column and the text it is about, in every kind of block.</summary>
    public class PositionTests
    {
        static void AssertFindings(string markdown, int line, params (string id, string observed)[] expected)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            string[] lines = markdown.Split('\n');
            foreach (var (id, observed) in expected)
            {
                var found = result.Diagnostics.Where(d => d.Id == id && d.Observed == observed).ToList();
                Assert.True(found.Count == 1, id + " '" + observed + "' in:\n" + string.Join("\n", result.Diagnostics));
                var d = found[0];
                int column = lines[line - 1].IndexOf(observed, System.StringComparison.Ordinal) + 1;
                Assert.Equal(line, d.Line);
                Assert.Equal(column, d.Column);
                Assert.Equal(line, d.EndLine);
                Assert.Equal(column + observed.Length, d.EndColumn);
            }
        }

        const string Marks = "`x` <b> <http://x.y> [[w:nope]] ![c](fw:picture/00000000-0000-0000-0000-000000000000) [y]{ws=zzz-unknown} [z]{wss=1}";

        static readonly (string, string)[] MarkFindings =
        {
            ("gdr.syntax.unsupported-markup", "`x`"),
            ("gdr.syntax.unsupported-markup", "<b>"),
            ("gdr.syntax.unsupported-markup", "<http://x.y>"),
            ("gdr.link.not-found", "[[w:nope]]"),
            ("gdr.picture.unknown", "![c](fw:picture/00000000-0000-0000-0000-000000000000)"),
            ("gdr.ws.unknown", "ws=zzz-unknown"),
            ("gdr.syntax.unknown-attribute", "wss=1"),
        };

        [Fact]
        public void InAParagraphAfterAnother() => AssertFindings("a\n\n" + Marks, 3, MarkFindings);

        [Fact]
        public void InAListItem() => AssertFindings("a\n\n- " + Marks, 3, MarkFindings);

        [Fact]
        public void InAQuote() => AssertFindings("a\n\n> " + Marks, 3, MarkFindings);

        [Fact]
        public void InAHeading() => AssertFindings("a\n\n# " + Marks, 3, MarkFindings);

        [Fact]
        public void InAFenceLine() => AssertFindings("a\n\n::: {style=S}\n" + Marks + "\n:::", 4, MarkFindings);

        [Fact]
        public void InATableCellAfterAParagraph() => AssertFindings("a\n\n| " + Marks.Replace("|", "x") + " |\n| --- |", 3, MarkFindings);

        [Fact]
        public void InTheCellsOfASecondTable()
            => AssertFindings("| h |\n| --- |\n\nb\n\n| x | `x` | [[w:nope]] | [y]{ws=zzz-unknown} |\n| --- | --- | --- | --- |", 6,
                ("gdr.syntax.unsupported-markup", "`x`"), ("gdr.link.not-found", "[[w:nope]]"), ("gdr.ws.unknown", "ws=zzz-unknown"));

        [Fact]
        public void InABodyRowOfATable()
            => AssertFindings("a\n\n| h | h |\n| --- | --- |\n| `x` | [[w:nope]] |", 5,
                ("gdr.syntax.unsupported-markup", "`x`"), ("gdr.link.not-found", "[[w:nope]]"));

        // The text form [text]{link=...} is the common way to write a link; its finding sits on the whole [text]{...} span.
        const string TextLink = "[see]{link=w:zzz}";
        const string TextLinkWithUrl = "[see this]{url=\"x\" link=w:zzz}";

        [Fact]
        public void ATextFormLinkInAParagraph() => AssertFindings("a\n\nb " + TextLink + " c", 3, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkWithAUrlInAParagraph() => AssertFindings("a\n\nb " + TextLinkWithUrl + " c", 3, ("gdr.link.not-found", TextLinkWithUrl));

        [Fact]
        public void ATextFormLinkOnTheSecondLineOfAParagraph() => AssertFindings("a\n\nfirst line\nsecond " + TextLink + " c", 4, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkOnTheContinuationLineOfAListItem() => AssertFindings("a\n\n- first\n  b " + TextLink, 4, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAQuote() => AssertFindings("a\n\n> b " + TextLink, 3, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAHeading() => AssertFindings("a\n\n# b " + TextLink, 3, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInAFenceLine() => AssertFindings("a\n\n::: {style=S}\nb " + TextLink + " c\n:::", 4, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInATableCell() => AssertFindings("a\n\n| h | h |\n| --- | --- |\n| x | b " + TextLink + " c |", 5, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInTheHeaderOfASecondTable() => AssertFindings("| h |\n| --- |\n\nb\n\n| x " + TextLink + " | y |\n| --- | --- |", 6, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInACellNextToAnotherSpan() => AssertFindings("a\n\n| [p]{v} | " + TextLink + " |\n| --- | --- |", 3, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkInALaterRowOfATableAfterAFence()
            => AssertFindings("::: {style=S}\nq\n:::\n\n| h | h |\n| --- | --- |\n| a | b |\n| c | d " + TextLink + " |", 8, ("gdr.link.not-found", TextLink));

        [Fact]
        public void ATextFormLinkWithCrlfLineEnds()
        {
            var d = Assert.Single(MarkdownConverter.Parse("a\r\n\r\nb " + TextLink + " c\r\n", Hosts.Project()).Diagnostics);
            Assert.Equal((3, 3, 3, 3 + TextLink.Length), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.Equal(TextLink, d.Observed);
        }

        [Fact]
        public void ANestedSpanInsideATextFormLinkDoesNotMoveIt() => AssertFindings("a\n\nb [see [c]{v} d]{link=w:zzz} e", 3, ("gdr.link.not-found", "[see [c]{v} d]{link=w:zzz}"));

        [Fact]
        public void ATextFormLinkTheHostResolvesRaisesNothing()
        {
            var host = new TestObjects();
            var guid = new System.Guid("11111111-2222-3333-4444-555555555555");
            host.Names[guid] = "palit";
            Assert.Empty(MarkdownConverter.Parse("a\n\n[see]{link=w:palit guid=" + guid + "}", Hosts.Project(host)).Diagnostics);
        }

        [Theory]
        [InlineData("a `x` b", "\\`x\\`")]
        [InlineData("a <b>x</b> c", "\\<b>")]
        [InlineData("a <http://x.y> c", "\\<http://x.y>")]
        [InlineData("<div>x</div>", "\\<div>x\\</div>")]
        public void UnsupportedMarkupComesWithTheEscapeAsTheFix(string markdown, string fix)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "gdr.syntax.unsupported-markup" && d.Replacement == fix);
        }

        [Fact]
        public void AnUnclosedBlockComesWithTheClosingBraceAsTheFix()
        {
            var d = Assert.Single(MarkdownConverter.Parse("see [kaon]{ws=ceb-x-old", Hosts.Project()).Diagnostics);
            Assert.Equal("{ws=ceb-x-old", d.Observed);
            Assert.Equal("{ws=ceb-x-old}", d.Replacement);
        }

        [Fact]
        public void FindingsAreInSourceOrder()
        {
            var result = MarkdownConverter.Parse("`a`\n\n[x]{wss=1}\n\n<b>\n\n[y]{ws=zzz-unknown}", Hosts.Project());
            Assert.Equal(new[] { 1, 3, 5, 7 }, result.Diagnostics.Select(d => d.Line).ToArray());
        }

        [Fact]
        public void AQuotedValueThatLooksLikeALinkInAnEmptyMarkerRaisesNothing()
        {
            var result = MarkdownConverter.Parse("{empty style=\"Heading 1\" v s2=\"[x](y){a,b} \" s147=mang}", Hosts.Project());
            Assert.Empty(result.Diagnostics);
            Assert.Equal("[x](y){a,b} ", result.Document.Paragraphs[0].Props.StrProps[2]);
        }

        [Fact]
        public void ABrokenBlockHasItsPlaceToo()
        {
            var result = MarkdownConverter.Parse("a\n\n| h | h |\n| --- | --- |\n| [x]{ws=ceb | y |", Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "gdr.syntax.malformed-attribute" && d.Line == 5 && d.Column == 6);
        }
    }
}
