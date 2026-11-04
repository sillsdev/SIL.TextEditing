using System.Collections.Generic;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Field-level edit rules: undo ordering (ADR 0023), paste grouping, line breaks, leaving and cutting.</summary>
    public class EditRulesFieldTests
    {
        [Fact]
        public void Ctrl_Z_and_Ctrl_Y_end_the_typing_step_before_they_ask_the_host_to_undo_or_redo() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("ab"));
            var undo = new SimpleUndoStack();
            undo.Attach(field.Editor);
            var log = new List<string>();
            field.Editor.UndoGroupEnded += g => log.Add("ended");
            field.Editor.DocumentChanged += (s, r, a) => log.Add("applied");
            field.UndoRequested += () => { log.Add("undo"); undo.Undo(); };
            field.RedoRequested += () => { log.Add("redo"); undo.Redo(); };
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Type("x");
            field.Type("y");
            log.Clear();

            field.Press(Key.Z, KeyModifiers.Control);
            Assert.Equal("ab", field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(new[] { "ended", "undo", "applied", "applied" }, log);

            log.Clear();
            field.Press(Key.Y, KeyModifiers.Control);
            Assert.Equal("abxy", field.Editor.Document.Paragraphs[0].Text);
            Assert.Equal(new[] { "redo", "applied", "applied" }, log);   // nothing was open, so nothing ends
        });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Shift_Enter_inserts_a_hard_line_break_that_the_layout_breaks_the_line_at(bool single) => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("abcd"), 520, f => f.SingleParagraph = single);
            var passed = new List<KeyModifiers>();
            field.EnterRequested += passed.Add;
            field.Editor.SetCaret(new TextPosition(0, 2));
            Assert.Single(field.ParagraphLayoutFor(0).Lines);

            Assert.True(field.HandleKey(Key.Enter, KeyModifiers.Shift));
            Assert.Empty(passed);
            Assert.Single(field.Editor.Document.Paragraphs);
            Assert.Equal("ab" + DocumentEditor.LineSeparator + "cd", field.Editor.Document.Paragraphs[0].Text);
            Fixtures.Layout(field, 520);
            Assert.Equal(2, field.ParagraphLayoutFor(0).Lines.Count);   // the layout treats U+2028 as a line break
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
        });

        [Fact]
        public void Plain_Enter_still_splits_a_paragraph_or_goes_to_the_host() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("abcd"));
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.HandleKey(Key.Enter, KeyModifiers.None);
            Assert.Equal(2, field.Editor.Document.Paragraphs.Count);
        });

        static async System.Threading.Tasks.Task Wait(int milliseconds)
        {
            for (int waited = 0; waited < milliseconds; waited += 25)
            {
                await System.Threading.Tasks.Task.Delay(25);
                Headless.Pump();
            }
        }

        [Fact]
        public void A_field_that_is_not_on_screen_runs_no_pause_timer_and_one_that_leaves_the_screen_closes_its_step() => Headless.Run(async () =>
        {
            var detached = Fixtures.Field(OneParagraph("ab"));
            detached.Editor.TypingPause = System.TimeSpan.FromMilliseconds(40);
            detached.Editor.SetCaret(new TextPosition(0, 2));
            detached.Type("x");
            await Wait(300);
            Assert.True(detached.Editor.HasOpenUndoGroup);   // no timer fired: nothing ends a step after the field is gone

            var (window, field) = Fixtures.InWindow(OneParagraph("ab"));
            field.Editor.TypingPause = System.TimeSpan.FromMilliseconds(40);
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Type("x");
            await Wait(300);
            Assert.False(field.Editor.HasOpenUndoGroup);   // on screen, the pause closes it

            field.Editor.TypingPause = System.TimeSpan.FromSeconds(30);
            field.Type("y");
            Assert.True(field.Editor.HasOpenUndoGroup);
            window.Content = null;                          // the field leaves the visual tree
            Headless.Pump();
            Assert.False(field.Editor.HasOpenUndoGroup);
            window.Close();
        });

        [Fact]
        public void A_pause_timer_restart_from_another_thread_is_handed_to_the_UI_thread_which_decides_whether_the_field_is_on_screen() => Headless.Run(async () =>
        {
            var restart = typeof(RichTextField).GetMethod("RestartPauseTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var (window, field) = Fixtures.InWindow(OneParagraph("ab"));
            field.Editor.TypingPause = System.TimeSpan.FromSeconds(30);   // typing here starts a timer that never fires in the test
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Type("x");
            Assert.True(field.Editor.HasOpenUndoGroup);
            field.Editor.TypingPause = System.TimeSpan.FromMilliseconds(40);
            await System.Threading.Tasks.Task.Run(() => restart.Invoke(field, null));   // the edit came from another thread
            Headless.Pump();
            await Wait(300);
            Assert.False(field.Editor.HasOpenUndoGroup);   // the timer started on the UI thread and closed the step

            field.Type("y");
            field.Editor.TypingPause = System.TimeSpan.FromMilliseconds(40);
            await System.Threading.Tasks.Task.Run(() => restart.Invoke(field, null));
            window.Content = null;                          // the field leaves the screen before the posted callback runs
            Headless.Pump();
            await Wait(300);
            Assert.False(field.Editor.HasOpenUndoGroup);   // leaving closed the step; the callback saw an off-screen field and started no timer
            Assert.False(PauseTimerRunning(field));

            // The step is open again while the field is off screen, so only the on-screen check keeps the callback from starting a timer.
            field.Type("z");
            Assert.True(field.Editor.HasOpenUndoGroup);
            field.Editor.TypingPause = System.TimeSpan.FromSeconds(30);
            await System.Threading.Tasks.Task.Run(() => restart.Invoke(field, null));
            Headless.Pump();
            Assert.False(PauseTimerRunning(field));
            window.Close();
        });

        static bool PauseTimerRunning(RichTextField field)
        {
            var timer = typeof(RichTextField).GetField("_pauseTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(field) as global::Avalonia.Threading.DispatcherTimer;
            return timer != null && timer.IsEnabled;
        }

        [Fact]
        public void Cut_on_text_that_may_not_be_edited_does_nothing_but_Copy_still_works() => Headless.Run(async () =>
        {
            var locked = new Run { Ws = "en", Text = "locked" };
            locked.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ab" });
            p.Runs.Add(locked);
            var (window, field) = Fixtures.InWindow(Doc(p));
            try
            {
                var top = global::Avalonia.Controls.TopLevel.GetTopLevel(field);
                await ClipboardCompat.SetAsync(top, "sentinel", "");
                field.Editor.Anchor = new TextPosition(0, 1);
                field.Editor.SetCaret(new TextPosition(0, 5), true);     // crosses the locked run
                await field.CopyAsync(true);
                Assert.Equal("sentinel", await ClipboardCompat.GetTextAsync(top));
                Assert.Equal("ablocked", field.Editor.Document.Paragraphs[0].Text);

                await field.CopyAsync(false);
                Assert.Equal("bloc", await ClipboardCompat.GetTextAsync(top));

                await ClipboardCompat.SetAsync(top, "sentinel", "");
                field.IsReadOnly = true;
                field.Editor.Anchor = new TextPosition(0, 0);
                field.Editor.SetCaret(new TextPosition(0, 2), true);
                await field.CopyAsync(true);
                Assert.Equal("sentinel", await ClipboardCompat.GetTextAsync(top));
            }
            finally { window.Close(); }
        });

        [Fact]
        public void An_input_method_cannot_compose_or_commit_into_a_read_only_run_or_field() => Headless.Run(() =>
        {
            var locked = new Run { Ws = "en", Text = "locked" };
            locked.Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(locked);
            var open = Para("en", "en", "free");
            var field = Fixtures.Field(Doc(p, open));

            field.Editor.SetCaret(new TextPosition(0, 3));            // the paragraph has no editable place, so the caret stays
            Assert.Equal(new TextPosition(0, 3), field.Editor.Caret);
            field.SetPreedit("ni", 2);
            Assert.Null(field.Preedit);
            field.Type("é");                                      // the commit arrives as text input
            Assert.Equal("locked", field.Editor.Document.Paragraphs[0].Text);

            field.Editor.SetCaret(new TextPosition(1, 4));
            field.SetPreedit("ni", 2);
            Assert.Equal("ni", field.Preedit);
            field.IsReadOnly = true;                                   // turning read-only on drops a composition in progress
            Assert.Null(field.Preedit);
            field.SetPreedit("ni", 2);
            Assert.Null(field.Preedit);
            field.Type("x");
            Assert.Equal("free", field.Editor.Document.Paragraphs[1].Text);
        });

        [Fact]
        public void Pasting_plain_text_is_one_undo_step_apart_from_the_typing_around_it() => Headless.Run(async () =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("ab"));
            try
            {
                var undo = new SimpleUndoStack();
                undo.Attach(field.Editor);
                field.Editor.SetCaret(new TextPosition(0, 2));
                field.Type("x");
                await ClipboardCompat.SetAsync(global::Avalonia.Controls.TopLevel.GetTopLevel(field), "l1\nl2\nl3", "");
                await field.PasteAsync();
                field.Type("y");
                Assert.Equal(3, field.Editor.Document.Paragraphs.Count);
                Assert.Equal(3, undo.DoneSteps);
                undo.Undo();
                undo.Undo();
                Assert.Single(field.Editor.Document.Paragraphs);
                Assert.Equal("abx", field.Editor.Document.Paragraphs[0].Text);
            }
            finally { window.Close(); }
        });
    }
}
