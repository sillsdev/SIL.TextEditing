using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    public class ActiveWritingSystemTests
    {
        [Fact]
        public void Active_ws_follows_the_caret()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 9)); // inside "bonjour"
            Assert.Equal("fr", e.ActiveWs);
            e.SetCaret(new TextPosition(0, 2));
            Assert.Equal("en", e.ActiveWs);
        }

        [Fact]
        public void Typing_uses_the_chosen_ws_not_the_preceding_run()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 13)); // right after "bonjour"
            Assert.Equal("fr", e.ActiveWs);
            e.SetActiveWs("en");
            e.TypeText("X");
            Assert.Equal("en", WsOfCharAt(e, 0, 13));
        }

        [Fact]
        public void Typing_without_an_override_uses_the_run_at_the_caret()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 13));
            var expected = e.ActiveWs;
            e.TypeText("Y");
            Assert.Equal(expected, WsOfCharAt(e, 0, 13));
        }

        [Fact]
        public void Empty_paragraph_uses_its_default_ws()
        {
            var e = new DocumentEditor(Doc(Para("ta"), Para("en", "en", "x")));
            e.SetCaret(new TextPosition(0, 0));
            Assert.Equal("ta", e.ActiveWs);
        }

        [Fact]
        public void Selection_reports_every_ws_it_touches()
        {
            var e = new DocumentEditor(Basic());
            e.SetCaret(new TextPosition(0, 2));
            e.SetCaret(new TextPosition(0, 10), true);
            Assert.Equal(new[] { "en", "fr" }, e.SelectionWsSet());
            e.SetCaret(new TextPosition(0, 7));
            e.SetCaret(new TextPosition(0, 11), true);
            Assert.Equal(new[] { "fr" }, e.SelectionWsSet());
        }

        [Fact]
        public void Active_ws_change_is_raised_once_with_old_and_new()
        {
            var e = new DocumentEditor(Basic());
            string? seen = null;
            e.ActiveWsChanged += (o, n) => seen = o + ">" + n;
            e.SetCaret(new TextPosition(0, 9));
            Assert.Equal("en>fr", seen);
        }

        static string? WsOfCharAt(DocumentEditor e, int paragraph, int offset)
            => e.Document.Paragraphs[paragraph].RunAfter(offset, out _)?.Ws;
    }
}
