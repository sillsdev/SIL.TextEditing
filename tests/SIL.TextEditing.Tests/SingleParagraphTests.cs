using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0012: in single-paragraph mode the field never makes a second paragraph.</summary>
    public class SingleParagraphTests
    {
        static DocumentEditor Single(string text)
        {
            var e = new DocumentEditor(OneParagraph(text)) { SingleParagraph = true };
            e.SetCaret(new TextPosition(0, text.Length));
            return e;
        }

        [Fact]
        public void Enter_does_nothing_in_the_editor_and_keeps_the_selection()
        {
            var e = Single("abc");
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 1));
            e.SetCaret(new TextPosition(0, 3), true);
            e.Enter();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abc", e.Document.Paragraphs[0].Text);
            Assert.True(e.HasSelection);
            Assert.Empty(undo.Done);
        }

        [Fact]
        public void A_line_break_in_typed_text_becomes_a_space()
        {
            var e = Single("ab");
            e.TypeText("x\ny\r\nz");
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abx y z", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Pasted_paragraphs_are_joined_into_one_with_a_space_in_the_text_before_it()
        {
            var e = Single("ab");
            var one = Para("en", "en", "one");
            var two = Para("fr", "fr", "deux");
            two.Style = "Other";
            var three = Para("en", "en", "three");
            e.Paste(new List<Paragraph> { one, two, three });
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("abone deux three", e.Document.Paragraphs[0].Text);
            Assert.Equal(new TextPosition(0, 16), e.Caret);
            // the space after "one" is English and the one after "deux" is French, so they merge into the runs before them
            var runs = e.Document.Paragraphs[0].Runs;
            Assert.Equal(new[] { "abone ", "deux ", "three" }, new[] { runs[0].Text, runs[1].Text, runs[2].Text });
            Assert.Equal("fr", runs[1].Ws);
        }

        [Fact]
        public void A_paste_of_one_paragraph_and_ordinary_typing_work_as_before()
        {
            var e = Single("ab");
            e.Paste(new List<Paragraph> { Para("en", "en", "XY") });
            e.TypeText("!");
            Assert.Equal("abXY!", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void A_document_with_several_paragraphs_keeps_them_and_Backspace_may_still_join()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), Para("en", "en", "two"))) { SingleParagraph = true };
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal("onetwo", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Turning_the_mode_off_restores_Enter()
        {
            var e = Single("ab");
            e.SingleParagraph = false;
            e.Enter();
            Assert.Equal(2, e.Document.Paragraphs.Count);
        }
    }
}
