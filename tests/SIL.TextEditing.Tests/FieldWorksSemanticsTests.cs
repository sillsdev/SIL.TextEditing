using System.Text;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    /// <summary>ADR 0018: Backspace removes one code point, Delete a base with its marks, and the host's normalisation
    /// form is applied to typed and pasted text. Each rule runs on left-to-right, right-to-left and mixed text.</summary>
    public class FieldWorksSemanticsTests
    {
        // Latin inside Arabic: the mixed line. Arabic letters are U+0639 U+0631 U+0628 U+064A.
        const string Mixed = "عر ab بي";

        static DocumentEditor Editor(string text, bool rtl)
        {
            var p = Para(rtl ? "ar" : "en", "en", text);
            p.Rtl = rtl;
            return new DocumentEditor(Doc(p));
        }

        static string Text(DocumentEditor e) => e.Document.Paragraphs[0].Text;

        [Theory]
        [InlineData("xe\u0301\u0302y", 1, "xy", false)]
        [InlineData("\u0639\u064E\u0651\u0631", 0, "\u0631", true)]   // ain + fatha + shadda, then ra
        [InlineData("\u0639\u0631 ae\u0301 \u0628", 4, "\u0639\u0631 a \u0628", true)]
        public void Delete_removes_one_base_and_all_the_marks_after_it(string text, int at, string expected, bool rtl)
        {
            var e = Editor(text, rtl);
            e.SetCaret(new TextPosition(0, at));
            e.DeleteForward();
            Assert.Equal(expected, Text(e));
            Assert.Equal(at, e.Caret.Offset);
        }

        [Fact]
        public void Delete_is_not_a_grapheme_cluster_delete()
        {
            // Devanagari ka + virama + ssa is one cluster (GB9c); Delete takes ka and its mark, leaving ssa.
            var e = Editor("क्ष", false);
            e.DeleteForward();
            Assert.Equal("ष", Text(e));
        }

        [Fact]
        public void Delete_removes_a_surrogate_pair_and_the_marks_after_it_as_one_step()
        {
            var e = Editor("\U0001D157\U0001D165z", false);   // musical note + combining stem (Mc, a pair)
            e.DeleteForward();
            Assert.Equal("z", Text(e));
        }

        [Theory]
        [InlineData("abc", false)]
        [InlineData("عرب", true)]
        [InlineData(Mixed, true)]
        public void Backspace_removes_one_code_point_even_a_combining_mark_alone(string text, bool rtl)
        {
            var e = Editor(text + "é", rtl);
            e.SetCaret(new TextPosition(0, text.Length + 2));
            e.Backspace();
            Assert.Equal(text + "e", Text(e));
            e.Backspace();
            Assert.Equal(text, Text(e));
            var s = Editor("a\U0001F600b", rtl);
            s.SetCaret(new TextPosition(0, 3));
            s.Backspace();
            Assert.Equal("ab", Text(s));
        }

        [Fact]
        public void Backspace_and_delete_remove_an_object_in_one_step_in_mixed_text()
        {
            var p = Para("ar", "ar", "عر ab ");
            p.Rtl = true;
            p.Runs.Add(Run.Object("ar", "\u0008pic"));
            p.Runs.Add(new Run { Ws = "ar", Text = "ب" });
            var e = new DocumentEditor(Doc(p));
            e.SetCaret(new TextPosition(0, 7));
            e.Backspace();
            Assert.Equal(7, e.Document.Paragraphs[0].Length);
            Assert.Equal("عر ab ب", Text(e));
            var q = new DocumentEditor(Doc(Para("ar", "ar", "عر ab ")));
            q.Document.Paragraphs[0].Runs.Add(Run.Object("ar", "\u0008pic"));
            q.Document.Paragraphs[0].Runs.Add(new Run { Ws = "ar", Text = "ب" });
            q.SetCaret(new TextPosition(0, 6));
            q.DeleteForward();
            Assert.Equal("عر ab ب", Text(q));
        }

        [Theory]
        [InlineData("café", false)]
        [InlineData("أب", true)]            // alef with hamza above: decomposes under NFD
        [InlineData("ab é أ", true)]
        public void Typed_text_is_converted_to_the_hosts_form_and_the_caret_follows_it(string typed, bool rtl)
        {
            var e = Editor("x", rtl);
            e.Document.Normalization = NormalizationForm.FormD;
            e.SetCaret(new TextPosition(0, 1));
            e.TypeText(typed);
            var nfd = typed.Normalize(NormalizationForm.FormD);
            Assert.Equal("x" + nfd, Text(e));
            Assert.Equal(1 + nfd.Length, e.Caret.Offset);
        }

        [Fact]
        public void Without_a_form_text_is_inserted_as_it_arrives()
        {
            var e = Editor("x", false);
            e.SetCaret(new TextPosition(0, 1));
            e.TypeText("é");
            Assert.Equal("xé", Text(e));
        }

        [Fact]
        public void Pasted_runs_are_converted_but_keep_writing_system_and_properties()
        {
            var src = new DocumentEditor(Doc(Para("fr", "fr", "café")));
            src.Anchor = new TextPosition(0, 0);
            src.SetCaret(new TextPosition(0, 4), true);
            var frag = src.GetFragment();
            frag[0].Runs[0].Props.SetInt(4242, 7);
            var e = Editor("", false);
            e.Document.Normalization = NormalizationForm.FormD;
            e.Paste(frag);
            Assert.Equal("café", Text(e));
            Assert.Equal("fr", e.Document.Paragraphs[0].Runs[0].Ws);
            Assert.Equal(7, e.Document.Paragraphs[0].Runs[0].Props.IntProps[4242].Value);
            Assert.Equal(5, e.Caret.Offset);
            Assert.Equal("café", frag[0].Text);   // the source fragment is untouched
        }
    }
}
