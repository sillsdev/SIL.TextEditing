using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Tests
{
    public class InterchangeTests
    {
        [Fact]
        public void Xml_keeps_paragraph_key_direction_writing_systems_and_exact_text()
        {
            var p = Para("ar", "en", "a\u0001b\\c", "ar", "مرحبا");
            p.Rtl = true;
            p.Style = "Normal";
            p.Key = "key0";
            p.Runs[0].Props.SetInt(TextPropertyNumbers.FontSize, 12000, TextPropertyNumbers.VariationMilliPoint);
            var back = Interchange.FromXml(Interchange.ToXml(new[] { p }));
            Assert.Single(back);
            Assert.Equal("key0", back[0].Key);
            Assert.Null(Interchange.FromXml(Interchange.ToXml(new[] { Para("en", "en", "x") }))[0].Key);
            Assert.True(back[0].Rtl);
            Assert.Equal("Normal", back[0].Style);
            Assert.Equal("ar", back[0].DefaultWs);
            Assert.Equal(p.Runs.Select(r => r.Text), back[0].Runs.Select(r => r.Text));
            Assert.Equal(p.Runs.Select(r => r.Ws), back[0].Runs.Select(r => r.Ws));
            Assert.Equal(new IntPropValue(TextPropertyNumbers.VariationMilliPoint, 12000), back[0].Runs[0].Props.IntProps[TextPropertyNumbers.FontSize]);
        }

        [Fact]
        public void Text_written_by_another_producer_pastes_with_its_runs_and_properties()
        {
            // As an outside writer would produce it: plain attributes, UTF-16 text with a combining mark and an emoji.
            var xml = "<Runs><Para id=\"" + System.Guid.NewGuid() + "\" style=\"\" rtl=\"0\" defws=\"fr\">"
                + "<Run ws=\"fr\" i_4242=\"0:42\">from outside é́ \U0001F600</Run></Para></Runs>";
            var e = new DocumentEditor(OneParagraph("host: "));
            e.SetCaret(new TextPosition(0, 6));
            e.Paste(Interchange.FromXml(xml));
            var p = e.Document.Paragraphs[0];
            Assert.Equal("host: from outside é́ \U0001F600", p.Text);
            Assert.Equal("fr", p.Runs.Last().Ws);
            Assert.True(p.Runs.Last().Props.IntProps.ContainsKey(4242));
        }

        [Fact]
        public void Json_dump_differs_when_a_property_differs()
        {
            var a = OneParagraph("x");
            var b = a.Clone();
            Assert.Equal(Interchange.Json(a), Interchange.Json(b));
            b.Paragraphs[0].Runs[0].Props.SetStr(1, "Arial");
            Assert.NotEqual(Interchange.Json(a), Interchange.Json(b));
        }
    }
}
