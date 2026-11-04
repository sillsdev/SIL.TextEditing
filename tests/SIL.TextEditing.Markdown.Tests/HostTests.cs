using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>What the host controls: the style names the notation maps to, which styles exist, the default writing systems and the
    /// handling of objects. The library knows no project.</summary>
    public class HostTests
    {
        sealed class Catalog : IStyleCatalog
        {
            public HashSet<string> Paragraph = new HashSet<string> { "Normal", "Heading 1", "Example" };
            public HashSet<string> Character = new HashSet<string> { "Strong", "Hyperlink" };
            public bool IsParagraphStyle(string name) => Paragraph.Contains(name);
            public bool IsCharacterStyle(string name) => Character.Contains(name);
        }

        [Fact]
        public void TheStyleNamesTheNotationMapsToAreTheHosts()
        {
            var options = new MarkdownOptions
            {
                StyleNames = new MarkdownStyleNames { Normal = "Standard", Heading1 = "Titre 1", BulletedList = "Liste", Hyperlink = "Lien" },
            };
            var parsed = MarkdownConverter.Parse("plain\n\n# head\n\n- item\n\n[t](https://x.org)", options);
            Assert.Equal(new[] { "Standard", "Titre 1", "Liste", "Standard" }, parsed.Document.Paragraphs.Select(p => p.Style).ToArray());
            Assert.Equal("Lien", parsed.Document.Paragraphs[3].Runs.Single(r => r.ObjData != null).Props.StrProps[133]);
            Assert.Equal("plain\n\n# head\n\n- item\n\n[t](https://x.org)\n", MarkdownConverter.ToMarkdown(parsed.Document, options));
        }

        [Fact]
        public void AParagraphWithTheDefaultNameIsAFenceWhenTheHostRenamesTheStyle()
        {
            var options = new MarkdownOptions { StyleNames = new MarkdownStyleNames { Normal = "Standard" } };
            var document = B.Doc(B.P("Normal", B.En("x")));
            Assert.Equal("::: {style=Normal}\nx\n:::\n", MarkdownConverter.ToMarkdown(document, options));
        }

        [Fact]
        public void AnUnknownStyleIsReportedByTheCatalog()
        {
            var options = new MarkdownOptions { Styles = new Catalog() };
            var result = MarkdownConverter.Parse("::: {style=Exampel}\nx\n:::\n\n[y]{cs=Stong}\n\n[z]{cs=Strong}\n\n::: {style=Example}\nok\n:::", options);
            Assert.Equal(new[] { "gdr.style.unknown", "gdr.style.unknown" }, result.Diagnostics.Select(d => d.Id).ToArray());
            Assert.Equal(1, result.Diagnostics[0].Line);
        }

        [Fact]
        public void TheDefaultsAreTheHostsWritingSystems()
        {
            var options = new MarkdownOptions { WritingSystems = new FixedWritingSystems("fr", "mg") };
            var parsed = MarkdownConverter.Parse("a [b]{v} [c]{ws=en}", options);
            Assert.Equal(new[] { "fr", "mg", "fr", "en" }, parsed.Document.Paragraphs[0].Runs.Select(r => r.Ws).ToArray());
            Assert.Equal("a [b]{v} [c]{ws=en}\n", MarkdownConverter.ToMarkdown(parsed.Document, options));
        }

        [Fact]
        public void WithoutAHostEveryObjectIsGenericExceptExternalPaths()
        {
            var document = B.Doc(B.P("Normal",
                B.R("en", "￼", null, TestObjects.PictureData(new System.Guid("99999999-8888-7777-6666-555555555555"))),
                B.R("en", "see", p => p.SetStr(133, "Hyperlink"), TestObjects.LinkData("w", new System.Guid("11111111-2222-3333-4444-555555555555"))),
                B.R("en", "web", p => p.SetStr(133, "Hyperlink"), TestObjects.ExternalPath + "https://x.org")));
            string text = MarkdownConverter.ToMarkdown(document);
            Assert.StartsWith("[&#xFFFC;]{s6=", text);
            Assert.Contains("[see](silfw://localhost/link?tool=lexiconEdit&guid=11111111-2222-3333-4444-555555555555)", text);
            Assert.Contains("[web](https://x.org)", text);
            var back = MarkdownConverter.Parse(text);
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs));
        }

        [Fact]
        public void ANameFormNeedsAKindTheHostKnows()
        {
            var parsed = MarkdownConverter.Parse("[[w:palit]]");
            Assert.Empty(parsed.Diagnostics);
            Assert.Equal("[[w:palit]]", parsed.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void TheHostSeesTheDescriptionOfEachLink()
        {
            var seen = new List<MarkdownObject>();
            var spy = new Spy(seen);
            var document = B.Doc(B.P("Normal",
                B.R("en", "palit", p => p.SetStr(133, "Hyperlink"), TestObjects.LinkData("w", new System.Guid("11111111-2222-3333-4444-555555555555")))));
            string text = MarkdownConverter.ToMarkdown(document, new MarkdownOptions { Objects = spy });
            Assert.Equal("[[w:palit]]{guid=11111111-2222-3333-4444-555555555555 tool=oddEdit}\n", text);
            Assert.Equal("palit", seen.Single().Name);
        }

        sealed class Spy : IObjectMapper
        {
            readonly List<MarkdownObject> _seen;
            public Spy(List<MarkdownObject> seen) { _seen = seen; }

            public MarkdownObject? Describe(string objData)
            {
                var link = new MarkdownObject
                {
                    Kind = MarkdownObjectKind.InternalLink, LinkKind = "w", Name = "palit", Guid = new System.Guid("11111111-2222-3333-4444-555555555555"), Tool = "oddEdit",
                };
                _seen.Add(link);
                return link;
            }

            public bool IsKnownLinkKind(string kind) => kind == "w";
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
            {
                objData = TestObjects.LinkData("w", new System.Guid("11111111-2222-3333-4444-555555555555"));
                problem = null;
                return true;
            }
        }

        [Fact]
        public void AnOptionThatMustBeThereRefusesNull()
        {
            var options = new MarkdownOptions();
            Assert.Throws<ArgumentNullException>(() => options.WritingSystems = null!);
            Assert.Throws<ArgumentNullException>(() => options.StyleNames = null!);
            Assert.Throws<ArgumentNullException>(() => options.Objects = null!);
            // The catalog is optional: null accepts every style name.
            options.Styles = null;
            Assert.Null(options.Styles);
            // The options keep what they had when a null is refused.
            Assert.Equal("en", options.WritingSystems.DefaultAnalysis);
            Assert.Equal("Normal", options.StyleNames.Normal);
            Assert.Same(DefaultObjectMapper.Instance, options.Objects);
        }

        [Fact]
        public void AStyleNameThatMustBeThereRefusesNull()
        {
            var names = new MarkdownStyleNames();
            Assert.Throws<ArgumentNullException>(() => names.Normal = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading1 = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading2 = null!);
            Assert.Throws<ArgumentNullException>(() => names.Heading3 = null!);
            Assert.Throws<ArgumentNullException>(() => names.BulletedList = null!);
            Assert.Throws<ArgumentNullException>(() => names.NumberedList = null!);
            Assert.Throws<ArgumentNullException>(() => names.BlockQuote = null!);
            Assert.Throws<ArgumentNullException>(() => names.TableHeader = null!);
            Assert.Throws<ArgumentNullException>(() => names.TableRow = null!);
            Assert.Throws<ArgumentNullException>(() => names.Hyperlink = null!);
            Assert.Equal("Heading 3", names.Heading3);
        }

        [Fact]
        public void ADiagnosticFromTheHostIsPlacedAtTheMarkup()
        {
            var result = MarkdownConverter.Parse("a\n\nsee [[w:palit]] here", new MarkdownOptions { Objects = new Refusing() });
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("gdr.link.not-found", d.Id);
            Assert.Equal(3, d.Line);
            Assert.Equal(5, d.Column);
            Assert.Equal("[[w:palit]]", d.Observed);
            Assert.Contains("no entry palit", d.Message);
        }

        sealed class Refusing : IObjectMapper
        {
            public MarkdownObject? Describe(string objData) => null;
            public bool IsKnownLinkKind(string kind) => true;
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
            {
                objData = string.Empty;
                problem = "no entry " + target.Name;
                return false;
            }
        }
    }
}
