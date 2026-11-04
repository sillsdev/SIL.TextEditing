using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>What a host can say back to the parser: a structured problem for an object (id, severity, message, choices, nearest), the lists
    /// of its writing systems and styles for the choices and nearest names, and the places of the objects in the result, so the host can put
    /// its own findings on a line and column.</summary>
    public class HostFindingTests
    {
        static readonly Guid Entry = new Guid("11111111-2222-3333-4444-555555555555");

        /// <summary>A host that resolves every object by a script. The old yes/no method must not be used when this interface is there.</summary>
        sealed class ScriptedHost : IObjectMapper, IObjectResolver
        {
            public delegate bool Resolve(MarkdownObject target, out string objData, out MarkdownProblem? problem);

            readonly Resolve _resolve;
            public readonly List<MarkdownObject> Seen = new List<MarkdownObject>();

            public ScriptedHost(Resolve resolve) { _resolve = resolve; }

            public MarkdownObject? Describe(string objData) => null;
            public bool IsKnownLinkKind(string kind) => kind == "w";

            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
                => throw new InvalidOperationException("TryResolve is the way in when the host implements IObjectResolver.");

            public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
            {
                Seen.Add(target);
                return _resolve(target, out objData, out problem);
            }
        }

        static ScriptedHost Host(ScriptedHost.Resolve resolve) => new ScriptedHost(resolve);

        static MarkdownOptions With(IObjectMapper host) => new MarkdownOptions { Objects = host };

        // ------------------------------------------------------------ problems with ids, severities, choices and nearest names

        [Fact]
        public void AnAmbiguousLinkIsReportedWithItsChoicesAtTheLink()
        {
            string[] choices = { "[[w:palit]]{guid=aaaaaaaa-0000-0000-0000-000000000001}", "[[w:palit]]{guid=aaaaaaaa-0000-0000-0000-000000000002}", "[[w:palit]]{guid=aaaaaaaa-0000-0000-0000-000000000003}" };
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkAmbiguous, MarkdownSeverity.Error, "[[w:palit]] matches 3 entries.") { Choices = choices };
                return false;
            });
            var result = MarkdownConverter.Parse("a\n\nsee [[w:palit]] here", With(host));
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.ambiguous", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.Equal("[[w:palit]] matches 3 entries.", d.Message);
            Assert.Equal(choices, d.Choices);
            Assert.Null(d.Replacement);
            Assert.Equal((3, 5, 3, 16), (d.Line, d.Column, d.EndLine, d.EndColumn));
            Assert.Equal("[[w:palit]]", d.Observed);
            Assert.True(result.HasErrors);
            Assert.Equal("see palit here", result.Document.Paragraphs[1].Text);
            Assert.All(result.Document.Paragraphs[1].Runs, r => Assert.Null(r.ObjData));
        }

        [Fact]
        public void ABrokenLinkIsAWarningAndTheLinkIsKept()
        {
            string data = TestObjects.LinkData("w", Entry);
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = data;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The link target was deleted; the text is kept.");
                return true;
            });
            var result = MarkdownConverter.Parse("a\n\n[[w:palit]]{guid=11111111-2222-3333-4444-555555555555 broken}", With(host));
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.link.broken", d.Id);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.Equal((3, 1), (d.Line, d.Column));
            Assert.Equal("[[w:palit]]{guid=11111111-2222-3333-4444-555555555555 broken}", d.Observed);
            Assert.False(result.HasErrors);
            var run = Assert.Single(result.Document.Paragraphs[1].Runs);
            Assert.Equal(data, run.ObjData);
        }

        [Fact]
        public void ALinkThatIsNotFoundCarriesTheNearestNamesAndTheDefaultId()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("[[w:palt]] matches no entry. Nearest: palit, palito.") { Nearest = new[] { "palit", "palito" } };
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse("see [[w:palt]]", With(host)).Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.Equal(new[] { "palit", "palito" }, d.Nearest);
            Assert.Empty(d.Choices);
        }

        [Fact]
        public void APictureThatIsUnknownHasItsOwnDefaultId()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("not a picture of this project");
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse("![c](fw:picture/99999999-8888-7777-6666-555555555555)", With(host)).Diagnostics);
            Assert.Equal("fwmd.picture.unknown", d.Id);
        }

        [Fact]
        public void AHostThatCannotSayWhyStillGetsAFinding()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = null;
                return false;
            });
            var d = Assert.Single(MarkdownConverter.Parse("[t]{link=w:x}", With(host)).Diagnostics);
            Assert.Equal("fwmd.link.not-found", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
        }

        [Fact]
        public void AFailureIsAnErrorWhateverSeverityTheHostGave()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem(null, MarkdownSeverity.Warning, "it is only a warning, but there is no data");
                return false;
            });
            var result = MarkdownConverter.Parse("[t]{link=w:x}", With(host));
            Assert.Equal(MarkdownSeverity.Error, Assert.Single(result.Diagnostics).Severity);
        }

        [Fact]
        public void AReplacementFromTheHostIsTheFix()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem("misspelt") { Replacement = "[[w:palit]]" };
                return false;
            });
            Assert.Equal("[[w:palit]]", Assert.Single(MarkdownConverter.Parse("[[w:palt]]", With(host)).Diagnostics).Replacement);
        }

        [Fact]
        public void HostProblemsAreFoundInTableCellsAndFencesAtTheirPlaces()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = string.Empty;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkAmbiguous, MarkdownSeverity.Error, "ambiguous");
                return false;
            });
            var result = MarkdownConverter.Parse("a\n\n| h | h |\n| --- | --- |\n| x | b [[w:p]] c |\n\n::: {style=S}\nq [t]{link=w:p} r\n:::", With(host));
            Assert.Equal(new[] { (5, 9, "[[w:p]]"), (8, 3, "[t]{link=w:p}") }, result.Diagnostics.Select(d => (d.Line, d.Column, d.Observed!)).ToArray());
        }

        // ------------------------------------------------------------ the host sees where each object is, and so does the result

        [Fact]
        public void TheHostIsToldWhereTheObjectIsWhileItResolvesIt()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = TestObjects.LinkData("w", Entry);
                problem = null;
                return true;
            });
            MarkdownConverter.Parse("a\n\n[[w:palit]] and [see]{link=w:palit guid=11111111-2222-3333-4444-555555555555}\n\n| h |\n| --- |\n| [t](https://x.org) |", With(host));
            var seen = host.Seen;
            Assert.Equal(3, seen.Count);
            Assert.Equal((3, 1, 3, 12), (seen[0].Source!.Line, seen[0].Source!.Column, seen[0].Source!.EndLine, seen[0].Source!.EndColumn));
            Assert.Equal((3, 17), (seen[1].Source!.Line, seen[1].Source!.Column));
            Assert.Equal((7, 3, 7, 21), (seen[2].Source!.Line, seen[2].Source!.Column, seen[2].Source!.EndLine, seen[2].Source!.EndColumn));
        }

        [Fact]
        public void TheResultListsEveryObjectWithItsRangeAndData()
        {
            string link = TestObjects.LinkData("w", Entry);
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                problem = null;
                objData = target.Kind == MarkdownObjectKind.ExternalLink ? TestObjects.ExternalPath + target.Url : link;
                return target.Name != "nope";
            });
            string text = "a\n\n[[w:palit]] [see]{link=w:palit} [t](https://x.org) [[w:nope]]";
            var result = MarkdownConverter.Parse(text, With(host));
            Assert.Equal(4, result.Objects.Count);
            Assert.Equal(new[] { "[[w:palit]]", "[see]{link=w:palit}", "[t](https://x.org)", "[[w:nope]]" },
                result.Objects.Select(o => text.Substring(o.Source!.Start, o.Source.End - o.Source.Start)).ToArray());
            Assert.Equal(new[] { link, link, TestObjects.ExternalPath + "https://x.org", null }, result.Objects.Select(o => o.ObjData).ToArray());
            Assert.Equal(new[] { MarkdownObjectKind.InternalLink, MarkdownObjectKind.InternalLink, MarkdownObjectKind.ExternalLink, MarkdownObjectKind.InternalLink },
                result.Objects.Select(o => o.Kind).ToArray());
            Assert.Equal(text, result.Source);
        }

        [Fact]
        public void TheHostCanPlaceItsOwnFindingOnAnObjectOfTheResult()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = TestObjects.LinkData("w", Entry);
                problem = null;
                return true;
            });
            var result = MarkdownConverter.Parse("a\n\nsee [[w:palit]]{guid=11111111-2222-3333-4444-555555555555} here", With(host));
            var link = Assert.Single(result.Objects);
            var d = result.CreateDiagnostic(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The target was deleted.", link.Source!.Start, link.Source.End);
            Assert.Equal("[[w:palit]]{guid=11111111-2222-3333-4444-555555555555}", d.Observed);
            Assert.Equal((3, 5), (d.Line, d.Column));
            Assert.Equal(3, d.EndLine);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.Empty(result.Diagnostics);
        }

        [Fact]
        public void AnInlineResultHasObjectsAndCreatesFindingsToo()
        {
            var host = Host((MarkdownObject target, out string objData, out MarkdownProblem? problem) =>
            {
                objData = TestObjects.LinkData("w", Entry);
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "deleted");
                return true;
            });
            var result = MarkdownConverter.ParseInline("see [[w:palit]]", With(host));
            Assert.Equal("[[w:palit]]", Assert.Single(result.Diagnostics).Observed);
            var link = Assert.Single(result.Objects);
            var d = result.CreateDiagnostic("x.y", MarkdownSeverity.Suggestion, "m", link.Source!.Start, link.Source.End, replacement: "r", choices: new[] { "c" }, nearest: new[] { "n" });
            Assert.Equal((1, 5, "r"), (d.Line, d.Column, d.Replacement));
            Assert.Equal(new[] { "c" }, d.Choices);
            Assert.Equal(new[] { "n" }, d.Nearest);
        }

        [Fact]
        public void AWriterReadBackMeetsTheHostsWarningsWithoutTrouble()
        {
            // The writer reads what it writes through the same host: a warning for a deleted target must not make the text unreadable.
            string data = TestObjects.LinkData("w", Entry);
            var host = new BrokenLinkHost(data);
            var document = B.Doc(B.P("Normal", B.R("en", "palit", p => p.SetStr(133, "Hyperlink"), data)));
            string markdown = MarkdownConverter.ToMarkdown(document, With(host));
            Assert.Equal("[[w:palit]]{guid=11111111-2222-3333-4444-555555555555 broken}\n", markdown);
            var back = MarkdownConverter.Parse(markdown, With(host));
            Assert.False(back.HasErrors);
            Assert.Equal(MarkdownSeverity.Warning, Assert.Single(back.Diagnostics).Severity);
            Assert.True(MarkdownConverter.AreEquivalent(document.Paragraphs, back.Document.Paragraphs));
        }

        sealed class BrokenLinkHost : IObjectMapper, IObjectResolver
        {
            readonly string _data;
            public BrokenLinkHost(string data) { _data = data; }
            public MarkdownObject? Describe(string objData)
                => objData == _data ? new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, LinkKind = "w", Name = "palit", Guid = Entry, Broken = true } : null;
            public bool IsKnownLinkKind(string kind) => kind == "w";
            public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem) { objData = _data; problem = null; return true; }
            public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
            {
                objData = _data;
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "The link target was deleted; the text is kept.");
                return true;
            }
        }

        // ------------------------------------------------------------ the host's lists of writing systems and styles

        sealed class ListedWritingSystems : IWritingSystemMapper, IKnownWritingSystems
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public IReadOnlyList<string> KnownTags { get; } = new[] { "en", "ceb", "ceb-x-old", "ar" };
            public bool IsKnown(string tag) => KnownTags.Contains(tag);
        }

        sealed class ListedStyles : IStyleCatalog, IKnownStyles
        {
            public IReadOnlyList<string> ParagraphStyleNames { get; } = new[] { "Normal", "Example", "Examples", "Heading 1" };
            public IReadOnlyList<string> CharacterStyleNames { get; } = new[] { "Strong", "Emphasized Text", "Hyperlink" };
            public bool IsParagraphStyle(string name) => ParagraphStyleNames.Contains(name);
            public bool IsCharacterStyle(string name) => CharacterStyleNames.Contains(name);
        }

        [Fact]
        public void AnUnknownWritingSystemOffersTheProjectsSystemsAndTheClosest()
        {
            var options = new MarkdownOptions { WritingSystems = new ListedWritingSystems() };
            var d = Assert.Single(MarkdownConverter.Parse("see [kaon]{ws=cbe}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(new[] { "en", "ceb", "ceb-x-old", "ar" }, d.Choices);
            Assert.Equal(new[] { "ceb" }, d.Nearest);
            Assert.Null(d.Replacement);
        }

        [Fact]
        public void AnUnknownWritingSystemOfAnEmptyMarkerOffersThemToo()
        {
            var options = new MarkdownOptions { WritingSystems = new ListedWritingSystems() };
            var d = Assert.Single(MarkdownConverter.Parse("{empty ws=cbe}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(4, d.Choices.Count);
        }

        [Fact]
        public void AnUnknownWritingSystemWithoutAListHasNoChoices()
        {
            var d = Assert.Single(MarkdownConverter.Parse("see [kaon]{ws=zzz-unknown}", Hosts.Project()).Diagnostics);
            Assert.Empty(d.Choices);
            Assert.Empty(d.Nearest);
        }

        [Fact]
        public void AnUnknownStyleNamesTheNearest()
        {
            var options = new MarkdownOptions { Styles = new ListedStyles() };
            var paragraph = Assert.Single(MarkdownConverter.Parse("::: {style=Exampel}\nx\n:::", options).Diagnostics);
            Assert.Equal("fwmd.style.unknown", paragraph.Id);
            Assert.Equal(new[] { "Example", "Examples" }, paragraph.Nearest);
            Assert.Contains("Nearest: Example, Examples", paragraph.Message);
            var character = Assert.Single(MarkdownConverter.Parse("[x]{cs=Stong}", options).Diagnostics);
            Assert.Equal(new[] { "Strong" }, character.Nearest);
        }

        [Fact]
        public void AnUnknownStyleFarFromEveryNameHasNoNearest()
        {
            var options = new MarkdownOptions { Styles = new ListedStyles() };
            var d = Assert.Single(MarkdownConverter.Parse("::: {style=Zebra}\nx\n:::", options).Diagnostics);
            Assert.Empty(d.Nearest);
            Assert.DoesNotContain("Nearest", d.Message);
        }

        // ------------------------------------------------------------ the host's own problems for writing systems and styles

        /// <summary>A host that judges tags itself. The yes/no method must not be used when the checker is there.</summary>
        sealed class CheckedWritingSystems : IWritingSystemMapper, IWritingSystemChecker
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public bool IsKnown(string tag) => throw new InvalidOperationException("CheckWritingSystem is the way in when the host implements IWritingSystemChecker.");
            public readonly List<string> Asked = new List<string>();

            public MarkdownProblem? CheckWritingSystem(string tag)
            {
                Asked.Add(tag);
                if (tag == "ceb-x-old")
                    return new MarkdownProblem("ws.legacy", MarkdownSeverity.Warning, "ceb-x-old is a legacy writing system of this project; it is kept.");
                if (tag == "qaa-zzz")
                    return new MarkdownProblem(null, MarkdownSeverity.Error, "qaa-zzz is not in this project. Its systems are en and ceb.") { Choices = new[] { "en", "ceb" }, Nearest = new[] { "ceb" } };
                if (tag == "xx")
                    return new MarkdownProblem("This tag was removed in 2025.") { Replacement = "ceb" };
                return null;
            }
        }

        sealed class CheckedStyles : IStyleCatalog, IStyleChecker
        {
            public bool IsParagraphStyle(string name) => throw new InvalidOperationException("CheckStyle is the way in when the host implements IStyleChecker.");
            public bool IsCharacterStyle(string name) => throw new InvalidOperationException("CheckStyle is the way in when the host implements IStyleChecker.");

            public MarkdownProblem? CheckStyle(string name, bool paragraph)
            {
                if (name == "Exampel") return new MarkdownProblem("'Exampel' is not a " + (paragraph ? "paragraph" : "character") + " style. Nearest: Example.") { Nearest = new[] { "Example" } };
                if (name == "Old Style") return new MarkdownProblem("style.deprecated", MarkdownSeverity.Suggestion, "Old Style is deprecated; use New Style.");
                return null;
            }
        }

        [Fact]
        public void AHostsProblemForAWritingSystemIsReportedWithItsChoicesAndNearest()
        {
            var options = new MarkdownOptions { WritingSystems = new CheckedWritingSystems() };
            var result = MarkdownConverter.Parse("a\n\nsee [kaon]{ws=qaa-zzz} here", options);
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(MarkdownSeverity.Error, d.Severity);
            Assert.Equal("qaa-zzz is not in this project. Its systems are en and ceb.", d.Message);
            Assert.Equal(new[] { "en", "ceb" }, d.Choices);
            Assert.Equal(new[] { "ceb" }, d.Nearest);
            Assert.Equal((3, 12, "ws=qaa-zzz"), (d.Line, d.Column, d.Observed));
        }

        [Fact]
        public void AKnownButWarnedWritingSystemStillReadsWithNoError()
        {
            var options = new MarkdownOptions { WritingSystems = new CheckedWritingSystems() };
            var result = MarkdownConverter.Parse("see [kaon]{ws=ceb-x-old}", options);
            var d = Assert.Single(result.Diagnostics);
            Assert.Equal("ws.legacy", d.Id);
            Assert.Equal(MarkdownSeverity.Warning, d.Severity);
            Assert.False(result.HasErrors);
            Assert.Equal("ceb-x-old", result.Document.Paragraphs[0].Runs.Single(r => r.Text == "kaon").Ws);
        }

        [Fact]
        public void AWritingSystemTheHostLikesIsNotReportedAndIsAskedAboutOnce()
        {
            var host = new CheckedWritingSystems();
            var result = MarkdownConverter.Parse("[a]{ws=ceb} [b]{ws=ar} [c]{v}", new MarkdownOptions { WritingSystems = host });
            Assert.Empty(result.Diagnostics);
            Assert.Equal(new[] { "ceb", "ar" }, host.Asked);
        }

        [Fact]
        public void AHostsProblemForTheWritingSystemOfAnEmptyMarkerIsReportedToo()
        {
            var options = new MarkdownOptions { WritingSystems = new CheckedWritingSystems() };
            var d = Assert.Single(MarkdownConverter.Parse("a\n\n{empty ws=qaa-zzz}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal(new[] { "en", "ceb" }, d.Choices);
            Assert.Equal((3, 8, "ws=qaa-zzz"), (d.Line, d.Column, d.Observed));
            var warned = Assert.Single(MarkdownConverter.Parse("{empty ws=ceb-x-old}", options).Diagnostics);
            Assert.Equal((MarkdownSeverity.Warning, "ws.legacy"), (warned.Severity, warned.Id));
        }

        [Fact]
        public void AReplacementFromTheHostsWritingSystemCheckerIsTheFixAndItsIdIsOptional()
        {
            var options = new MarkdownOptions { WritingSystems = new CheckedWritingSystems() };
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=xx}", options).Diagnostics);
            Assert.Equal("fwmd.ws.unknown", d.Id);
            Assert.Equal("ceb", d.Replacement);
        }

        [Fact]
        public void AHostsProblemForAStyleCarriesItsNearestNamesForParagraphsAndCharacters()
        {
            var options = new MarkdownOptions { Styles = new CheckedStyles() };
            var paragraph = Assert.Single(MarkdownConverter.Parse("a\n\n::: {style=Exampel}\nx\n:::", options).Diagnostics);
            Assert.Equal("fwmd.style.unknown", paragraph.Id);
            Assert.Equal(new[] { "Example" }, paragraph.Nearest);
            Assert.Contains("paragraph style", paragraph.Message);
            Assert.Equal((3, 6, "style=Exampel"), (paragraph.Line, paragraph.Column, paragraph.Observed));
            var character = Assert.Single(MarkdownConverter.Parse("see [x]{cs=Exampel}", options).Diagnostics);
            Assert.Contains("character style", character.Message);
            Assert.Equal("cs=Exampel", character.Observed);
        }

        [Fact]
        public void AStyleThatIsAcceptedWithASuggestionStillReadsWithNoError()
        {
            var options = new MarkdownOptions { Styles = new CheckedStyles() };
            var result = MarkdownConverter.Parse("::: {style=\"Old Style\"}\nx\n:::\n\n[y]{cs=\"Old Style\"} and [z]{cs=Strong}", options);
            Assert.False(result.HasErrors);
            Assert.Equal(new[] { "style.deprecated", "style.deprecated" }, result.Diagnostics.Select(d => d.Id).ToArray());
            Assert.All(result.Diagnostics, d => Assert.Equal(MarkdownSeverity.Suggestion, d.Severity));
            Assert.Equal("Old Style", result.Document.Paragraphs[0].Style);
        }

        [Fact]
        public void TheListsFillWhatTheHostsCheckerLeavesEmpty()
        {
            var options = new MarkdownOptions { WritingSystems = new ListedAndChecked() };
            var d = Assert.Single(MarkdownConverter.Parse("[x]{ws=cbe}", options).Diagnostics);
            Assert.Equal(new[] { "en", "ceb" }, d.Choices);
            Assert.Equal(new[] { "ceb" }, d.Nearest);
        }

        sealed class ListedAndChecked : IWritingSystemMapper, IWritingSystemChecker, IKnownWritingSystems
        {
            public string DefaultAnalysis => "en";
            public string DefaultVernacular => "ceb";
            public IReadOnlyList<string> KnownTags { get; } = new[] { "en", "ceb" };
            public bool IsKnown(string tag) => KnownTags.Contains(tag);
            public MarkdownProblem? CheckWritingSystem(string tag) => IsKnown(tag) ? null : new MarkdownProblem("'" + tag + "' is not here.");
        }

        [Fact]
        public void ADiagnosticsPropertiesAreSettableForTheHost()
        {
            var d = new MarkdownDiagnostic("x.y", MarkdownSeverity.Error, "m", 0, 1) { Observed = "o", Replacement = "r", Choices = new[] { "a" }, Nearest = new[] { "b" } };
            Assert.Equal(("o", "r"), (d.Observed, d.Replacement));
            Assert.Equal(new[] { "a" }, d.Choices);
            Assert.Equal(new[] { "b" }, d.Nearest);
        }
    }
}
