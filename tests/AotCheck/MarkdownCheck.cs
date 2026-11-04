using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

// Exercises the public surface of SIL.TextEditing.Markdown under AOT, using the public API only: every block and inline form with the
// default host, then the same forms with a host of our own that implements every optional interface. Markdig is rooted too (see
// AotCheck.csproj), so the publish analyses all of it. A failed check is a line on the console and a non-zero exit code; the publish
// itself is the real check, and this makes sure the code that publishes also runs.
internal static class MarkdownCheck
{
    static readonly List<string> Failures = new List<string>();

    static void Check(bool condition, string what)
    {
        if (!condition) Failures.Add(what);
    }

    /// <summary>Runs every check; returns the failures, empty when all passed.</summary>
    public static IReadOnlyList<string> Run()
    {
        DefaultHost();
        CustomHost();
        Abstract();
        return Failures;
    }

    // The publish runs with invariant globalization, where string.Normalize leaves the text alone (no decomposition tables), so the check
    // below is for what the platform gives, and the code that normalises is run all the same.
    const string AllForms =
        "# Heading one\n\n## Heading two\n\n### Heading three\n\n"
        + "Plain **bold** *italic* ***both*** [mang]{ws=ceb} [vern]{v} [x]{cs=\"Emphasized Text\" fontsize=14000} [site](https://example.org) a\\\nbreak and caf\u00E9 &amp; \\*stars\\*\n\n"
        + "- bullet one\n- bullet two\n\n"
        + "1. first\n2. second\n\n"
        + "> a quote\n\n"
        + "| A | B |\n| --- | --- |\n| 1 | **2** |\n\n"
        + "::: {style=Example fontsize=12000}\nfenced line\nsecond line\n:::\n\n"
        + "{empty}\n\n"
        + "{empty v}\n\n"
        + "[opaque]{s6=\"xyz\"}\n";

    static void DefaultHost()
    {
        var parsed = MarkdownConverter.Parse(AllForms);
        Check(!parsed.HasErrors, "default host: no errors in the every-form text: " + string.Join("; ", parsed.Diagnostics));
        var paragraphs = parsed.Document.Paragraphs;
        string[] styles = paragraphs.Select(p => p.Style).Distinct().ToArray();
        foreach (var expected in new[] { "Heading 1", "Heading 2", "Heading 3", "Normal", "Bulleted List", "Numbered List", "Block Quote", "GDR Table Header", "GDR Table Row", "Example" })
            Check(styles.Contains(expected), "default host: a paragraph of style " + expected);
        var inline = paragraphs.FirstOrDefault(p => p.Style == "Normal" && p.Runs.Count > 3);
        Check(inline != null, "default host: the inline paragraph");
        if (inline != null)
        {
            Check(inline.Runs.Any(r => r.Props.IntProps.ContainsKey(TextPropertyNumbers.Bold)), "default host: a bold run");
            Check(inline.Runs.Any(r => r.Props.IntProps.ContainsKey(TextPropertyNumbers.Italic)), "default host: an italic run");
            Check(inline.Runs.Any(r => r.Ws == "ceb"), "default host: a span with ws");
            Check(inline.Runs.Any(r => r.ObjData != null && r.ObjData.Length > 0 && r.ObjData[0] == DefaultObjectMapper.ExternalPathNameType), "default host: an external link");
            Check(inline.Runs.Any(r => r.Text.IndexOf('\u2028') >= 0), "default host: a hard break");
            Check(inline.Runs.Any(r => r.Text.Contains("caf" + "\u00E9".Normalize(System.Text.NormalizationForm.FormD))), "default host: text read as the platform normalises it");
        }
        Check(paragraphs.Any(p => p.Runs.Count == 0), "default host: an empty paragraph");
        Check(paragraphs.Any(p => p.Runs.Any(r => r.ObjData == "xyz")), "default host: generic object data");

        string written = MarkdownConverter.ToMarkdown(parsed.Document);
        var again = MarkdownConverter.Parse(written);
        Check(!again.HasErrors, "default host: the written text reads without errors");
        Check(MarkdownConverter.AreEquivalent(parsed.Document.Paragraphs, again.Document.Paragraphs), "default host: written then read is equivalent");
        Check(MarkdownConverter.Canonicalize(written) == written, "default host: the written text is canonical");
        Check(MarkdownConverter.ToMarkdown((IEnumerable<Paragraph>)paragraphs) == written, "default host: ToMarkdown of the paragraphs");
        Check(MarkdownConverter.AreEquivalent(paragraphs[0], again.Document.Paragraphs[0]), "default host: a paragraph is equivalent to itself read back");

        // Findings of every family the parser makes without a host: syntax, table, heading, unclosed fence.
        var bad = MarkdownConverter.Parse("`code` <b>x</b> [y]{wss=1} [z]{bold=on bold=off}\n\n#### deep\n\n| a | b |\n| :--- | --- |\n| 1 |\n\n::: {style=x}\nunclosed");
        foreach (var id in new[] { MarkdownDiagnosticIds.UnsupportedMarkup, MarkdownDiagnosticIds.UnknownAttribute, MarkdownDiagnosticIds.DuplicateAttribute, MarkdownDiagnosticIds.HeadingTooDeep,
                     MarkdownDiagnosticIds.TableAlignment, MarkdownDiagnosticIds.TableRagged, MarkdownDiagnosticIds.UnclosedMark })
            Check(bad.Diagnostics.Any(d => d.Id == id), "default host: finding " + id);
        Check(bad.HasErrors && bad.Diagnostics.All(d => d.Line > 0 && d.Column > 0), "default host: findings have a place");
        var made = bad.CreateDiagnostic("host.own", MarkdownSeverity.Warning, "mine", 0, 4, "x", new[] { "a" }, new[] { "b" });
        Check(made.Observed == "`cod" && made.Line == 1, "default host: a finding of the host's own");

        // A text beyond Markdig's depth limit is a finding and plain text, not an exception.
        var deep = MarkdownConverter.Parse(new string('*', 5000) + "x");
        Check(deep.Document.Paragraphs.Count > 0, "default host: deeply nested text is kept");

        // Other style names.
        var names = new MarkdownStyleNames { Normal = "Body", Heading1 = "H1", BulletedList = "Bullets", TableRow = "Row", TableHeader = "Head", Hyperlink = "Link" };
        var renamed = MarkdownConverter.Parse("# a\n\n- b\n\nc [d](https://e.org)", new MarkdownOptions { StyleNames = names });
        Check(renamed.Document.Paragraphs.Select(p => p.Style).SequenceEqual(new[] { "H1", "Bullets", "Body" }), "default host: renamed styles");
    }

    static void CustomHost()
    {
        var host = new CheckHost();
        var options = new MarkdownOptions { WritingSystems = host, Styles = host, Objects = host };
        string md = "Know [[w:palit]] and [[w:palit]]{guid=" + CheckHost.Entry + "} and [a rule]{link=rule:Nasal guid=" + CheckHost.Rule + "} and ![a picture](fw:picture/" + CheckHost.Picture + ")\n\n"
            + "::: {style=Example}\n[mang]{ws=ceb} [old]{ws=old}\n:::\n";
        var parsed = MarkdownConverter.Parse(md, options);
        Check(parsed.Diagnostics.All(d => d.Severity != MarkdownSeverity.Error), "custom host: no errors: " + string.Join("; ", parsed.Diagnostics));
        Check(parsed.Objects.Count == 4, "custom host: four objects, got " + parsed.Objects.Count);
        Check(parsed.Objects.All(o => o.Source != null && o.ObjData != null), "custom host: each object has a place and data");
        Check(parsed.Document.Paragraphs[0].Runs.Any(r => r.ObjData == CheckHost.PictureData), "custom host: the picture's data");
        Check(parsed.Diagnostics.Any(d => d.Id == "host.ws.legacy" && d.Severity == MarkdownSeverity.Warning), "custom host: the host's warning for a tag");

        var written = MarkdownConverter.ToMarkdown(parsed.Document, options);
        var back = MarkdownConverter.Parse(written, options);
        Check(MarkdownConverter.AreEquivalent(parsed.Document.Paragraphs, back.Document.Paragraphs), "custom host: written then read is equivalent");
        Check(written.Contains("fw:picture/" + CheckHost.Picture), "custom host: the picture is written from the host's description");

        // The host's problems: unknown writing system and style, an ambiguous and a missing link, a deleted target, a missing picture.
        var bad = MarkdownConverter.Parse("[x]{ws=zzz} [[w:palt]] [[w:twin]] [[w:gone]] ![p](fw:picture/" + Guid.Empty + ")\n\n::: {style=Exmple}\n[y]{cs=Strng}\n:::", options);
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.WsUnknown), "custom host: unknown writing system");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkNotFound && d.Nearest.Contains("palit")), "custom host: link not found with nearest");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkAmbiguous && d.Choices.Count == 2), "custom host: ambiguous link with choices");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkBroken && d.Severity == MarkdownSeverity.Warning), "custom host: a deleted target is a warning");
        Check(bad.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.PictureUnknown), "custom host: unknown picture");
        Check(bad.Diagnostics.Count(d => d.Id == MarkdownDiagnosticIds.StyleUnknown) == 2, "custom host: unknown styles");

        // A host with only the required interfaces.
        var plain = new MarkdownOptions { WritingSystems = new PlainSystems(), Styles = new PlainStyles(), Objects = new PlainObjects() };
        var plainResult = MarkdownConverter.Parse("[x]{ws=zzz}\n\n::: {style=nope}\n[a](https://e.org) [[w:q]]\n:::", plain);
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.WsUnknown), "plain host: unknown writing system");
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.StyleUnknown), "plain host: unknown style");
        Check(plainResult.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.LinkNotFound), "plain host: a link it cannot make");
        Check(new MarkdownProblem("m").Severity == MarkdownSeverity.Error && new MarkdownSourceRange(0, 1, 1, 1, 1, 2).End == 1, "the small public types");
    }

    static void Abstract()
    {
        var result = MarkdownConverter.ParseInline("An **abstract** with [mang]{ws=ceb}\\\nand a break\n\n# not allowed");
        Check(result.Diagnostics.Any(d => d.Id == MarkdownDiagnosticIds.FieldBlockSyntax) && result.HasErrors, "abstract: block syntax is a finding");
        Check(result.Runs.Any(r => r.Ws == "ceb"), "abstract: a span");
        string text = MarkdownConverter.ToInlineMarkdown(result.Runs);
        Check(text.Contains("**abstract**"), "abstract: written back inline");
        Check(result.Source.Length > 0 && result.CreateDiagnostic("x.y", MarkdownSeverity.Suggestion, "m", 0, 2).Observed == "An", "abstract: a finding of the host's own");
    }

    sealed class CheckHost : IWritingSystemMapper, IKnownWritingSystems, IWritingSystemChecker, IStyleCatalog, IKnownStyles, IStyleChecker, IObjectMapper, IObjectResolver
    {
        public static readonly Guid Entry = new Guid("11111111-2222-3333-4444-555555555555");
        public static readonly Guid Rule = new Guid("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        public static readonly Guid Picture = new Guid("99999999-8888-7777-6666-555555555555");
        public static readonly string PictureData = "\u0008" + Picture.ToString("N");

        static readonly string[] Tags = { "en", "ceb", "ar", "old" };
        static readonly string[] ParagraphStyles = { "Normal", "Example" };
        static readonly string[] CharacterStyles = { "Emphasized Text", "Strong" };

        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => Array.IndexOf(Tags, tag) >= 0;
        public IReadOnlyList<string> KnownTags => Tags;

        public MarkdownProblem? CheckWritingSystem(string tag)
        {
            if (tag == "old") return new MarkdownProblem("host.ws.legacy", MarkdownSeverity.Warning, "'old' is a legacy writing system.");
            return IsKnown(tag) ? null : new MarkdownProblem("'" + tag + "' is not a writing system of this project.");
        }

        public bool IsParagraphStyle(string name) => Array.IndexOf(ParagraphStyles, name) >= 0;
        public bool IsCharacterStyle(string name) => Array.IndexOf(CharacterStyles, name) >= 0;
        public IReadOnlyList<string> ParagraphStyleNames => ParagraphStyles;
        public IReadOnlyList<string> CharacterStyleNames => CharacterStyles;

        public MarkdownProblem? CheckStyle(string name, bool paragraph)
            => (paragraph ? IsParagraphStyle(name) : IsCharacterStyle(name)) ? null : new MarkdownProblem("'" + name + "' is not a style of this project.");

        public MarkdownObject? Describe(string objData)
        {
            if (objData == PictureData) return new MarkdownObject { Kind = MarkdownObjectKind.Picture, Guid = Picture, Caption = "a picture" };
            if (objData == "link:w") return new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, LinkKind = "w", Guid = Entry, Name = "palit" };
            if (objData == "link:rule") return new MarkdownObject { Kind = MarkdownObjectKind.InternalLink, LinkKind = "rule", Guid = Rule, Name = "Nasal" };
            return null;
        }

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            bool ok = TryResolve(target, out objData, out var p);
            problem = p?.Message;
            return ok;
        }

        public bool TryResolve(MarkdownObject target, out string objData, out MarkdownProblem? problem)
        {
            problem = null;
            objData = string.Empty;
            if (target.Kind == MarkdownObjectKind.Picture)
            {
                if (target.Guid != Picture) { problem = new MarkdownProblem("no such picture"); return false; }
                objData = PictureData;
                return true;
            }
            if (target.Kind == MarkdownObjectKind.ExternalLink) { objData = DefaultObjectMapper.ExternalPathNameType + (target.Url ?? string.Empty); return true; }
            if (target.LinkKind == "w" && target.Name == "twin")
            {
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkAmbiguous, MarkdownSeverity.Error, "two entries are called twin") { Choices = new[] { "[[w:twin]]{guid=1}", "[[w:twin]]{guid=2}" } };
                return false;
            }
            if (target.LinkKind == "w" && target.Name == "gone")
            {
                objData = "link:w";
                problem = new MarkdownProblem(MarkdownDiagnosticIds.LinkBroken, MarkdownSeverity.Warning, "the target was deleted");
                return true;
            }
            if (target.LinkKind == "w" && target.Name == "palit" && (target.Guid == null || target.Guid == Entry)) { objData = "link:w"; return true; }
            if (target.LinkKind == "rule" && target.Guid == Rule) { objData = "link:rule"; return true; }
            problem = new MarkdownProblem("no " + target.LinkKind + " named " + target.Name) { Nearest = new[] { "palit" } };
            return false;
        }

        public bool IsKnownLinkKind(string kind) => kind == "w" || kind == "rule";
    }

    sealed class PlainSystems : IWritingSystemMapper
    {
        public string DefaultAnalysis => "en";
        public string DefaultVernacular => "ceb";
        public bool IsKnown(string tag) => tag == "en" || tag == "ceb";
    }

    sealed class PlainStyles : IStyleCatalog
    {
        public bool IsParagraphStyle(string name) => name == "Normal";
        public bool IsCharacterStyle(string name) => name == "Strong";
    }

    sealed class PlainObjects : IObjectMapper
    {
        public MarkdownObject? Describe(string objData) => null;

        public bool TryCreateObjData(MarkdownObject target, out string objData, out string? problem)
        {
            objData = string.Empty;
            problem = "nothing resolves";
            return false;
        }

        public bool IsKnownLinkKind(string kind) => kind == "w";
    }
}
