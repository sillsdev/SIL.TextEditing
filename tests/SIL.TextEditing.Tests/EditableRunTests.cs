using System.Collections.Generic;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0019: editability is a run property (ktptEditable) and a field-level flag; selection always works.</summary>
    public class EditableRunTests
    {
        static Run Marked(string text, int editable)
        {
            var r = new Run { Ws = "en", Text = text };
            r.Props.SetInt(TextPropertyNumbers.Editable, editable);
            return r;
        }

        /// <summary>"Gloss: " (not editable) + "abc" (editable) + " [fixed]" (not editable) + "xyz" (editable).</summary>
        static DocumentEditor Labelled(int labelKind = TextPropertyNumbers.EditableNo)
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(Marked("Gloss: ", labelKind));
            p.Runs.Add(new Run { Ws = "en", Text = "abc" });
            p.Runs.Add(Marked(" [fixed]", labelKind));
            p.Runs.Add(new Run { Ws = "en", Text = "xyz" });
            return new DocumentEditor(Doc(p));
        }

        static string Text(DocumentEditor e) => e.Document.Paragraphs[0].Text;

        [Fact]
        public void A_run_without_the_property_is_editable_and_the_values_map_to_FieldWorks_numbers()
        {
            Assert.Equal(RunEditable.Editable, new Run { Text = "a" }.Editable);
            Assert.Equal(RunEditable.NotEditable, Marked("a", 0).Editable);
            Assert.Equal(RunEditable.Editable, Marked("a", 1).Editable);
            Assert.Equal(RunEditable.SemiEditable, Marked("a", 2).Editable);
            Assert.Equal(155, TextPropertyNumbers.Editable);
        }

        [Fact]
        public void A_caret_placed_in_read_only_text_moves_to_the_nearest_place_where_editing_is_allowed()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(7, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 12));
            Assert.Equal(10, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 20));
            Assert.Equal(20, e.Caret.Offset);
        }

        [Fact]
        public void A_direction_makes_the_caret_step_over_read_only_text_instead_of_bouncing_back()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 10));
            e.SetCaret(new TextPosition(0, 11), false, +1);
            Assert.Equal(18, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 12), false, -1);
            Assert.Equal(10, e.Caret.Offset);
        }

        [Fact]
        public void Selection_reaches_read_only_text()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.SetCaret(new TextPosition(0, 2), true);
            Assert.True(e.HasSelection);
            Assert.Equal(2, e.Caret.Offset);
            e.SetCaret(new TextPosition(0, 0));
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 13), true);
            Assert.Equal(13, e.Caret.Offset);
        }

        [Fact]
        public void Typing_at_the_edge_of_read_only_text_lands_in_the_editable_run_without_its_editable_property()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.TypeText("Q");
            Assert.Equal("Gloss: Qabc [fixed]xyz", Text(e));
            var typed = e.Document.Paragraphs[0].Runs[1];
            Assert.Equal("Qabc", typed.Text);
            Assert.False(typed.Props.TryGetInt(TextPropertyNumbers.Editable, out _));
            Assert.Equal(RunEditable.NotEditable, e.Document.Paragraphs[0].Runs[0].Editable);
        }

        [Fact]
        public void Typing_over_a_selection_that_crosses_read_only_text_changes_nothing()
        {
            var e = Labelled();
            e.Anchor = new TextPosition(0, 8);
            e.SetCaret(new TextPosition(0, 12), true);
            e.TypeText("Z");
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            Assert.True(e.HasSelection);
        }

        [Fact]
        public void Delete_backspace_cut_and_paste_are_refused_across_read_only_text()
        {
            var e = Labelled();
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.Anchor = new TextPosition(0, 5);
            e.SetCaret(new TextPosition(0, 9), true);
            e.DeleteSelection("cut");
            e.Backspace();
            e.DeleteForward();
            e.Paste(new List<Paragraph> { Para("en", "en", "P") });
            e.ReplaceRange(new TextPosition(0, 5), new TextPosition(0, 9), null, "cut");
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            Assert.Empty(undo.Done);
        }

        [Fact]
        public void Backspace_and_Delete_at_the_edge_of_read_only_text_do_not_eat_it()
        {
            var e = Labelled();
            e.SetCaret(new TextPosition(0, 7));
            e.Backspace();
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            e.SetCaret(new TextPosition(0, 10));
            e.DeleteForward();
            Assert.Equal("Gloss: abc [fixed]xyz", Text(e));
            e.Backspace();
            Assert.Equal("Gloss: ab [fixed]xyz", Text(e));
        }

        [Fact]
        public void Editable_text_around_read_only_text_still_edits_and_selection_can_be_copied()
        {
            var e = Labelled();
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 21), true);
            Assert.Equal("Gloss: abc [fixed]xyz", string.Concat(e.GetFragment()[0].Text));
            e.Anchor = new TextPosition(0, 8);
            e.SetCaret(new TextPosition(0, 9), true);
            e.DeleteSelection();
            Assert.Equal("Gloss: ac [fixed]xyz", Text(e));
        }

        [Fact]
        public void Semi_editable_text_takes_typing_and_deletion_like_editable_text_but_cannot_be_formatted()
        {
            // FieldWorks' views count semi-editable (type-ahead) text as editable and refuse only formatting in it.
            var e = Labelled(TextPropertyNumbers.EditableSemi);   // "Gloss: " and " [fixed]" are semi-editable
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(3, e.Caret.Offset);                       // the caret stays where it is put
            e.TypeText("X");
            Assert.Equal("GloXss: abc [fixed]xyz", Text(e));
            e.Anchor = new TextPosition(0, 0);
            e.SetCaret(new TextPosition(0, 4), true);
            Assert.True(e.CanEdit);
            Assert.False(e.CanFormatSelection);
            e.DeleteSelection();
            Assert.Equal("ss: abc [fixed]xyz", Text(e));
            e.Anchor = new TextPosition(0, 4);
            e.SetCaret(new TextPosition(0, 7), true);              // "abc", an editable run
            Assert.True(e.CanFormatSelection);
        }

        [Fact]
        public void A_read_only_field_refuses_every_edit_but_keeps_the_caret_and_selection_working()
        {
            var e = new DocumentEditor(Basic()) { IsReadOnly = true };
            var undo = new SimpleUndoStack();
            undo.Attach(e);
            e.SetCaret(new TextPosition(0, 3));
            Assert.Equal(3, e.Caret.Offset);
            e.TypeText("x");
            e.Enter();
            e.Backspace();
            e.DeleteForward();
            e.Paste(new List<Paragraph> { Para("en", "en", "P") });
            e.Anchor = new TextPosition(0, 1);
            e.SetCaret(new TextPosition(0, 5), true);
            Assert.True(e.HasSelection);
            e.DeleteSelection();
            Assert.Equal("Hello bonjour world", Text(e));
            Assert.Empty(undo.Done);
            Assert.False(e.CanEdit);

            e.IsReadOnly = false;
            e.TypeText("x");
            Assert.Equal("Hx bonjour world", Text(e));
        }

        [Fact]
        public void The_editable_places_are_the_stretches_between_read_only_text_and_Tab_steps_through_them()
        {
            var e = Labelled();   // places: [7, 10] and [18, 21]
            e.SetCaret(new TextPosition(0, 8));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Next, out var p));
            Assert.Equal(new TextPosition(0, 18), p);
            e.SetCaret(p);
            Assert.False(e.TryGetEditablePlace(EditablePlace.Next, out _));    // none after the last place: leave
            Assert.True(e.TryGetEditablePlace(EditablePlace.Previous, out p));
            Assert.Equal(new TextPosition(0, 10), p);                          // the end of the previous place
            e.SetCaret(p);
            Assert.False(e.TryGetEditablePlace(EditablePlace.Previous, out _));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Last, out p));
            Assert.Equal(new TextPosition(0, 21), p);
            Assert.True(e.TryGetEditablePlace(EditablePlace.First, out p));
            Assert.Equal(new TextPosition(0, 7), p);
            e.SetCaret(new TextPosition(0, 21));
            Assert.False(e.TryGetEditablePlace(EditablePlace.Last, out _));    // already at the last place
        }

        [Fact]
        public void Each_paragraph_is_an_editable_place_and_a_read_only_field_has_none()
        {
            var locked = Para("en", "en", "locked");
            locked.Runs[0].Props.SetInt(TextPropertyNumbers.Editable, TextPropertyNumbers.EditableNo);
            var e = new DocumentEditor(Doc(Para("en", "en", "one"), locked, Para("en", "en", "three")));
            e.SetCaret(new TextPosition(0, 1));
            Assert.True(e.TryGetEditablePlace(EditablePlace.Next, out var p));
            Assert.Equal(new TextPosition(2, 0), p);                           // over the read-only paragraph
            e.IsReadOnly = true;
            Assert.False(e.TryGetEditablePlace(EditablePlace.Next, out _));
            Assert.False(e.TryGetEditablePlace(EditablePlace.Last, out _));
        }

        [Fact]
        public void Pasting_a_copied_read_only_run_gives_editable_text_that_can_be_deleted_again()
        {
            var p = new Paragraph { DefaultWs = "en" };
            p.Runs.Add(new Run { Ws = "en", Text = "ed" });
            p.Runs.Add(Marked("LOCKED", TextPropertyNumbers.EditableNo));
            p.Runs.Add(new Run { Ws = "en", Text = "it" });
            var e = new DocumentEditor(Doc(p));
            e.Anchor = new TextPosition(0, 2);
            e.SetCaret(new TextPosition(0, 8), true);
            var fragment = e.GetFragment();
            Assert.Equal(RunEditable.NotEditable, fragment[0].Runs[0].Editable);   // copying keeps the property on the clipboard copy

            e.SetCaret(new TextPosition(0, 1));
            e.Paste(fragment);
            Assert.Equal("eLOCKEDdLOCKEDit", Text(e));
            var pasted = e.Document.Paragraphs[0].Runs;
            Assert.Equal(new[] { RunEditable.Editable, RunEditable.NotEditable, RunEditable.Editable }, new[] { pasted[0].Editable, pasted[1].Editable, pasted[2].Editable });
            Assert.Equal("eLOCKEDd", pasted[0].Text);
            Assert.Equal(RunEditable.NotEditable, fragment[0].Runs[0].Editable);   // the caller's fragment is untouched

            e.Anchor = new TextPosition(0, 1);
            e.SetCaret(new TextPosition(0, 7), true);
            Assert.True(e.CanEdit);
            e.DeleteSelection();
            Assert.Equal("edLOCKEDit", Text(e));
        }

        [Fact]
        public void The_host_can_still_apply_changes_to_a_read_only_field()
        {
            var e = new DocumentEditor(OneParagraph("abc")) { IsReadOnly = true };
            var d = new EditDelta { Start = 0, CaretAfter = new TextPosition(0, 1) };
            d.Before.Add(e.Document.Paragraphs[0].Clone());
            d.After.Add(Para("en", "en", "xyz"));
            e.Apply(d);
            Assert.Equal("xyz", Text(e));
        }
    }
}
