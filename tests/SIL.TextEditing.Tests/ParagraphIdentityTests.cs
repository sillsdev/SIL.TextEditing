using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>Which paragraph keeps its identity when Enter splits one or Backspace and Delete join two.</summary>
    public class ParagraphIdentityTests
    {
        [Fact]
        public void Enter_in_the_middle_leaves_the_old_id_on_the_left()
        {
            var e = new DocumentEditor(OneParagraph("hello world"));
            var id = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(0, 5));
            e.Enter();
            Assert.Equal(id, e.Document.Paragraphs[0].Id);
            Assert.NotEqual(id, e.Document.Paragraphs[1].Id);
            Assert.Equal("hello", e.Document.Paragraphs[0].Text);
            Assert.Equal(" world", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void Enter_at_offset_zero_leaves_the_original_on_the_right()
        {
            var e = new DocumentEditor(OneParagraph("hello"));
            var id = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(0, 0));
            e.Enter();
            Assert.Equal(id, e.Document.Paragraphs[1].Id);
            Assert.NotEqual(id, e.Document.Paragraphs[0].Id);
            Assert.Equal("", e.Document.Paragraphs[0].Text);
            Assert.Equal("hello", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void Enter_at_the_end_leaves_the_old_id_on_the_left()
        {
            var e = new DocumentEditor(OneParagraph("hello"));
            var id = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(0, 5));
            e.Enter();
            Assert.Equal(id, e.Document.Paragraphs[0].Id);
            Assert.Equal("", e.Document.Paragraphs[1].Text);
        }

        [Fact]
        public void Join_of_two_non_empty_paragraphs_keeps_the_first()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "aaa"), Para("en", "en", "bbb")));
            var first = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal(first, e.Document.Paragraphs[0].Id);
            Assert.Equal("aaabbb", e.Document.Paragraphs[0].Text);
        }

        [Fact]
        public void Join_with_an_empty_left_paragraph_keeps_the_right()
        {
            var e = new DocumentEditor(Doc(Para("en"), Para("en", "en", "bbb")));
            var second = e.Document.Paragraphs[1].Id;
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal(second, e.Document.Paragraphs[0].Id);
        }

        [Fact]
        public void Join_with_an_empty_right_paragraph_keeps_the_left()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "aaa"), Para("en")));
            var first = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(1, 0));
            e.Backspace();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal(first, e.Document.Paragraphs[0].Id);
        }

        [Fact]
        public void Delete_at_the_end_joins_with_the_next_paragraph()
        {
            var e = new DocumentEditor(Doc(Para("en", "en", "aaa"), Para("en")));
            var first = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(0, 3));
            e.DeleteForward();
            Assert.Single(e.Document.Paragraphs);
            Assert.Equal(first, e.Document.Paragraphs[0].Id);
        }

        [Fact]
        public void Undo_and_redo_restore_both_ids()
        {
            var e = new DocumentEditor(OneParagraph("hello"));
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            var id = e.Document.Paragraphs[0].Id;
            e.SetCaret(new TextPosition(0, 2));
            e.Enter();
            var right = e.Document.Paragraphs[1].Id;
            undo.Undo();
            Assert.Equal(id, e.Document.Paragraphs[0].Id);
            Assert.Single(e.Document.Paragraphs);
            undo.Redo();
            Assert.Equal(id, e.Document.Paragraphs[0].Id);
            Assert.Equal(right, e.Document.Paragraphs[1].Id);
        }

        [Fact]
        public void Multi_paragraph_paste_gives_new_ids_to_the_inserted_paragraphs()
        {
            var e = new DocumentEditor(OneParagraph("abcdef"));
            var id = e.Document.Paragraphs[0].Id;
            var fragment = new System.Collections.Generic.List<Paragraph> { Para("en", "en", "X"), Para("en", "en", "MID"), Para("en", "en", "Y") };
            e.SetCaret(new TextPosition(0, 3));
            e.Paste(fragment);
            Assert.Equal(new[] { "abcX", "MID", "Ydef" }, e.Document.Paragraphs.ConvertAll(p => p.Text).ToArray());
            Assert.Equal(id, e.Document.Paragraphs[0].Id);
            Assert.NotEqual(e.Document.Paragraphs[1].Id, e.Document.Paragraphs[2].Id);
            Assert.Equal(new TextPosition(2, 1), e.Caret);
        }
    }
}
