using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>A writing system the host did not register is drawn at FieldWorks' 10 point default (owner decision, 2026-11-04).</summary>
    public class FallbackTests
    {
        [Fact]
        public void An_unregistered_writing_system_is_10_point_Segoe_UI()
        {
            var info = new WritingSystemRegistry().Get("zz");
            Assert.Equal(10, info.Size);
            Assert.Equal("Segoe UI", info.Font);
            Assert.Equal("zz", info.Tag);
        }

        [Fact]
        public void A_registered_writing_system_keeps_its_own_size()
        {
            var registry = new WritingSystemRegistry().Add(WritingSystemRegistry.Define("en", "Segoe UI", 14));
            Assert.Equal(14, registry.Get("en").Size);
        }

        [Fact]
        public void A_field_with_no_registered_writing_systems_lays_text_out_at_10_point() => Headless.Run(() =>
        {
            var plain = new RichTextField(OneParagraph("Hello world"));
            Fixtures.Layout(plain, 400);
            var sixteen = Fixtures.Field(OneParagraph("Hello world"), 400);
            double ratio = plain.ParagraphLayoutFor(0).Width / sixteen.ParagraphLayoutFor(0).Width;
            Assert.InRange(ratio, 0.55, 0.7);   // 10 / 16 = 0.625
        });
    }
}
