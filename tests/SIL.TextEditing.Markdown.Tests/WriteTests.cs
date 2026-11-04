using System;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>The canonical spelling of each construct (spec 5): one line per paragraph, minimal escaping, one spelling per value.</summary>
    public class WriteTests
    {
        static readonly Guid Entry = new Guid("11111111-2222-3333-4444-555555555555");
        static readonly Guid Rule = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        static readonly Guid Picture = new Guid("99999999-8888-7777-6666-555555555555");

        static TestObjects Objects()
        {
            var objects = new TestObjects();
            objects.Names[Entry] = "palit";
            objects.Names[Rule] = "Nasal";
            objects.Pictures.Add(Picture);
            return objects;
        }

        static string Write(params Paragraph[] paragraphs) => MarkdownConverter.ToMarkdown(B.Doc(paragraphs), Hosts.Project(Objects()));

        static string Write(Run run) => Write(B.P("Normal", run));

        // ------------------------------------------------------------ blocks

        [Fact]
        public void NoParagraphsIsNoText() => Assert.Equal("", Write());

        [Fact]
        public void ParagraphsAreSeparatedByOneBlankLine() => Assert.Equal("one\n\ntwo\n", Write(B.Plain("one"), B.Plain("two")));

        [Fact]
        public void HeadingsUseTheHashForms()
            => Assert.Equal("# A\n\n## B\n\n### C\n", Write(B.P("Heading 1", B.En("A")), B.P("Heading 2", B.En("B")), B.P("Heading 3", B.En("C"))));

        [Fact]
        public void AdjacentListParagraphsAreOneTightList()
            => Assert.Equal("- a\n- b\n\n1. c\n2. d\n", Write(B.P("Bulleted List", B.En("a")), B.P("Bulleted List", B.En("b")), B.P("Numbered List", B.En("c")), B.P("Numbered List", B.En("d"))));

        [Fact]
        public void EachBlockQuoteParagraphIsItsOwnBlock()
            => Assert.Equal("> q\n\n> r\n", Write(B.P("Block Quote", B.En("q")), B.P("Block Quote", B.En("r"))));

        [Fact]
        public void EmptyParagraphsAreMarkers()
            => Assert.Equal("{empty}\n\n{empty style=\"Heading 2\"}\n\n{empty v}\n\n{empty ws=ar}\n\n{empty style=\"\"}\n",
                Write(B.Empty(), B.Empty("Heading 2"), B.Empty("Normal", "ceb"), B.Empty("Normal", "ar"), B.Empty("")));

        [Fact]
        public void AnEmptyParagraphKeepsItsProperties()
        {
            var paragraph = B.Empty().WithProps(p => p.SetInt(128, 1));
            paragraph.Rtl = true;
            Assert.Equal("{empty rightToLeft=1}\n", Write(paragraph));
        }

        [Fact]
        public void ARunOfParagraphsWithOneStyleAndPropertiesIsOneFence()
            => Assert.Equal("::: {style=Example}\none\ntwo\n:::\n\n::: {style=Other}\nthree\n:::\n",
                Write(B.P("Example", B.En("one")), B.P("Example", B.En("two")), B.P("Other", B.En("three"))));

        [Fact]
        public void ANormalParagraphWithOnlyPropertiesIsAFenceWithoutAStyle()
            => Assert.Equal("::: {fontsize=12000}\nx\n:::\n", Write(B.P("Normal", B.En("x")).WithProps(p => p.SetInt(6, 12000, 1))));

        [Fact]
        public void AHeadingWithExtraPropertiesIsAFenceWithTheStyle()
            => Assert.Equal("::: {style=\"Heading 2\" align=center}\nx\n:::\n", Write(B.P("Heading 2", B.En("x")).WithProps(p => p.SetInt(17, 2, 3))));

        [Fact]
        public void AParagraphWithNoStyleNameIsAFenceWithAnEmptyStyle()
            => Assert.Equal("::: {style=\"\"}\nx\n:::\n", Write(B.P("", B.En("x"))));

        [Fact]
        public void ParagraphsWithDifferentPropertiesAreDifferentFences()
            => Assert.Equal("::: {style=S fontsize=1}\na\n:::\n\n::: {style=S fontsize=2}\nb\n:::\n",
                Write(B.P("S", B.En("a")).WithProps(p => p.SetInt(6, 1, 1)), B.P("S", B.En("b")).WithProps(p => p.SetInt(6, 2, 1))));

        [Fact]
        public void AnEmptyParagraphBreaksAFence()
            => Assert.Equal("::: {style=S}\na\n:::\n\n{empty style=S}\n\n::: {style=S}\nb\n:::\n", Write(B.P("S", B.En("a")), B.Empty("S"), B.P("S", B.En("b"))));

        [Fact]
        public void ARightToLeftParagraphIsAFenceWithTheProperty()
        {
            var paragraph = B.P("Normal", B.En("x"));
            paragraph.Rtl = true;
            Assert.Equal("::: {rightToLeft=1}\nx\n:::\n", Write(paragraph));
        }

        // ------------------------------------------------------------ tables

        [Fact]
        public void ATableIsAHeaderADelimiterAndRows()
            => Assert.Equal("| Name | Gloss |\n| --- | --- |\n| mang | eat |\n| kaon |  |\n",
                Write(B.P("GDR Table Header", B.En("Name\tGloss")), B.P("GDR Table Row", B.En("mang\teat")), B.P("GDR Table Row", B.En("kaon\t"))));

        [Fact]
        public void ARowWithoutAHeaderIsAnOrphanFence()
            => Assert.Equal("::: {style=\"GDR Table Row\"}\na&#x9;b\n:::\n", Write(B.P("GDR Table Row", B.En("a\tb"))));

        [Fact]
        public void ARowWithTheWrongNumberOfCellsEndsTheTable()
            => Assert.Equal("| A | B |\n| --- | --- |\n| 1 | 2 |\n\n::: {style=\"GDR Table Row\"}\n3\n:::\n",
                Write(B.P("GDR Table Header", B.En("A\tB")), B.P("GDR Table Row", B.En("1\t2")), B.P("GDR Table Row", B.En("3"))));

        [Fact]
        public void AHeaderInTheMiddleStartsANewTable()
            => Assert.Equal("| A |\n| --- |\n| 1 |\n\n| B |\n| --- |\n| 2 |\n",
                Write(B.P("GDR Table Header", B.En("A")), B.P("GDR Table Row", B.En("1")), B.P("GDR Table Header", B.En("B")), B.P("GDR Table Row", B.En("2"))));

        [Fact]
        public void APipeInACellIsEscaped()
            => Assert.Equal("| a\\|b |\n| --- |\n", Write(B.P("GDR Table Header", B.En("a|b"))));

        [Fact]
        public void ACellWithALineSeparatorMakesTheRowAFenceLine()
            => Assert.Equal("| A |\n| --- |\n\n::: {style=\"GDR Table Row\"}\nx\\\ny\n:::\n",
                Write(B.P("GDR Table Header", B.En("A")), B.P("GDR Table Row", B.En("x\u2028y"))));

        [Fact]
        public void ARunBoundaryInsideACellKeepsItsFormat()
            => Assert.Equal("| **bold** plain | x |\n| --- | --- |\n", Write(B.P("GDR Table Header", B.BoldRun("en", "bold"), B.En(" plain\tx"))));

        // ------------------------------------------------------------ marks

        [Theory]
        [InlineData("Hello world", "Hello world\n")]
        [InlineData("[ng] and [+nasal] and {a,b} and /a/ and C -> [+nasal] / _V", "[ng] and [+nasal] and {a,b} and /a/ and C -> [+nasal] / _V\n")]
        [InlineData("*ngoy and *ngoy", "*ngoy and *ngoy\n")]
        [InlineData("a_b_c", "a_b_c\n")]
        public void TextNeedsNoEscapeUnlessItWouldChangeMeaning(string text, string expected) => Assert.Equal(expected, Write(B.Plain(text)));

        [Theory]
        [InlineData("*word*", "\\*word*\n")]
        [InlineData("**word**", "\\*\\*word**\n")]
        [InlineData("a <b> c", "a \\<b> c\n")]
        [InlineData("a `b` c", "a \\`b\\` c\n")]
        [InlineData("[ng]{v}", "\\[ng]{v}\n")]
        [InlineData("[[w:palit]]", "\\[[w:palit]]\n")]
        [InlineData("see [x](y)", "see \\[x](y)\n")]
        [InlineData("[ng]: foo", "\\[ng]: foo\n")]
        [InlineData("# not a heading", "\\# not a heading\n")]
        [InlineData("- not a list", "\\- not a list\n")]
        [InlineData("> not a quote", "\\> not a quote\n")]
        [InlineData("1. not a list", "1\\. not a list\n")]
        [InlineData("{empty}", "\\{empty}\n")]
        [InlineData(":::", ":::\n")]
        [InlineData("a\\", "a\\\\\n")]
        [InlineData("\\*", "\\\\*\n")]
        [InlineData("&amp; &#x20;", "\\&amp; \\&#x20;\n")]
        [InlineData("a ![b](c)", "a !\\[b](c)\n")]
        [InlineData("---", "\\---\n")]
        public void OnlyTheEscapesThatAreNeededAreWritten(string text, string expected) => Assert.Equal(expected, Write(B.Plain(text)));

        [Theory]
        [InlineData(" a", "&#x20;a\n")]
        [InlineData("a ", "a&#x20;\n")]
        [InlineData("a  b", "a&#x20;&#x20;b\n")]
        [InlineData("a b", "a b\n")]
        [InlineData(" ", "&#x20;\n")]
        [InlineData("a\u00A0b", "a&#xA0;b\n")]
        [InlineData("a\tb", "a&#x9;b\n")]
        [InlineData("a\u2003b", "a&#x2003;b\n")]
        [InlineData("a\uFFFCb", "a&#xFFFC;b\n")]
        [InlineData("a\u0000b", "a&#x0;b\n")]
        [InlineData("a\r\nb", "a&#xD;&#xA;b\n")]
        [InlineData("caf\u00e9 \U0001F600", "caf\u00e9 \U0001F600\n")]
        public void WhitespaceAndInvisibleCharactersAreExact(string text, string expected) => Assert.Equal(expected, Write(B.Plain(text)));

        [Fact]
        public void ALoneSurrogateIsANumericReference() => Assert.Equal("a&#xD800;b\n", Write(B.Plain("a" + (char)0xD800 + "b")));

        [Fact]
        public void ALineSeparatorInsideAParagraphIsABackslashLineEnd() => Assert.Equal("one\\\ntwo\n", Write(B.Plain("one\u2028two")));

        [Theory]
        [InlineData("\u2028a", "&#x2028;a\n")]
        [InlineData("a\u2028", "a&#x2028;\n")]
        [InlineData("a\u2028\u2028b", "a&#x2028;&#x2028;b\n")]
        public void ALineSeparatorAtAnEdgeIsANumericReference(string text, string expected) => Assert.Equal(expected, Write(B.Plain(text)));

        [Fact]
        public void TheSecondLineOfABrokenParagraphIsALineStartToo()
            => Assert.Equal("one\\\n&#x20;two\n\ntwo\\\n\\# x\n", Write(B.Plain("one\u2028 two"), B.Plain("two\u2028# x")));

        [Fact]
        public void AListItemContinuationIsIndentedAndAQuoteContinuationIsQuoted()
            => Assert.Equal("- a\\\n  b\n\n> c\\\n> d\n", Write(B.P("Bulleted List", B.En("a\u2028b")), B.P("Block Quote", B.En("c\u2028d"))));

        [Fact]
        public void AHeadingWithALineSeparatorIsAFence()
            => Assert.Equal("::: {style=\"Heading 1\"}\na\\\nb\n:::\n", Write(B.P("Heading 1", B.En("a\u2028b"))));

        [Fact]
        public void ATrailingHashSequenceInAHeadingIsEscaped() => Assert.Equal("# C \\#\n", Write(B.P("Heading 1", B.En("C #"))));

        [Fact]
        public void BoldAndItalicUseStarsWhereCommonMarkAcceptsThem()
            => Assert.Equal("a **bold** and *italic* and ***both***\n",
                Write(B.P("Normal", B.En("a "), B.BoldRun("en", "bold"), B.En(" and "), B.ItalicRun("en", "italic"), B.En(" and "),
                    B.R("en", "both", p => p.SetInt(3, 1, 3).SetInt(2, 1, 3)))));

        [Fact]
        public void BoldOnARunWithPunctuationOrSpaceUsesTheAttribute()
            => Assert.Equal("[mang-]{bold=on} and [ x]{bold=on} and [y ]{italic=on} and [.]{bold=on}\n",
                Write(B.P("Normal", B.BoldRun("en", "mang-"), B.En(" and "), B.BoldRun("en", " x"), B.En(" and "), B.ItalicRun("en", "y "), B.En(" and "), B.BoldRun("en", "."))));

        [Fact]
        public void OffAndInvertAreAttributesOnly()
            => Assert.Equal("[a]{bold=off} [b]{italic=invert}\n",
                Write(B.P("Normal", B.R("en", "a", p => p.SetInt(3, 0, 3)), B.En(" "), B.R("en", "b", p => p.SetInt(2, 2, 3)))));

        [Fact]
        public void AdjacentMarksThatCommonMarkWouldMergeFallBackToAttributes()
        {
            string text = Write(B.P("Normal", B.BoldRun("en", "a"), B.R("en", "b", p => p.SetInt(3, 1, 3).SetInt(2, 1, 3)), B.ItalicRun("en", "c")));
            var back = MarkdownConverter.Parse(text, Hosts.Project(Objects())).Document.Paragraphs[0];
            Assert.Equal("Normal| en\"a\"{p3=1:3} en\"b\"{p2=1:3,p3=1:3} en\"c\"{p2=1:3}", ModelDump.Of(back));
        }

        // ------------------------------------------------------------ writing systems and styles

        [Fact]
        public void TheDefaultVernacularIsV() => Assert.Equal("a [mang]{v} b\n", Write(B.P("Normal", B.En("a "), B.R("ceb", "mang"), B.En(" b"))));

        [Fact]
        public void AnyOtherWritingSystemIsWs() => Assert.Equal("[kaon]{ws=ceb-x-old}\n", Write(B.R("ceb-x-old", "kaon")));

        [Fact]
        public void BoldOnVernacularWithPlainEdgesHugsTheText() => Assert.Equal("[**mang**]{v}\n", Write(B.BoldRun("ceb", "mang")));

        [Fact]
        public void ACharacterStyleIsCs() => Assert.Equal("[mang-]{v cs=\"Emphasized Text\"}\n", Write(B.Styled("ceb", "mang-", "Emphasized Text")));

        [Fact]
        public void AStyleNamedNoneIsWrittenAsTheNumberedProperty() => Assert.Equal("[x]{s133=none}\n", Write(B.Styled("en", "x", "none")));

        [Fact]
        public void AttributesAreInCanonicalOrder()
            => Assert.Equal("[-x-]{v cs=Strong bold=on fontsize=14000 italic=on p999=\"5:7\" s2=Old}\n",
                Write(B.R("ceb", "-x-", p => p.SetStr(133, "Strong").SetInt(3, 1, 3).SetInt(2, 1, 3).SetInt(6, 14000, 1).SetInt(999, 5, 7).SetStr(2, "Old"))));

        [Theory]
        [InlineData(6, 14000, 1, "fontsize=14000")]
        [InlineData(6, 150, 2, "fontsize=150 fontsizeUnit=rel")]
        [InlineData(6, 150, 0, "p6=\"150:default\"")]
        [InlineData(4, 2, 3, "superscript=sub")]
        [InlineData(4, 7, 3, "p4=\"7:enum\"")]
        [InlineData(4, 1, 0, "p4=\"1:default\"")]
        [InlineData(5, 4, 3, "underline=double")]
        [InlineData(8, 255, 0, "forecolor=ff0000")]
        [InlineData(8, 16777215, 0, "forecolor=ffffff")]
        [InlineData(9, -1073741824, 0, "backcolor=transparent")]
        [InlineData(8, 16777216, 0, "p8=\"16777216:default\"")]
        [InlineData(8, 255, 3, "p8=\"255:enum\"")]
        [InlineData(24, -12000, 1, "lineHeight=12000 lineHeightType=exact")]
        [InlineData(24, 12000, 1, "lineHeight=12000")]
        [InlineData(24, 12000, 2, "lineHeight=12000 lineHeightUnit=rel")]
        [InlineData(24, int.MinValue, 1, "p24=\"-2147483648:mpt\"")]
        [InlineData(50, 100, 1, "MarginTop=100")]
        [InlineData(128, 1, 0, "rightToLeft=1")]
        [InlineData(155, 0, 3, "editable=no")]
        [InlineData(3, 1, 15, "p3=\"1:ninch\"")]
        [InlineData(3, 1, 9, "p3=\"1:9\"")]
        [InlineData(7, -500, 1, "offset=-500")]
        public void NamedKeysAndTheirFallbacks(int number, int value, int variation, string expected)
            => Assert.Equal("[x]{" + expected + "}\n", Write(B.R("en", "x", p => p.SetInt(number, value, variation))));

        [Fact]
        public void StringPropertiesUseNamedKeysOrS()
            => Assert.Equal("[x]{fontFamily=\"Doulos SIL\" s7=custom s999=v}\n",
                Write(B.R("en", "x", p => p.SetStr(1, "Doulos SIL").SetStr(7, "custom").SetStr(999, "v"))));

        // ------------------------------------------------------------ objects

        static string Hyperlink(string kind, Guid guid) => TestObjects.LinkData(kind, guid);

        [Fact]
        public void ALinkWhoseTextIsTheTargetsNameUsesTheNameForm()
            => Assert.Equal("[[w:palit]]{guid=" + Entry + "}\n", Write(B.R("en", "palit", p => p.SetStr(133, "Hyperlink"), Hyperlink("w", Entry))));

        [Fact]
        public void ALinkWithOtherTextUsesTheTextForm()
            => Assert.Equal("[see this rule]{link=\"rule:Nasal\" guid=" + Rule + "}\n", Write(B.R("en", "see this rule", p => p.SetStr(133, "Hyperlink"), Hyperlink("rule", Rule))));

        [Fact]
        public void ALinkWithoutACharacterStyleSaysCsNone()
            => Assert.Equal("[[w:palit]]{cs=none guid=" + Entry + "}\n", Write(B.R("en", "palit", null, Hyperlink("w", Entry))));

        [Fact]
        public void ALinkWithAnotherStyleSaysCs()
            => Assert.Equal("[[w:palit]]{cs=Strong guid=" + Entry + "}\n", Write(B.R("en", "palit", p => p.SetStr(133, "Strong"), Hyperlink("w", Entry))));

        [Fact]
        public void ALinkCanBeInAnotherWritingSystem()
            => Assert.Equal("[[w:palit]]{v guid=" + Entry + "}\n", Write(B.R("ceb", "palit", p => p.SetStr(133, "Hyperlink"), Hyperlink("w", Entry))));

        [Fact]
        public void AnExternalLinkIsParenthesisForm()
            => Assert.Equal("see [here](https://example.org/a?b=1&c=2).\n",
                Write(B.P("Normal", B.En("see "), B.R("en", "here", p => p.SetStr(133, "Hyperlink"), TestObjects.ExternalPath + "https://example.org/a?b=1&c=2"), B.En("."))));

        [Fact]
        public void AnExternalLinkWithAnUrlThatCannotBeBareUsesTheUrlAttribute()
            => Assert.Equal("[t]{url=\"a b)\"}\n", Write(B.R("en", "t", p => p.SetStr(133, "Hyperlink"), TestObjects.ExternalPath + "a b)")));

        [Fact]
        public void AnExternalLinkTakesBoldAsAnAttribute()
            => Assert.Equal("[t](https://x.org){bold=on}\n", Write(B.R("en", "t", p => p.SetStr(133, "Hyperlink").SetInt(3, 1, 3), TestObjects.ExternalPath + "https://x.org")));

        [Fact]
        public void APictureIsAnImageWithTheCaptionFromTheHost()
            => Assert.Equal("![a picture](fw:picture/" + Picture + ")\n", Write(B.R("en", "\uFFFC", null, TestObjects.PictureData(Picture))));

        [Fact]
        public void AnyOtherObjectDataIsTheS6Attribute()
        {
            string data = "\u0001ab\ud800|}\"\\";
            Assert.Equal("[&#xFFFC;]{s6=\"\\u0001ab\\uD800|}\\\"\\\\\"}\n", Write(B.R("en", "\uFFFC", null, data)));
        }

        [Fact]
        public void PipesInQuotedValuesAreEscapedInTableCells()
            => Assert.Equal("| [&#xFFFC;]{s6=\"a\\u007Cb\"} |\n| --- |\n", Write(B.P("GDR Table Header", B.R("en", "\uFFFC", null, "a|b"))));

        [Fact]
        public void ARunWithObjectDataAndOrdinaryTextKeepsBoth()
            => Assert.Equal("[shown]{s6=q}\n", Write(B.R("en", "shown", null, "q")));

        // ------------------------------------------------------------ canonical forms of Markdown

        [Theory]
        [InlineData("a\nb", "a b\n")]
        [InlineData("# A\nB", "# A\n\nB\n")]
        [InlineData("* a\n* b\n\n\n\n1) c", "- a\n- b\n\n1. c\n")]
        [InlineData("[x]{bold=on  v}", "[**x**]{v}\n")]
        [InlineData("[x]{MarginTop=100 ws=ar}", "[x]{ws=ar MarginTop=100}\n")]
        [InlineData("[x]{marginTop=100}", "[x]{MarginTop=100}\n")]
        [InlineData("[x]{p50=12000}", "[x]{p50=\"12000:default\"}\n")]
        [InlineData("[x]{p50=\"12000:mpt\"}", "[x]{MarginTop=12000}\n")]
        [InlineData("[ng]{ws=en}", "ng\n")]
        [InlineData("a   \nb", "a b\n")]
        [InlineData("a\\\nb", "a\\\nb\n")]
        [InlineData("***x***", "***x***\n")]
        [InlineData("_x_ __y__", "*x* **y**\n")]
        [InlineData("[x]{bold=on}", "**x**\n")]
        [InlineData("[[w:palit]]{guid=11111111-2222-3333-4444-555555555555 ws=ceb cs=Hyperlink}", "[[w:palit]]{v guid=11111111-2222-3333-4444-555555555555}\n")]
        public void ReadingThenWritingGivesTheCanonicalText(string markdown, string canonical)
            => Assert.Equal(canonical, MarkdownConverter.Canonicalize(markdown, Hosts.Project(Objects())));
    }
}
