using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>Issue 103 through the playground: with "Slow dictionary, off the UI thread" ticked the playground's checkers are
    /// <see cref="IBackgroundSpellChecker"/>s, a worker asks them (15 ms a word) and the field applies the answers on its own thread.</summary>
    public class SpellingBackgroundPlaygroundTests
    {
        static string[] Words(Session s, int paragraph) =>
            s.Field.SpellingMarks(paragraph).Select(m => s.ParagraphText(paragraph).Substring(m.Start, m.Length)).ToArray();

        /// <summary>Lets the worker have turns until the field has everything in view checked; it waits for the result, not for a time.</summary>
        static void UntilChecked(Session s)
        {
            for (int i = 0; i < 4000 && !s.Field.IsSpellingComplete; i++)
            {
                Thread.Sleep(2);
                Headless.Pump();
            }
            Assert.True(s.Field.IsSpellingComplete);
        }

        [Fact]
        public void Ticking_the_slow_dictionary_box_has_the_words_asked_off_the_UI_thread_and_the_same_squiggles_arrive() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                Headless.Pump();
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));          // the plain dictionary: asked on the UI thread
                Assert.Empty(s.View.Spelling!.CheckThreads);

                s.Find<CheckBox>(PlaygroundView.Names.SpellBackground).IsChecked = true;
                Headless.Pump();
                Assert.Contains("dictionary: slow, asked off the UI thread", s.Log);
                UntilChecked(s);
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
                Assert.Equal(new[] { "abc123" }, Words(s, 1));
                Assert.Equal(new[] { "Gluedmots" }, s.Field.SpellingMarks(2).Where(m => m.Problem == SpellingProblem.Mixed).Select(m => s.ParagraphText(2).Substring(m.Start, m.Length)));
                var threads = s.View.Spelling!.CheckThreads;
                Assert.NotEmpty(threads);
                Assert.DoesNotContain(Environment.CurrentManagedThreadId, threads);

                s.Find<CheckBox>(PlaygroundView.Names.SpellBackground).IsChecked = false;   // and back to the plain dictionary
                Headless.Pump();
                UntilChecked(s);
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
            }
        });

        [Fact]
        public void Typing_while_the_slow_dictionary_is_being_asked_leaves_no_squiggle_for_text_that_is_not_there() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling))
            {
                s.Find<CheckBox>(PlaygroundView.Names.SpellBackground).IsChecked = true;
                Headless.Pump();
                s.Caret(6, s.ParagraphText(6).Length);
                s.Type("teh ");                                                 // while the worker may be in the middle of its batches
                UntilChecked(s);
                Assert.Equal(new[] { "jumpd", "teh" }, Words(s, 0));
                Assert.Equal(new[] { "teh" }, Words(s, 6));
                Assert.Equal(SpellScanner.Check(s.Editor.Document.Paragraphs[6], s.Field.SpellChecking!), s.Field.SpellingMarks(6).ToList());
            }
        });
    }
}
