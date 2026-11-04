using System;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Spec 3.2 to 3.4: how inline notation becomes runs.</summary>
    public class ParseInlineTests
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

        static string Dump(string markdown) => ModelDump.Of(MarkdownConverter.Parse(markdown, Hosts.Project(Objects())).Document);
        static string[] Ids(string markdown) => MarkdownConverter.Parse(markdown, Hosts.Project(Objects())).Diagnostics.Select(d => d.Id).ToArray();

        const string Bold = "{p3=1:3}", Italic = "{p2=1:3}";

        [Theory]
        [InlineData("unmarked", "Normal| en\"unmarked\"")]
        [InlineData("[mang-]{v}", "Normal| ceb\"mang-\"")]
        [InlineData("a [mang]{v} b", "Normal| en\"a \" ceb\"mang\" en\" b\"")]
        [InlineData("[kaon]{ws=ceb-x-old}", "Normal| ceb-x-old\"kaon\"")]
        [InlineData("[x]{ws=ar}", "Normal| ar\"x\"")]
        public void WritingSystems(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Theory]
        [InlineData("**b**", "Normal| en\"b\"" + Bold)]
        [InlineData("*i*", "Normal| en\"i\"" + Italic)]
        [InlineData("a **b** c", "Normal| en\"a \" en\"b\"" + Bold + " en\" c\"")]
        [InlineData("***x***", "Normal| en\"x\"{p2=1:3,p3=1:3}")]
        [InlineData("[x]{bold=on}", "Normal| en\"x\"" + Bold)]
        [InlineData("[x]{bold=off}", "Normal| en\"x\"{p3=0:3}")]
        [InlineData("[x]{bold=invert}", "Normal| en\"x\"{p3=2:3}")]
        [InlineData("[x]{italic=on bold=on}", "Normal| en\"x\"{p2=1:3,p3=1:3}")]
        [InlineData("**[mang-]{v}**", "Normal| ceb\"mang-\"" + Bold)]
        [InlineData("[**mang**]{v}", "Normal| ceb\"mang\"" + Bold)]
        [InlineData("[mang-]{v cs=\"Emphasized Text\"}", "Normal| ceb\"mang-\"{s133=Emphasized Text}")]
        [InlineData("[mang-]{v bold=on}", "Normal| ceb\"mang-\"" + Bold)]
        public void MarksAndStyles(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Theory]
        [InlineData("[ng]")]
        [InlineData("[+nasal] and [ng]")]
        [InlineData("{a,b}")]
        [InlineData("/a/ and C -> [+nasal] / _V")]
        [InlineData("a * b")]
        [InlineData("a_b_c")]
        public void CommonLinguisticTextNeedsNoEscapeAndRaisesNoFinding(string markdown)
        {
            Assert.Empty(Ids(markdown));
            Assert.Equal("Normal| en\"" + markdown + "\"", Dump(markdown));
        }

        [Theory]
        [InlineData("a&#x20;b", "Normal| en\"a b\"")]
        [InlineData("&#x20;a&#x20;", "Normal| en\" a \"")]
        [InlineData("a&#xA0;b", "Normal| en\"a\\x00A0b\"")]
        [InlineData("a&#xFFFC;b", "Normal| en\"a\\xFFFCb\"")]
        [InlineData("a&#x0;b", "Normal| en\"a\\x0000b\"")]
        [InlineData("a&#xD800;b", "Normal| en\"a\\xD800b\"")]
        [InlineData("a&#xD;&#xA;b", "Normal| en\"a\\x000D\\x000Ab\"")]
        [InlineData("a&#x1F600;b", "Normal| en\"a\\xD83D\\xDE00b\"")]
        [InlineData("\\&#x20;", "Normal| en\"&#x20;\"")]
        [InlineData("a \\* b \\[c\\] \\{d} \\\\", "Normal| en\"a * b [c] {d} \\\\\"")]
        public void NumericReferencesAndEscapesGiveExactText(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Fact]
        public void RunsOfSpacesAreKept() => Assert.Equal("Normal| en\"a  b\"", Dump("a  b"));

        // ------------------------------------------------------------ links

        static string NameLinkRun(string kind, Guid guid, string text, string extra = "")
            => "en\"" + text + "\"{s133=Hyperlink" + extra + "}<obj " + ModelDump.Escape(TestObjects.LinkData(kind, guid)) + ">";

        [Fact]
        public void TheNameFormReadsTheNameAsTheRunText()
            => Assert.Equal("Normal| " + NameLinkRun("w", Entry, "palit"), Dump("[[w:palit]]{guid=" + Entry + "}"));

        [Fact]
        public void TheTextFormKeepsTheAuthorsText()
            => Assert.Equal("Normal| " + NameLinkRun("rule", Rule, "see this rule"), Dump("[see this rule]{link=rule:Nasal guid=" + Rule + "}"));

        [Fact]
        public void ALinkTakesWritingSystemAndStyleInTheSameBlock()
            => Assert.Equal("Normal| ceb\"palit\"{s133=Strong}<obj " + ModelDump.Escape(TestObjects.LinkData("w", Entry)) + ">",
                Dump("[[w:palit]]{guid=" + Entry + " ws=ceb cs=Strong}"));

        [Fact]
        public void CsNoneMarksALinkWithNoCharacterStyle()
            => Assert.Equal("Normal| en\"palit\"<obj " + ModelDump.Escape(TestObjects.LinkData("w", Entry)) + ">",
                Dump("[[w:palit]]{guid=" + Entry + " cs=none}"));

        [Fact]
        public void ANameWithoutAGuidAsksTheHost()
        {
            var ids = Ids("[[w:palt]]");
            Assert.Equal(new[] { "gdr.link.not-found" }, ids);
        }

        [Fact]
        public void AKindTheHostDoesNotKnowIsPlainText()
        {
            Assert.Empty(Ids("[[Note: x]]"));
            Assert.Equal("Normal| en\"[[Note: x]]\"", Dump("[[Note: x]]"));
        }

        [Fact]
        public void AnExternalLinkCarriesTheUrlAndTheHyperlinkStyle()
            => Assert.Equal("Normal| en\"see \" en\"here\"{s133=Hyperlink}<obj \\x0004https://example.org/a?b=1&c=2> en\".\"",
                Dump("see [here](https://example.org/a?b=1&c=2)."));

        [Fact]
        public void AnExternalLinkTakesAnAttributeBlock()
            => Assert.Equal("Normal| ceb\"t\"{p3=1:3,s133=Hyperlink}<obj \\x0004https://x.org>", Dump("[t](https://x.org){ws=ceb bold=on}"));

        [Fact]
        public void AnExternalLinkCanBeSpelledWithAUrlAttribute()
            => Assert.Equal("Normal| en\"t\"{s133=Hyperlink}<obj \\x0004a b>", Dump("[t]{url=\"a b\"}"));

        [Fact]
        public void AStoredUrlThatDiffersIsKeptVerbatim()
        {
            var parsed = MarkdownConverter.Parse("[[w:palit]]{guid=" + Entry + " url=\"x y\"}", Hosts.Project(new RecordingObjects(Objects())));
            Assert.Empty(parsed.Diagnostics);
        }

        sealed class RecordingObjects : IObjectMapper
        {
            readonly IObjectMapper _inner;
            public RecordingObjects(IObjectMapper inner) { _inner = inner; }
            public MarkdownObject? Describe(string objData) => _inner.Describe(objData);
            public bool IsKnownLinkKind(string kind) => _inner.IsKnownLinkKind(kind);
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
            {
                Assert.Equal("x y", target.Url);
                return _inner.TryCreateObjData(target, out objData, out problem);
            }
        }

        [Fact]
        public void BrokenIsAFlagOnTheLink()
        {
            var seen = new BrokenProbe();
            MarkdownConverter.Parse("[[w:palit]]{guid=" + Entry + " broken}", new MarkdownOptions { Objects = seen });
            Assert.True(seen.Broken);
        }

        sealed class BrokenProbe : IObjectMapper
        {
            public bool Broken;
            public MarkdownObject? Describe(string objData) => null;
            public bool IsKnownLinkKind(string kind) => true;
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem) { Broken = target.Broken; objData = "x"; problem = null; return true; }
        }

        [Fact]
        public void APictureIsAnObjectReplacementRunWithTheHostsData()
            => Assert.Equal("Normal| en\"\\xFFFC\"<obj " + ModelDump.Escape(TestObjects.PictureData(Picture)) + ">", Dump("![a caption](fw:picture/" + Picture + ")"));

        [Fact]
        public void AnUnknownPictureIsReported() => Assert.Equal(new[] { "gdr.picture.unknown" }, Ids("![c](fw:picture/" + Guid.Empty + ")"));

        [Fact]
        public void AnythingOtherThanAPictureUrlInAnImageIsReported() => Assert.Equal(new[] { "gdr.picture.unknown" }, Ids("![c](http://x/y.png)"));

        [Fact]
        public void AnyOtherObjectDataIsTheS6Attribute()
            => Assert.Equal("Normal| en\"\\xFFFC\"<obj \\x0001ab\\xD800>", Dump("[&#xFFFC;]{s6=\"\\u0001ab\\uD800\"}"));

        [Fact]
        public void S6OnTextKeepsTheText() => Assert.Equal("Normal| en\"shown\"<obj q>", Dump("[shown]{s6=q}"));

        // ------------------------------------------------------------ attributes

        [Theory]
        [InlineData("[x]{fontsize=14000}", "Normal| en\"x\"{p6=14000:1}")]
        [InlineData("[x]{fontsize=150 fontsizeUnit=rel}", "Normal| en\"x\"{p6=150:2}")]
        [InlineData("[x]{underline=double superscript=sub}", "Normal| en\"x\"{p4=2:3,p5=4:3}")]
        [InlineData("[x]{forecolor=ff0000}", "Normal| en\"x\"{p8=255:0}")]
        [InlineData("[x]{backcolor=transparent}", "Normal| en\"x\"{p9=-1073741824:0}")]
        [InlineData("[x]{MarginTop=100}", "Normal| en\"x\"{p50=100:1}")]
        [InlineData("[x]{marginTop=100}", "Normal| en\"x\"{p50=100:1}")]
        [InlineData("[x]{lineHeight=12000 lineHeightType=exact}", "Normal| en\"x\"{p24=-12000:1}")]
        [InlineData("[x]{p50=\"12000:mpt\"}", "Normal| en\"x\"{p50=12000:1}")]
        [InlineData("[x]{p50=12000}", "Normal| en\"x\"{p50=12000:0}")]
        [InlineData("[x]{p999=\"5:7\"}", "Normal| en\"x\"{p999=5:7}")]
        [InlineData("[x]{s2=\"Old\" s999=\"v\"}", "Normal| en\"x\"{s2=Old,s999=v}")]
        [InlineData("[x]{fontFamily=\"Doulos SIL\"}", "Normal| en\"x\"{s1=Doulos SIL}")]
        [InlineData("[x]{s133=\"none\"}", "Normal| en\"x\"{s133=none}")]
        public void PropertyKeys(string markdown, string expected) => Assert.Equal(expected, Dump(markdown));

        [Fact]
        public void UnknownNumbersAreKeptWithAWarning()
        {
            var result = MarkdownConverter.Parse("[x]{p999=\"5:7\"}", Hosts.Project());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("gdr.prop.unknown-number", d.Id);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.False(result.HasErrors);
        }

        [Fact]
        public void AnUnknownKeyIsReportedWithTheNearestKey()
        {
            var d = Assert.Single(MarkdownConverter.Parse("[x]{wss=ceb}", Hosts.Project()).Diagnostics);
            Assert.Equal("gdr.syntax.unknown-attribute", d.Id);
            Assert.Contains("ws", d.Message);
            Assert.Equal(new[] { "ws" }, d.Choices);
        }

        [Fact]
        public void ADuplicateKeyIsReported() => Assert.Equal(new[] { "gdr.syntax.duplicate-attribute" }, Ids("[x]{ws=ceb ws=en}"));

        [Fact]
        public void TwoSpellingsOfOnePropertyAreADuplicate() => Assert.Contains("gdr.syntax.duplicate-attribute", Ids("[x]{MarginTop=1 marginTop=2}"));

        [Fact]
        public void AnUnclosedBlockIsReportedAndStaysText()
        {
            var result = MarkdownConverter.Parse("[kaon]{ws=ceb-x-old", Hosts.Project());
            Assert.Equal(new[] { "gdr.syntax.unclosed-mark" }, result.Diagnostics.Select(d => d.Id).ToArray());
            Assert.Equal("Normal| en\"[kaon]{ws=ceb-x-old\"", ModelDump.Of(result.Document));
        }

        [Theory]
        [InlineData("[x]{}")]
        [InlineData("[x]{a,b}")]
        [InlineData("[x]{ws=}")]
        [InlineData("[x]{ws=\"a\\q\"}")]
        [InlineData("[x]{1a=b}")]
        public void AMalformedBlockIsReportedAndStaysText(string markdown)
        {
            var result = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.Contains(result.Diagnostics, d => d.Id == "gdr.syntax.malformed-attribute");
            Assert.Equal("Normal| en\"" + ModelDump.Escape(markdown) + "\"", ModelDump.Of(result.Document));
        }

        [Fact]
        public void AnUnknownWritingSystemIsReported() => Assert.Equal(new[] { "gdr.ws.unknown" }, Ids("[x]{ws=zzz-unknown}"));

        [Fact]
        public void AnUnknownEnumValueIsReported() => Assert.Contains("gdr.syntax.malformed-attribute", Ids("[x]{bold=maybe}"));

        [Fact]
        public void AFlagThatIsNotKnownIsReported() => Assert.Contains("gdr.syntax.unknown-attribute", Ids("[x]{nope}"));

        [Fact]
        public void AKeyThatNeedsAValueIsNotAKnownFlag() => Assert.Contains("gdr.syntax.unknown-attribute", Ids("[x]{bold}"));

        [Fact]
        public void AQuotedValueMayHoldBraceSpacePipeAndEquals()
            => Assert.Equal("Normal| en\"x\"{s2=a} b|c=d}}", Dump("[x]{s2=\"a} b|c=d}\"}"));

        [Fact]
        public void APipeInAQuotedValueDoesNotSplitATableCell()
            => Assert.Equal("GDR Table Header| en\"x\"{s2=a|b}", Dump("| [x]{s2=\"a|b\"} |\n| --- |"));

        [Fact]
        public void InnerSpansOverrideOuterOnes()
            => Assert.Equal("Normal| ceb\"a \" ar\"b\" ceb\" c\"", Dump("[a [b]{ws=ar} c]{v}"));

        [Fact]
        public void AnExplicitAttributeInsideAMarkWins()
            => Assert.Equal("Normal| en\"x\"{p3=0:3}", Dump("**[x]{bold=off}**"));

        [Fact]
        public void PositionsOfFindingsInsideAFenceAreInTheSource()
        {
            var result = MarkdownConverter.Parse("a\n\n::: {style=\"S\"}\nfine\n[x]{wss=1}\n:::", Hosts.Project());
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal(5, d.Line);
            Assert.Equal(5, d.Column);
        }
    }
}
