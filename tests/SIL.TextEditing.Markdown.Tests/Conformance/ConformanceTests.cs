using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>Runs the JSON conformance fixtures in tests/conformance (README.md there): Markdown in, expected paragraphs and findings out, and the model to
    /// the canonical Markdown. The fixtures and the spec define the format; this library has to pass every one.</summary>
    public class ConformanceTests
    {
        public static IEnumerable<object[]> Cases() => ConformanceLoader.All().Select(c => new object[] { c.Id });

        [Theory]
        [MemberData(nameof(Cases))]
        public void FixtureCase(string id) => Run(ConformanceLoader.Find(id));

        [Fact]
        public void EveryFindingIdHasAPositiveAndANegativeCase()
        {
            // The ids the library reports that a fixture can express without a host's help, each with a case that expects it and a case that reads the
            // construct correctly (no finding of that id).
            var ids = new[]
            {
                "fwmd.version.unsupported", "fwmd.syntax.unclosed-mark", "fwmd.syntax.unknown-attribute", "fwmd.syntax.duplicate-attribute", "fwmd.syntax.malformed-attribute",
                "fwmd.syntax.too-deep", "fwmd.syntax.unsupported-markup", "fwmd.ws.unknown", "fwmd.link.not-found", "fwmd.link.ambiguous", "fwmd.link.broken",
                "fwmd.link.guid-mismatch", "fwmd.link.relative-without-base", "fwmd.link.stale-path", "fwmd.link.rebound", "fwmd.link.url-rebuilt", "fwmd.style.unknown",
                "fwmd.heading.too-deep", "fwmd.table.ragged", "fwmd.table.tab-in-cell", "fwmd.table.alignment", "fwmd.picture.unknown", "fwmd.field.block-syntax", "fwmd.prop.unknown-number",
            };
            var all = ConformanceLoader.All();
            var missing = new List<string>();
            foreach (string id in ids)
            {
                bool positive = all.Any(c => c.IsRead && c.Findings.Any(f => f.Id == id));
                bool negative = all.Any(c => c.IsRead && !c.Findings.Any(f => f.Id == id) && c.Name.StartsWith("ok: " + id, StringComparison.Ordinal));
                if (!positive || !negative) missing.Add(id + (positive ? string.Empty : " (no positive case)") + (negative ? string.Empty : " (no negative case, named \"ok: " + id + " ...\")"));
            }
            Assert.True(missing.Count == 0, string.Join("\n", missing));
        }

        // ---------------------------------------------------------------- the runner

        internal static MarkdownOptions OptionsFor(ConformanceCase c, out FixtureHost host)
        {
            host = FixtureHost.From(c.Project);
            var options = new MarkdownOptions
            {
                WritingSystems = host,
                Objects = host,
                Paths = host,
                Styles = host.HasStyles ? host : null,
                WriteFrontMatter = c.ExplicitFrontMatter,
            };
            if (c.Options != null)
            {
                string? form = Json.OptStr(c.Options, "linkForm");
                if (form != null) options.LinkForm = form == "B" ? MarkdownLinkForm.FileRelative : form == "C" ? MarkdownLinkForm.SilfwUrl : form == "D" ? MarkdownLinkForm.MotifUrl : form == "E" ? MarkdownLinkForm.ObjectAlias : MarkdownLinkForm.RootPath;
                options.LinkBase = Json.OptStr(c.Options, "linkBase");
                options.ProjectName = Json.OptStr(c.Options, "projectName");
                options.NormalizeText = Json.OptBool(c.Options, "normalize", true);
                if (Json.OptStr(c.Options, "unknownGuids") == "rebind") options.UnknownGuids = UnknownGuidPolicy.RebindByPath;
            }
            return options;
        }

        static void Run(ConformanceCase c)
        {
            var options = OptionsFor(c, out _);
            if (c.IsRead) RunRead(c, options);
            else RunWrite(c, options);
        }

        /// <summary>A multi-string field (spec 5.1): inline grammar only, one paragraph's runs, no front matter. The paragraph of the fixture holds the runs.</summary>
        static void RunInline(ConformanceCase c, MarkdownOptions options)
        {
            var result = MarkdownConverter.ParseInline(c.Markdown!, options);
            AssertFindings(c, result.Diagnostics);
            if (c.Paragraphs != null)
            {
                var read = new Paragraph { Style = c.Paragraphs[0].Style };
                foreach (var run in result.Runs) read.Runs.Add(run.Clone());
                read.DefaultWs = read.Runs.Count > 0 ? read.Runs[0].Ws : c.Paragraphs[0].DefaultWs;
                var expected = c.Paragraphs[0].Clone();
                expected.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                read.Props.StrProps.Remove(TextPropertyNumbers.NamedStyle);
                AssertParagraphs(new[] { c.Paragraphs[0] }, new[] { read }, "ParseInline");
            }
            if (c.CanonicalSpecified && !result.HasErrors)
            {
                string written = MarkdownConverter.ToInlineMarkdown(result.Runs, options);
                Assert.Equal(c.Canonical ?? c.Markdown!, written);
            }
        }

        static void RunRead(ConformanceCase c, MarkdownOptions options)
        {
            if (c.Inline) { RunInline(c, options); return; }
            string full = c.FullMarkdown(c.Markdown!);
            var result = MarkdownConverter.Parse(full, options);
            AssertFindings(c, result.Diagnostics);
            if (c.Paragraphs != null) AssertParagraphs(c.Paragraphs, result.Document.Paragraphs, "Parse");
            if (c.CanonicalSpecified && !result.HasErrors)
            {
                string written = MarkdownConverter.ToMarkdown(result.Document, options);
                string expected = c.Canonical ?? c.Markdown!;
                Assert.Equal(expected, written);
                // Canonical is idempotent: writing what was written changes nothing.
                var again = MarkdownConverter.Parse(c.FullMarkdown(written), options);
                Assert.Equal(written, MarkdownConverter.ToMarkdown(again.Document, options));
            }
        }

        static void AssertFindings(ConformanceCase c, IReadOnlyList<MarkdownDiagnostic> diagnostics)
        {
            int shift = c.ExplicitFrontMatter ? 0 : ConformanceCase.StandardFrontMatterLines;
            var actual = diagnostics.Select(d => new ExpectedFinding
            {
                Id = d.Id,
                Severity = d.Severity == MarkdownSeverity.Error ? "error" : d.Severity == MarkdownSeverity.Warning ? "warning" : "suggestion",
                Line = d.Line - shift, Column = d.Column, EndLine = d.EndLine - shift, EndColumn = d.EndColumn,
            }).ToList();
            var expectedText = c.Findings.Select(f => f.ToString()).ToList();
            var actualText = actual.Select(f => f.ToString()).ToList();
            Assert.True(expectedText.SequenceEqual(actualText), "Findings differ.\nExpected:\n" + string.Join("\n", expectedText) + "\nActual:\n" + string.Join("\n", actualText) + "\nMessages:\n" + string.Join("\n", diagnostics.Select(d => d.Id + " [" + d.Severity + "] " + d.Message)));
            for (int i = 0; i < c.Findings.Count; i++)
            {
                var e = c.Findings[i];
                var d = diagnostics[i];
                if (e.HasFix) Assert.Equal(e.Fix, d.Replacement);
                if (e.Choices != null) Assert.Equal(e.Choices, d.Choices.ToList());
                if (e.Nearest != null) Assert.Equal(e.Nearest, d.Nearest.ToList());
            }
        }

        static void RunWrite(ConformanceCase c, MarkdownOptions options)
        {
            Assert.NotNull(c.Paragraphs);
            Assert.True(c.CanonicalSpecified && c.Canonical != null, "A case with no markdown needs a canonical string.");
            var document = new Document();
            document.Paragraphs.AddRange(c.Paragraphs!.Select(p => p.Clone()));
            string written = MarkdownConverter.ToMarkdown(document, options);
            Assert.Equal(c.Canonical, written);
            var back = MarkdownConverter.Parse(c.FullMarkdown(written), options);
            Assert.False(back.HasErrors, "The canonical text has errors: " + string.Join("; ", back.Diagnostics.Select(d => d.ToString())));
            AssertParagraphs(c.Paragraphs!, back.Document.Paragraphs, "Reading the canonical text");
        }

        internal static void AssertParagraphs(IReadOnlyList<Paragraph> expected, IReadOnlyList<Paragraph> actual, string what)
        {
            string e = string.Join("\n", expected.Select(ModelDump.Of)), a = string.Join("\n", actual.Select(ModelDump.Of));
            if (expected.Count != actual.Count) Assert.Fail(what + ": " + expected.Count + " paragraphs expected, " + actual.Count + " read.\nExpected:\n" + e + "\nActual:\n" + a);
            for (int i = 0; i < expected.Count; i++)
                if (!MarkdownConverter.AreEquivalent(expected[i], actual[i]))
                    Assert.Fail(what + ": paragraph " + (i + 1) + " differs.\nExpected: " + ModelDump.Of(expected[i]) + "\nActual:   " + ModelDump.Of(actual[i]));
        }
    }
}
