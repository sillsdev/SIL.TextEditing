using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>FieldWorks' double-click word stops where the writing system, the spell-check property or the character style changes (issue 116),
    /// but not at a change of bold, size, colour or any other property.</summary>
    public class DoubleClickRunBoundaryTests
    {
        static (int Start, int End) Word(Paragraph p, int at)
        {
            WordBreaks.WordAt(p, at, null, out int s, out int e);
            return (s, e);
        }

        [Fact]
        public void A_word_in_two_writing_systems_is_two_double_click_words()
        {
            var p = Para("en", "en", "ab", "fr", "cd", "fr", " ef");
            Assert.Equal((0, 2), Word(p, 1));
            Assert.Equal((2, 4), Word(p, 2));
            Assert.Equal((2, 4), Word(p, 3));
            Assert.Equal((4, 5), Word(p, 4));
        }

        [Fact]
        public void A_change_of_the_spell_check_property_stops_the_word()
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.SpellCheck, TextPropertyNumbers.SpellCheckDoNot);
            Assert.Equal((0, 2), Word(p, 0));
            Assert.Equal((2, 4), Word(p, 3));
            Assert.Equal((4, 6), Word(p, 4));
        }

        [Fact]
        public void A_change_of_the_character_style_stops_the_word()
        {
            var p = Para("en", "en", "ab", "en", "cd", "en", "ef");
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            Assert.Equal((0, 2), Word(p, 1));
            Assert.Equal((2, 4), Word(p, 2));
            Assert.Equal((4, 6), Word(p, 5));
        }

        [Fact]
        public void Runs_that_differ_only_in_other_properties_stay_one_word()
        {
            var p = Para("en", "en", "ab", "en", "cd");
            p.Runs[1].Props.SetInt(TextPropertyNumbers.Bold, 1).SetInt(TextPropertyNumbers.ForeColor, 0x0000FF);
            Assert.Equal((0, 4), Word(p, 1));
            Assert.Equal((0, 4), Word(p, 3));
        }

        [Fact]
        public void A_white_space_run_and_a_word_split_by_the_style_each_stop_at_the_change()
        {
            var p = Para("en", "en", "ab  ", "en", "  cd");
            p.Runs[1].Props.SetStr(TextPropertyNumbers.NamedStyle, "Emphasis");
            Assert.Equal((2, 4), Word(p, 2));    // the spaces before the style change
            Assert.Equal((4, 6), Word(p, 4));    // the spaces after it
        }

        [Fact]
        public void The_other_word_rules_still_cross_the_boundary()
        {
            var p = Para("en", "en", "ab", "fr", "cd");
            Assert.True(WordBreaks.TryWordAt(p, 1, null, out int s, out int e));
            Assert.Equal((0, 4), (s, e));                                   // the word of a context request and of the spell checker
            Assert.Equal(4, WordBreaks.NextWordStart(p, 0, null));          // Ctrl+Right
            Assert.False(WordBreaks.Boundaries(p, null)[2]);                // whole-word find
        }
    }
}
