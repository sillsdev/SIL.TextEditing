using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>ADR 0023 through the playground: typing is one undo step, and the inspector and the log show it.</summary>
    public class UndoGroupPlaygroundTests
    {
        [Fact]
        public void Typing_a_word_is_one_undo_step_that_the_Undo_button_takes_back_whole() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                string before = s.ParagraphText(0);
                foreach (var c in "word") s.Type(c.ToString());
                Assert.Contains("undo 1 done, 0 undone (steps; 4 edits done)", s.Inspector);
                s.Click(PlaygroundView.Names.Undo);
                Assert.Equal(before, s.ParagraphText(0));
                Assert.Contains("undo 0 done, 1 undone", s.Inspector);
            }
        });

        [Fact]
        public void A_caret_key_ends_the_step_and_the_log_says_the_host_task_closed() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Caret(0, 0);
                s.Type("a");
                s.Type("b");
                Assert.DoesNotContain("ended", s.Log);
                s.Caret(0, 0);
                Assert.Contains("ended", s.Log);
                s.Type("c");
                Assert.Contains("undo 2 done", s.Inspector);
                s.Press(Key.Z, KeyModifiers.Control);
                Assert.StartsWith("ab", s.ParagraphText(0));
            }
        });

        [Fact]
        public void A_pause_in_typing_closes_the_step_by_itself() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.MixedBidi))
            {
                s.Editor.TypingPause = TimeSpan.FromMilliseconds(60);
                s.Caret(0, 0);
                s.Type("a");
                Assert.True(s.Editor.HasOpenUndoGroup);
                for (int i = 0; i < 40 && s.Editor.HasOpenUndoGroup; i++)
                {
                    await Task.Delay(50);
                    Headless.Pump();
                }
                Assert.False(s.Editor.HasOpenUndoGroup);
                Assert.Contains("ended", s.Log);
            }
        });
    }
}
