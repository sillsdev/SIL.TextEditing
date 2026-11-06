using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Keeps the format documentation in docs/formats honest: every finding id the source can report is in the catalogue (findings.md). It reads the
    /// repository from disk, so it finds the ids of every project (the LCModel adapter's too) without referencing it.</summary>
    public class FormatDocsTests
    {
        // A whole id in a string literal: "fwmd.syntax.unclosed-mark", "textediting.lcm.unstorable-property". A prefix such as "fwmd.table." ends in a dot and is
        // not an id.
        static readonly Regex IdLiteral = new Regex("\"((?:fwmd|textediting)\\.[a-z0-9.-]*[a-z0-9])\"", RegexOptions.CultureInvariant);

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
    }
}
