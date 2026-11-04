using System;
using System.Collections.Generic;

namespace SIL.TextEditing.LCModel
{
    /// <summary>FieldWorks' "TsString" clipboard form (ADR 0029), for a host to register on a field:
    /// <c>field.ClipboardForms.Add(new TsStringClipboardForm(handles))</c>. Copy writes the selection as the XML of one ITsString
    /// (<see cref="TsStringClipboard.ToPayload"/>: paragraphs joined with <see cref="TsStringClipboard.FieldWorksParagraphSeparator"/>,
    /// as FieldWorks' own copy does) and paste reads one, so a FieldWorks paste of our copy agrees with its own. By default the pasted
    /// string is one paragraph, as FieldWorks pastes it; <see cref="PasteParagraphSeparator"/> splits it at a separator instead. A
    /// writing system the payload names that the host's handles do not know keeps its id (it is given a handle only for the length
    /// of the read), so the field can tell it is missing and map it to the writing system of the selection
    /// (<see cref="ClipboardWritingSystems"/>).</summary>
    public sealed class TsStringClipboardForm : IClipboardForm
    {
        readonly IWritingSystemHandles _writingSystems;

        public TsStringClipboardForm(IWritingSystemHandles writingSystems)
        {
            _writingSystems = writingSystems ?? throw new ArgumentNullException(nameof(writingSystems));
        }

        /// <summary>What goes between paragraphs when a copy covers several; FieldWorks' own copy uses "; ".</summary>
        public string WriteParagraphSeparator { get; set; } = TsStringClipboard.FieldWorksParagraphSeparator;

        /// <summary>When set, a pasted string is split into paragraphs at this text; null (FieldWorks) keeps it as one paragraph.</summary>
        public string? PasteParagraphSeparator { get; set; }

        public string Id => ClipboardForms.TsStringId;
        public string DisplayName => "TsString";
        public string FormatName => TsStringClipboard.FormatName;
        public bool OffersCopyAs => false;

        public string? Write(IReadOnlyList<Paragraph> fragment) =>
            fragment.Count == 0 ? null : TsStringClipboard.ToPayload(fragment, _writingSystems, WriteParagraphSeparator).TsStringXml;

        public IReadOnlyList<Paragraph>? Read(string payload)
        {
            if (payload.IndexOf("<Str", StringComparison.Ordinal) < 0) return null;   // not a TsString payload
            return TsStringClipboard.FromXmlToFragment(payload, new ReadHandles(_writingSystems), PasteParagraphSeparator);
        }

        /// <summary>The host's handles for one read, plus a temporary handle for each id the host does not know: LCModel refuses a
        /// string whose writing system has no handle, and a pasted string may come from a project that has more writing systems.</summary>
        sealed class ReadHandles : IWritingSystemHandles
        {
            const int FirstTemporary = 0x40000000;
            readonly IWritingSystemHandles _host;
            readonly List<string> _unknown = new List<string>();

            public ReadHandles(IWritingSystemHandles host) { _host = host; }

            public int GetHandle(string wsId)
            {
                int handle = _host.GetHandle(wsId);
                if (handle != 0 || string.IsNullOrEmpty(wsId)) return handle;
                int at = _unknown.IndexOf(wsId);
                if (at < 0) { _unknown.Add(wsId); at = _unknown.Count - 1; }
                return FirstTemporary + at;
            }

            public string GetId(int handle)
            {
                int at = handle - FirstTemporary;
                return at >= 0 && at < _unknown.Count ? _unknown[at] : _host.GetId(handle);
            }
        }
    }
}
