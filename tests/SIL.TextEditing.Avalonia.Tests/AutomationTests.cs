using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Media;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    public class AutomationTests
    {
        static AutomationPeer Peer(RichTextField f) => ControlAutomationPeer.CreatePeerForElement(f);

        [Fact]
        public void The_peer_is_an_edit_with_the_host_name_and_the_text_as_its_value() => Headless.Run(() =>
        {
            var doc = Doc(Para("en", "en", "one"), Para("en", "en", "two"));
            doc.Paragraphs[1].Runs.Add(Run.Object("en", "\u0008pic"));
            var field = Fixtures.Field(doc);
            AutomationProperties.SetName(field, "Gloss");
            var peer = Peer(field);
            Assert.Equal(AutomationControlType.Edit, peer.GetAutomationControlType());
            Assert.Equal("Gloss", peer.GetName());
            Assert.True(peer.IsKeyboardFocusable());
            Assert.False(peer.HasKeyboardFocus());
            Assert.Equal("one\r\ntwo\uFFFC", ((IValueProvider)peer).Value);
            Assert.False(((IValueProvider)peer).IsReadOnly);
        });

        [Fact]
        public void Without_a_host_name_the_peer_has_no_hard_coded_one() => Headless.Run(() =>
        {
            Assert.DoesNotContain("Rich text field", Peer(Fixtures.Field(OneParagraph("a"))).GetName() ?? "");
        });

        [Fact]
        public void The_value_follows_typing() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("ab"));
            var peer = Peer(field);
            Assert.Equal("ab", ((IValueProvider)peer).Value);
            field.Editor.SetCaret(new TextPosition(0, 2));
            field.Type("c");
            Assert.Equal("abc", ((IValueProvider)peer).Value);
        });

        [Fact]
        public void The_peer_reports_keyboard_focus() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("ab"));
            var peer = Peer(field);
            Assert.False(peer.HasKeyboardFocus());
            Assert.True(peer.GetBoundingRectangle().Width > 0 && peer.GetBoundingRectangle().Height > 0);
            field.Focus();
            Headless.Pump();
            Assert.True(peer.HasKeyboardFocus());
            window.Close();
        });

        [Fact]
        public void Selection_and_caret_colours_follow_theme_resources_with_the_old_look_as_fallback() => Headless.Run(() =>
        {
            var field = Fixtures.Field(OneParagraph("ab"));
            Assert.Equal(Colors.LightSkyBlue, ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            Assert.Equal(Colors.Black, ((ISolidColorBrush)field.ResolveCaretBrush()).Color);
            field.Resources["TextControlSelectionHighlightColor"] = Color.Parse("#112233");
            field.Resources["TextControlForeground"] = new SolidColorBrush(Color.Parse("#445566"));
            Assert.Equal(Color.Parse("#112233"), ((ISolidColorBrush)field.ResolveSelectionBrush()).Color);
            Assert.Equal(Color.Parse("#445566"), ((ISolidColorBrush)field.ResolveCaretBrush()).Color);
        });
    }
}
