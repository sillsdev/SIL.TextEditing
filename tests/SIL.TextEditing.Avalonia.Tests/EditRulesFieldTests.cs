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
    }
}
