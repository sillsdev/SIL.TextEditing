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
