using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.Fixtures;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>The adapter never hands liblcm a property its own reader refuses (ADR 0016, amended). Such a property stays in the
    /// run model's bag, is left out of the ITsString or StyleRules the adapter builds, and is reported through the callback.</summary>
    public class UnstorablePropertyTests
    {
        static readonly IWritingSystemHandles H = Handles.Instance;

        public static TheoryData<int> Refused() => new TheoryData<int> { 50, 129, 150, 151, 152, 153, 154, 159, 160 };

        static Run RunWith(int tpt)
        {
            var run = new Run { Text = "cell", Ws = "en" };
            run.Props.IntProps[Bold] = new IntPropValue(VarEnum, ForceOn);
            run.Props.IntProps[tpt] = new IntPropValue(0, 1);
            return run;
        }

        static int[] Ints(ITsTextProps p)
        {
            var list = new List<int>();
            for (int i = 0; i < p.IntPropCount; i++) { p.GetIntProp(i, out int t, out _); list.Add(t); }
            return list.OrderBy(x => x).ToArray();
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void A_run_property_liblcm_refuses_is_left_out_and_reported(int tpt)
        {
            var run = RunWith(tpt);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);

            var d = Assert.Single(dropped);
            Assert.Equal(tpt, d.Number);
            Assert.False(d.IsString);
            Assert.Equal(0, d.RunIndex);
            Assert.Null(d.ParagraphIndex);
            Assert.Equal(DroppedProperty.DiagnosticId, d.Id);
            // The string loads back through liblcm's own XML reader, and keeps what liblcm can store.
            var back = TsStringClipboard.FromXml(TsStringClipboard.ToXml(tss, H), H);
            Assert.Equal(new[] { Ws, Bold }, Ints(back.get_Properties(0)));
            // The model is not touched.
            Assert.True(run.Props.IntProps.ContainsKey(tpt));
        }

        [Fact]
        public void No_callback_still_drops_without_throwing()
        {
            var tss = TsStringConversion.ToTsString(new List<Run> { RunWith(152) }, H, "en");
            TsStringClipboard.FromXml(TsStringClipboard.ToXml(tss, H), H);
        }

        [Theory]
        [InlineData(161, false)] [InlineData(9000, false)] [InlineData(9001, true)] [InlineData(4, true)] [InlineData(23, false)]
        public void A_property_liblcm_drops_without_error_still_passes_through_the_bag(int tpt, bool isString)
        {
            var run = new Run { Text = "x", Ws = "en" };
            if (isString) run.Props.StrProps[tpt] = "v"; else run.Props.IntProps[tpt] = new IntPropValue(0, 1);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Empty(dropped);
            var p = tss.get_Properties(0);
            if (isString) Assert.Equal("v", p.GetStrPropValue(tpt)); else Assert.Equal(1, p.GetIntPropValues(tpt, out _));
        }

        [Fact]
        public void Editable_stays_on_a_run_and_is_refused_in_StyleRules()
        {
            var run = new Run { Text = "x", Ws = "en" };
            run.Props.IntProps[155] = new IntPropValue(3, 0);
            var dropped = new List<DroppedProperty>();
            var tss = TsStringConversion.ToTsString(new List<Run> { run }, H, "en", dropped.Add);
            Assert.Empty(dropped);
            Assert.Equal(0, tss.get_Properties(0).GetIntPropValues(155, out _));

            var paragraph = new Paragraph { Style = "Normal" };
            paragraph.Props.IntProps[155] = new IntPropValue(3, 0);
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            Assert.Equal(155, Assert.Single(dropped).Number);
            Assert.Equal(0, rules.IntPropCount);
        }

        [Theory]
        [MemberData(nameof(Refused))]
        public void StyleRules_liblcm_refuses_are_left_out_and_load_back(int tpt)
        {
            var paragraph = new Paragraph { Style = "Table Cell" };
            paragraph.Props.IntProps[tpt] = new IntPropValue(0, 1);
            var dropped = new List<DroppedProperty>();
            var rules = TsStringConversion.ToStyleRules(paragraph, dropped.Add)!;
            var d = Assert.Single(dropped);
            Assert.Equal(tpt, d.Number);
            Assert.Null(d.RunIndex);
            var back = TsStringUtils.GetTextProps(XElement.Parse(TsStringUtils.GetXmlRep(rules, null!)), null!);
            Assert.Equal("Table Cell", back.GetStrPropValue(NamedStyle));
        }

        [Fact]
        public void StyleRules_holding_only_refused_properties_becomes_null()
        {
            var paragraph = new Paragraph();
            paragraph.Props.IntProps[152] = new IntPropValue(0, 1);
            Assert.Null(TsStringConversion.ToStyleRules(paragraph));
        }

        [Fact]
        public void A_document_reports_the_paragraph_and_run()
        {
            var doc = new Document();
            doc.Paragraphs.Add(new Paragraph { Runs = new RunList(new[] { new Run { Text = "a", Ws = "en" } }), DefaultWs = "en" });
            doc.Paragraphs.Add(new Paragraph { Runs = new RunList(new[] { new Run { Text = "b", Ws = "en" }, RunWith(154) }), DefaultWs = "en" });
            var dropped = new List<DroppedProperty>();
            var result = TsStringConversion.ToTextParagraphs(doc, H, dropped.Add);
            var d = Assert.Single(dropped);
            Assert.Equal((1, 1, 154), (d.ParagraphIndex, d.RunIndex, d.Number));
            TsStringClipboard.FromXml(TsStringClipboard.ToXml(result[1].Contents, H), H);
        }

        [Fact]
        public void A_copied_fragment_loads_back_without_the_refused_property()
        {
            var p = new Paragraph { Runs = new RunList(new[] { RunWith(160) }), DefaultWs = "en" };
            var dropped = new List<DroppedProperty>();
            var xml = TsStringClipboard.FragmentToXml(new[] { p }, H, onDropped: dropped.Add);
            Assert.Single(dropped);
            TsStringClipboard.FromXml(xml, H);
        }
    }
}
