using System;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using Xunit;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>What liblcm's own text-props XML serializer (the one a project save and load uses, in SIL.LCModel.Core) does with
    /// each property. These pin facts about liblcm so the adapter's unstorable list (ADR 0016) is checked against the real reader:
    /// when liblcm learns to load one of these back, the matching test fails and the property leaves the list.</summary>
    public class LiblcmPersistenceTests
    {
        public enum Outcome { RoundTrips, Dropped, Throws }

        // (name, property number, is string, variation, value, outcome) — property numbers are facts about the format.
        public static TheoryData<string, int, bool, int, string, Outcome> Cases() => new TheoryData<string, int, bool, int, string, Outcome>
        {
            { "bold (control)", 3, false, 3, "1", Outcome.RoundTrips },
            { "namedStyle (control)", 133, true, 0, "Emphasis", Outcome.RoundTrips },
            { "cellBorderWidth", 152, false, 1, "500", Outcome.Throws },
            { "cellSpacing", 153, false, 1, "500", Outcome.Throws },
            { "cellPadding", 154, false, 1, "500", Outcome.Throws },
            { "setRowDefaults", 159, false, 0, "1", Outcome.Throws },
            { "maxLines", 151, false, 0, "3", Outcome.Throws },
            { "hyphenate", 150, false, 3, "1", Outcome.Throws },
            { "directionDepth", 129, false, 0, "1", Outcome.Throws },
            { "relLineHeight", 160, false, 0, "1", Outcome.Throws },
            { "MarginTop", 50, false, 1, "500", Outcome.Throws },
            { "tabList", 4, true, 0, "x", Outcome.Dropped },
            { "tabDef", 23, false, 1, "36000", Outcome.Dropped },
            { "ktptTableRule", 161, false, 0, "1", Outcome.Dropped },
            { "unknown int", 9000, false, 0, "1", Outcome.Dropped },
            { "unknown string", 9001, true, 0, "x", Outcome.Dropped },
        };

        static ITsTextProps Props(int tpt, bool isString, int variation, string value)
        {
            var b = TsStringUtils.MakePropsBldr();
            if (isString) b.SetStrPropValue(tpt, value);
            else b.SetIntPropValues(tpt, variation, int.Parse(value));
            return b.GetTextProps();
        }

        static bool Same(ITsTextProps a, ITsTextProps b)
            => TsStringUtils.GetXmlRep(a, null!) == TsStringUtils.GetXmlRep(b, null!) && a.IntPropCount == b.IntPropCount && a.StrPropCount == b.StrPropCount;

        [Theory]
        [MemberData(nameof(Cases))]
        public void Props_serializer_outcome(string name, int tpt, bool isString, int variation, string value, Outcome expected)
        {
            var props = Props(tpt, isString, variation, value);
            var xml = TsStringUtils.GetXmlRep(props, null!);
            Outcome actual;
            try
            {
                var back = TsStringUtils.GetTextProps(XElement.Parse(xml), null!);
                actual = Same(props, back) && (isString ? back.StrPropCount == 1 : back.IntPropCount == 1) ? Outcome.RoundTrips : Outcome.Dropped;
            }
            catch (System.Xml.Schema.XmlSchemaException) { actual = Outcome.Throws; }
            Assert.True(expected == actual, $"{name} ({tpt}): expected {expected}, liblcm gave {actual}; xml {xml}");
        }

        [Fact]
        public void Editable_throws_in_StyleRules_but_is_dropped_in_a_run()
        {
            var props = Props(155, false, 3, "1");
            var xml = TsStringUtils.GetXmlRep(props, null!);
            Assert.Throws<System.Xml.Schema.XmlSchemaException>(() => TsStringUtils.GetTextProps(XElement.Parse(xml), null!));
            var b = TsStringUtils.MakePropsBldr();
            b.SetIntPropValues(1, 0, Handles.En);
            b.SetIntPropValues(155, 3, 1);
            var back = TsStringClipboard.FromXml(TsStringClipboard.ToXml(TsStringUtils.MakeString("text", b.GetTextProps()), Handles.Instance), Handles.Instance);
            Assert.Equal(1, back.get_Properties(0).IntPropCount);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Run_serializer_outcome(string name, int tpt, bool isString, int variation, string value, Outcome expected)
        {
            var b = TsStringUtils.MakePropsBldr();
            b.SetIntPropValues(1, 0, Handles.En);
            if (isString) b.SetStrPropValue(tpt, value); else b.SetIntPropValues(tpt, variation, int.Parse(value));
            var tss = TsStringUtils.MakeString("text", b.GetTextProps());
            var xml = TsStringClipboard.ToXml(tss, Handles.Instance);
            Outcome actual;
            try
            {
                var back = TsStringClipboard.FromXml(xml, Handles.Instance);
                var p = back.get_Properties(0);
                actual = (isString ? p.StrPropCount == 2 : p.IntPropCount == 2) || (tpt == 133 && p.StrPropCount == 1) ? Outcome.RoundTrips : Outcome.Dropped;
                if (tpt == 3) actual = p.IntPropCount == 2 ? Outcome.RoundTrips : Outcome.Dropped;
            }
            catch (System.Xml.Schema.XmlSchemaException) { actual = Outcome.Throws; }
            Assert.True(expected == actual, $"{name} ({tpt}): expected {expected}, liblcm run reader gave {actual}; xml {xml}");
        }
    }
}
