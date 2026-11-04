using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.TextEditing.Markdown.Reader;
using SIL.TextEditing.Markdown.Writer;
using Xunit;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Spec 3.4: the writer escapes exactly what changes the reading, however long the paragraph. The checks count backslashes,
    /// never time.</summary>
    public class EscapingTests
    {
        static string Write(params Paragraph[] paragraphs) => MarkdownConverter.ToMarkdown(B.Doc(paragraphs), Hosts.Project());

        static int Backslashes(string text) => text.Count(c => c == '\\');

        // Ordinary prose punctuation: every one of these is an optional escape candidate, none of them changes the reading.
        const string Prose = "a. b-c (d) e: f= ";

        static string Paragraph(int repeats, string trigger = "*word* ") => trigger + string.Concat(Enumerable.Repeat(Prose, repeats));

        [Fact]
        public void OneEmphasisLookalikeIsOneBackslashInAShortParagraph()
            => Assert.Equal(1, Backslashes(Write(B.Plain(Paragraph(10)))));

        [Theory]
        [InlineData(50)]   // about 300 candidates: the old cap was 400
        [InlineData(120)]  // about 2 KB
        [InlineData(1200)] // about 20 KB
        public void OneEmphasisLookalikeIsOneBackslashHoweverLongTheParagraph(int repeats)
        {
            string text = Write(B.Plain(Paragraph(repeats)));
            Assert.True(text.Length > repeats * Prose.Length);
            Assert.Equal(1, Backslashes(text));
            Assert.StartsWith("\\*word* a. b-c (d) e: f= ", text);
        }

        [Fact]
        public void SeveralTriggersEscapeOnlyWhatEachNeeds()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++) sb.Append(Paragraph(200, "*w" + i + "* "));
            sb.Append("[x]{v} and [+nasal] ");
            string text = Write(B.Plain(sb.ToString()));
            // One backslash for each of the five emphasis lookalikes and one for the bracket that would open the span: [x]{v} -> \[x]{v}.
            Assert.Equal(6, Backslashes(text));
            Assert.Contains("\\[x]{v} and [+nasal]", text);
        }

        [Fact]
        public void AParagraphLongEnoughToNeedManyEscapesStillReadsBack()
        {
            // Every word is an emphasis lookalike: the escapes are needed, not optional. The result must read back whatever the search spares.
            string text = string.Concat(Enumerable.Repeat("*w* _v_ [x]{v} ", 300));
            var paragraph = B.Plain(text);
            string markdown = Write(paragraph);
            var back = MarkdownConverter.Parse(markdown, Hosts.Project());
            Assert.False(back.HasErrors);
            Assert.True(MarkdownConverter.AreEquivalent(new[] { paragraph }, back.Document.Paragraphs));
        }

        [Fact]
        public void ALongTableCellEscapesOnlyWhatIsNeeded()
        {
            string cell = Paragraph(300, "*word* ");
            var header = B.P("GDR Table Header", B.En("a\t" + cell));
            string text = Write(header);
            Assert.Equal(1, Backslashes(text));
        }

        // ------------------------------------------------------------ the search writes what the one-by-one search it replaced wrote

        /// <summary>What the writer chose before the block search: try none of the optional escapes, then all, then take them away one at a
        /// time from the end back, keeping each that is still needed. Only plain and bracketed runs, so the first spelling of the ladder.</summary>
        static string OneByOne(Paragraph paragraph, MarkdownOptions options)
        {
            var context = BlockContext.Plain();
            var generator = new InlineGenerator(options, context);
            var cells = new List<List<Run>> { Equivalence.Normalized(paragraph).Runs };
            bool Accepts(string candidate)
            {
                var read = MarkdownReader.ReadParagraphs(context.FirstPrefix + candidate + "\n", options, out bool clean);
                return clean && read.Count == 1 && Equivalence.Equal(paragraph, read[0]);
            }
            var mode = new GenMode { StarMarks = true, ParenLinks = true };
            string bare = generator.Generate(cells, mode, null, out int count);
            if (Accepts(bare)) return bare;
            var mask = new bool[count];
            for (int k = 0; k < count; k++) mask[k] = true;
            Assert.True(Accepts(generator.Generate(cells, mode, mask, out _)));
            for (int k = count - 1; k >= 0; k--)
            {
                mask[k] = false;
                if (!Accepts(generator.Generate(cells, mode, mask, out _))) mask[k] = true;
            }
            return generator.Generate(cells, mode, mask, out _);
        }

        static readonly string[] Tokens =
        {
            "*", "**", "_", "__", "[", "]", "{", "}", "(", ")", "\\", "!", "#", "&", "-", "+", ".", ":", "=", ">", "~", "|", "[x]{v}", "]{", "[[", "](", "![", "*a*", "_b_", "word", "mang", "ng",
            " ", " ", " ", "1.", "1)", "- ", "# ", "> ", "\\*", "\\[", "&#x20;", "{empty}", "~~", "!", "a", "b", "{a,b}", "[+nasal]",
        };

        static readonly string[] Words =
        {
            "word", "mang", "ng", "the", "naga", "kaon", "palit", "a", "of", " ", " ", " ", " ", ", ", ". ", ": ", "-", "(", ")", "=", "x1",
        };

        /// <summary>A paragraph of mostly words with a few of the marks that matter, or (dense) a heap of them.</summary>
        static Paragraph RandomParagraph(Random random, bool dense)
        {
            var runs = new List<Run>();
            int runCount = 1 + random.Next(3);
            for (int r = 0; r < runCount; r++)
            {
                var sb = new StringBuilder();
                int tokens = 1 + random.Next(dense ? 40 : 60);
                for (int t = 0; t < tokens; t++)
                    sb.Append(dense || random.Next(12) == 0 ? Tokens[random.Next(Tokens.Length)] : Words[random.Next(Words.Length)]);
                runs.Add(B.R(random.Next(4) == 0 ? "ceb" : "en", sb.ToString()));
            }
            return B.P("Normal", runs.ToArray());
        }

        // The block search replaced a one-by-one search that read the paragraph once per candidate. Both start from "every escape" and
        // take escapes away from the end back. Where marks do not interact they write the same bytes; where they do (a bracket that only
        // pairs with another, a run of underscores) taking one away can break what taking two away does not, the block search finds the
        // second and keeps fewer escapes, and it never keeps more.
        [Fact]
        public void ForProseWithAFewMarksTheBlockSearchMostlyWritesWhatTheOneByOneSearchWroteAndNeverMoreEscapes()
        {
            var random = new Random(20261103);
            var options = Hosts.Project();
            int escaped = 0, same = 0;
            const int cases = 1500;
            for (int n = 0; n < cases; n++)
            {
                var paragraph = RandomParagraph(random, dense: false);
                string expected = OneByOne(paragraph, options) + "\n";
                string actual = MarkdownConverter.ToMarkdown(B.Doc(paragraph), options);
                if (Backslashes(expected) > 0) escaped++;
                if (expected == actual) same++;
                Assert.True(Backslashes(actual) <= Backslashes(expected), ModelDump.Of(B.Doc(paragraph)) + "\none by one:\n" + expected + "\nblocks:\n" + actual);
            }
            Assert.True(escaped > 300, "the corpus should need escapes: " + escaped);
            Assert.True(same >= cases * 97 / 100, same + " of " + cases + " are the same");
        }

        [Fact]
        public void ForDenseTextTheBlockSearchNeverKeepsMoreEscapesThanTheOneByOneSearch()
        {
            var random = new Random(20261104);
            var options = Hosts.Project();
            for (int n = 0; n < 700; n++)
            {
                var paragraph = RandomParagraph(random, dense: true);
                string expected = OneByOne(paragraph, options);
                string actual = MarkdownConverter.ToMarkdown(B.Doc(paragraph), options);
                Assert.True(Backslashes(actual) <= Backslashes(expected), ModelDump.Of(B.Doc(paragraph)) + "\none by one:\n" + expected + "\nblocks:\n" + actual);
            }
        }
    }
}
