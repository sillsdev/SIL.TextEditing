using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.Fixtures;

namespace SIL.TextEditing.LCModel.Tests
{
    public class ClipboardTests
    {
        static readonly IWritingSystemHandles H = Handles.Instance;

        [Theory]
        [InlineData("mixed")]
        [InlineData("arabic")]
        [InlineData("nfd")]
        [InlineData("picture")]
        [InlineData("empty")]
        public void TsString_xml_round_trips_through_LCModels_serializer(string which)
        {
            var original = which switch { "mixed" => Mixed(), "arabic" => Arabic(), "nfd" => Nfd(), "picture" => WithPictureAndLink(), _ => Empty() };
            var xml = TsStringClipboard.ToXml(original, H);
            Assert.StartsWith("<Str", xml.TrimStart());
            Assert.Equal(Dump(original), Dump(TsStringClipboard.FromXml(xml, H)));
        }

        [Fact]
        public void Fragment_payload_has_tsstring_xml_and_nfc_plain_text()
        {
            var doc = TsStringConversion.ToDocument(new[]
            {
                new TextParagraph(null, Nfd(), null),
                new TextParagraph(null, Mixed(), null),
            }, H);
            var payload = TsStringClipboard.ToPayload(doc.Paragraphs, H);
            Assert.Equal("\u00E9col\u00E9 \u00E0\nPlain bold on bold off bold invert styled big red", payload.PlainTextNfc);
            Assert.Contains("<Str", payload.TsStringXml);
        }

        [Fact]
        public void Pasted_payload_becomes_paragraphs_with_their_properties_and_no_keys()
        {
            var doc = TsStringConversion.ToDocument(new[]
            {
                new TextParagraph(System.Guid.NewGuid(), Nfd(), null),
                new TextParagraph(System.Guid.NewGuid(), Mixed(), null),
            }, H);
            var payload = TsStringClipboard.ToPayload(doc.Paragraphs, H);
            var fragment = TsStringClipboard.FromXmlToFragment(payload.TsStringXml, H);
            Assert.Equal(2, fragment.Count);
            Assert.All(fragment, p => Assert.Null(p.Key));
            Assert.Equal("e\u0301cole\u0301 a\u0300", fragment[0].Text);
            Assert.Equal(Dump(Mixed()), Dump(TsStringConversion.ToTsString(fragment[1], H)));
        }
    }
}
