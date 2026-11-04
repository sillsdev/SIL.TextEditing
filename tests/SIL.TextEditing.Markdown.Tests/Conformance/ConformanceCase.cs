using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SIL.TextEditing.Markdown.Notation;

namespace SIL.TextEditing.Markdown.Tests.Conformance
{
    /// <summary>One expected finding of a fixture: <c>id</c>, <c>severity</c> and the range, and optionally the fix, choices and nearest names.</summary>
    internal sealed class ExpectedFinding
    {
        public string Id = string.Empty, Severity = "error";
        public int Line, Column, EndLine, EndColumn;
        public bool HasFix;
        public string? Fix;
        public List<string>? Choices, Nearest;

        public override string ToString() => Id + " " + Severity + " " + Line + ":" + Column + "-" + EndLine + ":" + EndColumn;
    }

    /// <summary>One case of a conformance fixture file (tests/conformance/README.md).</summary>
    internal sealed class ConformanceCase
    {
        public string File = string.Empty, Name = string.Empty;
        public string Id => File + "::" + Name;
        public string? Markdown;
        public List<Paragraph>? Paragraphs;
        public List<ExpectedFinding> Findings = new List<ExpectedFinding>();
        public bool CanonicalSpecified;
        public string? Canonical;
        public bool ExplicitFrontMatter;
        public bool Pandoc = true;
        public string? PandocReason;
        public Dictionary<string, object?>? Project;
        public Dictionary<string, object?>? Options;

        public bool IsRead => Markdown != null;
        public bool HasErrors => Findings.Any(f => f.Severity == "error");

        /// <summary>The front matter the runner puts before the Markdown of a case that does not say <c>"frontMatter": "explicit"</c>.</summary>
        public const string StandardFrontMatter =
            "---\nfwmd: 1\nanalysis: \"en\"\nvernacular: \"ceb\"\nwritingSystems:\n  en:\n    lang: \"en\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n  ceb:\n    lang: \"ceb\"\n    dir: \"ltr\"\n    font: \"Charis SIL\"\n---\n\n";

        public static readonly int StandardFrontMatterLines = StandardFrontMatter.Count(c => c == '\n');

        public string FullMarkdown(string body) => ExplicitFrontMatter ? body : StandardFrontMatter + body;
    }

    internal static class ConformanceLoader
    {
        static List<ConformanceCase>? _all;

        public static string Directory => Path.Combine(AppContext.BaseDirectory, "conformance");

        public static IReadOnlyList<ConformanceCase> All()
        {
            if (_all != null) return _all;
            var cases = new List<ConformanceCase>();
            foreach (string path in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                var list = Json.Arr(Json.Parse(File.ReadAllText(path)));
                foreach (var item in list) cases.Add(Load(Path.GetFileName(path), Json.Obj(item)));
            }
            var duplicate = cases.GroupBy(c => c.Id).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) throw new InvalidOperationException("Two fixture cases are called " + duplicate.Key);
            return _all = cases;
        }

        public static ConformanceCase Find(string id) => All().First(c => c.Id == id);

        static ConformanceCase Load(string file, Dictionary<string, object?> o)
        {
            var c = new ConformanceCase { File = file, Name = Json.Str(o["name"]) };
            c.Markdown = Json.OptStr(o, "markdown");
            c.ExplicitFrontMatter = Json.OptStr(o, "frontMatter") == "explicit";
            if (o.TryGetValue("paragraphs", out var ps) && ps != null) c.Paragraphs = Json.Arr(ps).Select(p => Paragraph(Json.Obj(p))).ToList();
            if (o.TryGetValue("findings", out var fs) && fs != null)
            {
                foreach (var f in Json.Arr(fs).Select(Json.Obj))
                {
                    var finding = new ExpectedFinding
                    {
                        Id = Json.Str(f["id"]),
                        Severity = Json.OptStr(f, "severity") ?? "error",
                        Line = Json.OptInt(f, "line", 1),
                        Column = Json.OptInt(f, "column", 1),
                        EndLine = Json.OptInt(f, "endLine", Json.OptInt(f, "line", 1)),
                        EndColumn = Json.OptInt(f, "endColumn", 1),
                        HasFix = f.ContainsKey("fix"),
                        Fix = Json.OptStr(f, "fix"),
                    };
                    if (f.TryGetValue("choices", out var ch) && ch != null) finding.Choices = Json.Arr(ch).Select(Json.Str).ToList();
                    if (f.TryGetValue("nearest", out var ne) && ne != null) finding.Nearest = Json.Arr(ne).Select(Json.Str).ToList();
                    c.Findings.Add(finding);
                }
            }
            if (o.ContainsKey("canonical")) { c.CanonicalSpecified = true; c.Canonical = Json.OptStr(o, "canonical"); }
            if (o.TryGetValue("pandoc", out var pd))
            {
                if (pd is bool b) c.Pandoc = b;
                else if (pd is string reason) { c.Pandoc = false; c.PandocReason = reason; }
            }
            if (o.TryGetValue("project", out var pr) && pr != null) c.Project = Json.Obj(pr);
            if (o.TryGetValue("options", out var op) && op != null) c.Options = Json.Obj(op);
            return c;
        }

        // ---------------------------------------------------------------- the model

        static Paragraph Paragraph(Dictionary<string, object?> o)
        {
            string style = Json.OptStr(o, "style") ?? "Normal";
            var paragraph = new Paragraph { Style = style };
            if (o.TryGetValue("props", out var props) && props != null) Props(Json.Obj(props), paragraph.Props);
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Rtl = paragraph.Props.TryGetInt(128, out int rtl) && rtl != 0;
            if (o.TryGetValue("runs", out var runs) && runs != null)
                foreach (var r in Json.Arr(runs).Select(Json.Obj)) paragraph.Runs.Add(Run(r));
            paragraph.DefaultWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : Json.OptStr(o, "typingWs") ?? "en";
            return paragraph;
        }

        static Run Run(Dictionary<string, object?> o)
        {
            var run = new Run { Text = Json.Str(o["text"]), Ws = Json.Str(o["ws"]), ObjData = Json.OptStr(o, "objData") };
            if (o.TryGetValue("props", out var props) && props != null) Props(Json.Obj(props), run.Props);
            return run;
        }

        sealed class Collector : IAttrIssues
        {
            public readonly List<string> Problems = new List<string>();
            public void Report(string id, MarkdownSeverity severity, string message, AttrItem item, string? replacement = null) { if (severity == MarkdownSeverity.Error) Problems.Add(id + ": " + message); }
        }

        /// <summary>The properties a fixture spells with their attribute keys (<c>bold</c>, <c>fontsize</c>, <c>p50</c>, <c>s6x</c>) as a property bag.</summary>
        static void Props(Dictionary<string, object?> spelled, PropertyBag bag)
        {
            var items = spelled.Select(kv => new AttrItem(kv.Key, Json.Str(kv.Value), false, 0, 0)).ToList();
            var collector = new Collector();
            PropertyKeys.Apply(items, bag, collector, item => collector.Problems.Add("unknown key " + item.Key));
            if (collector.Problems.Count > 0) throw new InvalidOperationException("A fixture's properties are not valid: " + string.Join("; ", collector.Problems));
        }
    }
}
