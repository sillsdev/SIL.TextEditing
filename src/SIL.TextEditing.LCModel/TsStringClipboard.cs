using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel
{
    /// <summary>What a host puts on the clipboard for a selection: FieldWorks' "TsString" XML, written by LCModel's own
    /// serializer, next to plain text in NFC for other programs (ADR 0018).</summary>
    public sealed class TsStringClipboardPayload
    {
        public TsStringClipboardPayload(string tsStringXml, string plainTextNfc)
        {
            TsStringXml = tsStringXml;
            PlainTextNfc = plainTextNfc;
        }

        public string TsStringXml { get; }
        public string PlainTextNfc { get; }
    }

    /// <summary>The "TsString" clipboard format, through <see cref="TsStringSerializer"/>. The XML holds one ITsString.
    /// A selection over several paragraphs is joined with a line feed first, as the plain text is, so the paragraph
    /// keys stay out of the clipboard and a paste creates new paragraphs.</summary>
    public static class TsStringClipboard
    {
        /// <summary>The clipboard format name FieldWorks registers for this payload.</summary>
        public const string FormatName = "TsString";

        public static string ToXml(ITsString text, IWritingSystemHandles writingSystems)
            => TsStringSerializer.SerializeTsStringToXml(text, new HandlesFactory(writingSystems), 0, true, false);

        public static ITsString FromXml(string xml, IWritingSystemHandles writingSystems)
            => TsStringSerializer.DeserializeTsStringFromXml(xml, new HandlesFactory(writingSystems));

        /// <summary>The payload for a fragment, as <c>DocumentEditor.GetFragment</c> returns it.</summary>
        public static TsStringClipboardPayload ToPayload(IReadOnlyList<Paragraph> fragment, IWritingSystemHandles writingSystems)
        {
            var joined = new List<Run>();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0) joined.Add(new Run { Text = "\n", Ws = fragment[i].DefaultWs });
                joined.AddRange(fragment[i].Runs);
            }
            string defaultWs = fragment.Count > 0 ? fragment[0].DefaultWs : "en";
            var text = TsStringConversion.ToTsString(joined, writingSystems, defaultWs);
            string plain = string.Join("\n", fragment.Select(p => p.Text)).Normalize(NormalizationForm.FormC);
            return new TsStringClipboardPayload(ToXml(text, writingSystems), plain);
        }

        /// <summary>The paragraphs a pasted payload holds, split at line feeds, ready for <c>DocumentEditor.Paste</c>.
        /// None carries a key.</summary>
        public static List<Paragraph> FromXmlToFragment(string xml, IWritingSystemHandles writingSystems)
        {
            var text = FromXml(xml, writingSystems);
            var all = TsStringConversion.ToRuns(text, writingSystems);
            var result = new List<Paragraph>();
            var current = new Paragraph();
            foreach (var run in all)
            {
                if (run.IsObject || run.Text.IndexOf('\n') < 0)
                {
                    current.Runs.Add(run);
                    continue;
                }
                var parts = run.Text.Split('\n');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        result.Add(current);
                        current = new Paragraph();
                    }
                    if (parts[i].Length > 0) current.Runs.Add(run.CloneWithText(parts[i]));
                }
            }
            result.Add(current);
            foreach (var p in result.Where(p => p.Runs.Count > 0)) p.DefaultWs = p.Runs[0].Ws;
            return result;
        }
    }
}
