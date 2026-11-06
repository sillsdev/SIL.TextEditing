using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.TextEditing.Markdown.Tests.Conformance;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Keeps the format documentation in docs/formats honest: every finding id the source can report is in the catalogue (findings.md), and every
    /// example marked with a fixture comment is, character for character, the text of that fixture case. Both read the repository from disk, so they find the
    /// ids of every project (the LCModel adapter's too) without referencing it.</summary>
    public class FormatDocsTests
    {
        // A whole id in a string literal: "fwmd.syntax.unclosed-mark", "textediting.lcm.unstorable-property". A prefix such as "fwmd.table." ends in a dot and is
        // not an id.
        static readonly Regex IdLiteral = new Regex("\"((?:fwmd|textediting)\\.[a-z0-9.-]*[a-z0-9])\"", RegexOptions.CultureInvariant);

        // <!-- fixture: 05-links.json | case name | markdown --> before a fenced block. The field is markdown, canonical or file.
        static readonly Regex FixtureComment = new Regex(@"<!-- fixture: (?<file>[^|]+?) \| (?<name>.+?) \| (?<field>markdown|canonical|file) -->\n```[a-z]*\n(?<text>.*?)\n```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        // <!-- fixture-excerpt: records/blocks.schema.json --> before a fenced block that is part of that fixture file, as it stands.
        static readonly Regex ExcerptComment = new Regex(@"<!-- fixture-excerpt: (?<file>[^ ]+) -->\n```[a-z]*\n(?<text>.*?)\n```",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        static string RepositoryRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SIL.TextEditing.sln"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("SIL.TextEditing.sln is not above " + AppContext.BaseDirectory);
        }

        static string ReadText(string path) => File.ReadAllText(path).Replace("\r\n", "\n");

        static string FormatsDirectory => Path.Combine(RepositoryRoot(), "docs", "formats");

        [Fact]
        public void EveryFindingIdInTheSourceIsInTheCatalogue()
        {
            string catalogue = ReadText(Path.Combine(FormatsDirectory, "findings.md"));
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.GetFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in IdLiteral.Matches(ReadText(path))) ids.Add(m.Groups[1].Value);
            Assert.NotEmpty(ids);
            var missing = ids.Where(id => catalogue.IndexOf("`" + id + "`", StringComparison.Ordinal) < 0).ToList();
            Assert.True(missing.Count == 0, "docs/formats/findings.md does not list: " + string.Join(", ", missing));
        }

        [Fact]
        public void EveryMarkedExampleIsTheTextOfItsFixtureCase()
        {
            int checkedExamples = 0;
            var problems = new List<string>();
            foreach (string doc in Directory.GetFiles(FormatsDirectory, "*.md"))
            {
                foreach (Match m in FixtureComment.Matches(ReadText(doc)))
                {
                    checkedExamples++;
                    string file = m.Groups["file"].Value, name = m.Groups["name"].Value, field = m.Groups["field"].Value;
                    string where = Path.GetFileName(doc) + ": " + file + " \"" + name + "\" " + field;
                    string path = Path.Combine(RepositoryRoot(), "tests", "conformance", file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) { problems.Add(where + ": no such fixture file"); continue; }
                    var found = Json.Arr(Json.Parse(ReadText(path))).Select(Json.Obj).FirstOrDefault(c => Json.Str(c["name"]) == name);
                    if (found == null) { problems.Add(where + ": no such case"); continue; }
                    // A canonical of null means the input is canonical.
                    string? text = Json.OptStr(found, field) ?? (field == "canonical" && found.ContainsKey("canonical") ? Json.OptStr(found, "markdown") ?? Json.OptStr(found, "file") : null);
                    if (text == null) { problems.Add(where + ": the case has no " + field); continue; }
                    if (text.TrimEnd('\n') != m.Groups["text"].Value) problems.Add(where + ": the example differs from the fixture");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            Assert.True(checkedExamples > 0, "No example in docs/formats is marked with its fixture.");
        }

        [Fact]
        public void EveryMarkedExcerptIsPartOfItsFixtureFile()
        {
            var problems = new List<string>();
            foreach (string doc in Directory.GetFiles(FormatsDirectory, "*.md"))
            {
                foreach (Match m in ExcerptComment.Matches(ReadText(doc)))
                {
                    string file = m.Groups["file"].Value;
                    string path = Path.Combine(RepositoryRoot(), "tests", "conformance", file.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) problems.Add(Path.GetFileName(doc) + ": " + file + ": no such fixture file");
                    else if (ReadText(path).IndexOf(m.Groups["text"].Value, StringComparison.Ordinal) < 0) problems.Add(Path.GetFileName(doc) + ": " + file + ": the excerpt is not in the file");
                }
            }
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
    }
}
