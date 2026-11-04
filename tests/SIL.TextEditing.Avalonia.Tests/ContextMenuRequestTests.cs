using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>ADR 0021, ADR 0025: one context request gives one event; a host builds its menu from the event or sets Avalonia's
    /// ContextMenu on the field.</summary>
    public class ContextMenuRequestTests
    {
        static Point CenterOfFirstLine(Window window, RichTextField field)
        {
            var r = field.ParagraphLayoutFor(0).SelectionRects(0, 2)[0];
            return field.TranslatePoint(new Point(r.X + r.Width / 2, r.Y + r.Height / 2), window)!.Value;
        }

        [Fact]
        public void A_right_click_raises_one_request_for_the_pointer_and_the_Menu_key_one_for_the_caret() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello world"));
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            var at = CenterOfFirstLine(window, field);
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            Headless.Pump();
            Assert.Single(seen);
            Assert.Equal(ContextMenuTrigger.Pointer, seen[0].Trigger);
            Assert.Equal("hello", seen[0].Info.Word);

            field.Editor.SetCaret(new TextPosition(0, 8));
            field.Focus();
            Headless.Pump();
            field.Press(Key.Apps);
            Assert.Equal(2, seen.Count);
            Assert.Equal(ContextMenuTrigger.Keyboard, seen[1].Trigger);
            Assert.Equal("world", seen[1].Info.Word);
            Assert.True(seen[1].Position.Y >= field.CaretRect().Bottom - 1);
            window.Close();
        });

        [Fact]
        public void Shift_F10_raises_the_request_only_when_a_host_listens_and_leaves_the_key_alone_otherwise() => Headless.Run(() =>
        {
            var (window, field) = Fixtures.InWindow(OneParagraph("hello world"));
            Assert.False(field.HandleKey(Key.F10, KeyModifiers.Shift));
            var seen = new List<ContextMenuRequestedEventArgs>();
            field.ContextMenuRequested += (o, e) => seen.Add(e);
            Assert.True(field.HandleKey(Key.F10, KeyModifiers.Shift));
            Assert.Single(seen);
            Assert.False(field.HandleKey(Key.F10, KeyModifiers.Shift | KeyModifiers.Control));
            Assert.False(field.HandleKey(Key.F10, KeyModifiers.None));
            window.Close();
        });
    }
}
