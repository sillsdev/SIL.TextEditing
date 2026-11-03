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
    /// A selection over several paragraphs is joined with <see cref="FieldWorksParagraphSeparator"/> first, in the XML and in
    /// the plain text, as FieldWorks' EditingHelper.CopySelection does (its GetSelectionString fallback), so a FieldWorks
    /// paste of the payload agrees with its own copy. The paragraph keys stay out of the clipboard. The field's own
    /// copy inside itself uses <see cref="Interchange"/>, which keeps real paragraphs.</summary>
    public static class TsStringClipboard
    {
        /// <summary>The clipboard format name FieldWorks registers for this payload.</summary>
        public const string FormatName = "TsString";

        /// <summary>What FieldWorks puts between paragraphs of a copied selection.</summary>
        public const string FieldWorksParagraphSeparator = "; ";

        public static string ToXml(ITsString text, IWritingSystemHandles writingSystems)
            => TsStringSerializer.SerializeTsStringToXml(text, new HandlesFactory(writingSystems), 0, true, false);

        public static ITsString FromXml(string xml, IWritingSystemHandles writingSystems)
            => TsStringSerializer.DeserializeTsStringFromXml(xml, new HandlesFactory(writingSystems));

        /// <summary>The payload for a fragment, as <c>DocumentEditor.GetFragment</c> returns it.</summary>
        public static TsStringClipboardPayload ToPayload(IReadOnlyList<Paragraph> fragment, IWritingSystemHandles writingSystems,
            string paragraphSeparator = FieldWorksParagraphSeparator)
        {
            var joined = new List<Run>();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0) joined.Add(new Run { Text = paragraphSeparator, Ws = fragment[i].DefaultWs });
                joined.AddRange(fragment[i].Runs);
            }
            string defaultWs = fragment.Count > 0 ? fragment[0].DefaultWs : "en";
            var text = TsStringConversion.ToTsString(joined, writingSystems, defaultWs);
            string plain = string.Join(paragraphSeparator, fragment.Select(p => p.Text)).Normalize(NormalizationForm.FormC);
            return new TsStringClipboardPayload(ToXml(text, writingSystems), plain);
        }

        /// <summary>The paragraphs a pasted payload holds, ready for <c>DocumentEditor.Paste</c>; none carries a key. With the
        /// default (null) the payload is one paragraph, as FieldWorks pastes it; give a separator to split at it.</summary>
        public static List<Paragraph> FromXmlToFragment(string xml, IWritingSystemHandles writingSystems, string? paragraphSeparator = null)
        {
            var text = FromXml(xml, writingSystems);
            var all = TsStringConversion.ToRuns(text, writingSystems);
            var result = new List<Paragraph>();
            var current = new Paragraph();
            foreach (var run in all)
            {
                if (string.IsNullOrEmpty(paragraphSeparator) || run.IsObject || run.Text.IndexOf(paragraphSeparator, StringComparison.Ordinal) < 0)
                {
                    current.Runs.Add(run);
                    continue;
                }
                var parts = run.Text.Split(new[] { paragraphSeparator! }, StringSplitOptions.None);
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
