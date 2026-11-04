using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace SIL.TextEditing
{
    /// <summary>One way a selection travels on the clipboard (ADR 0029). The host registers the forms it can write and read: a
    /// FieldWorks host registers the TsString form through the LCModel adapter and FieldWorks Markdown through the Markdown
    /// library; plain text is built into the field and needs no registration. Every form works on text: a payload is a string,
    /// so the field takes no dependency on the library that writes it.</summary>
    public interface IClipboardForm
    {
        /// <summary>The stable name the paste order and "Copy as" use, such as <see cref="ClipboardForms.TsStringId"/>.</summary>
        string Id { get; }

        /// <summary>The name a menu shows for "Copy as" (see <see cref="OffersCopyAs"/>).</summary>
        string DisplayName { get; }

        /// <summary>The platform clipboard format name the payload is stored under ("TsString" for FieldWorks' own format).</summary>
        string FormatName { get; }

        /// <summary>True when "Copy as" offers this form on its own. A form that only exists to talk to another program, such as
        /// the TsString XML, says false.</summary>
        bool OffersCopyAs { get; }

        /// <summary>The payload for a copied selection (the fragment <see cref="DocumentEditor.GetFragment"/> returns), or null when
        /// the form cannot carry it. The field skips a form that returns null or throws; the other forms still go on the clipboard.</summary>
        string? Write(IReadOnlyList<Paragraph> fragment);

        /// <summary>The paragraphs a payload holds, or null when it is not this form's (the field then tries the next form in the paste
        /// order). The paragraphs are new objects with no keys; the field maps writing systems the target lacks (ADR 0029) before
        /// inserting them, and the editor normalises their text. A form that throws counts as null.</summary>
        IReadOnlyList<Paragraph>? Read(string payload);
    }

    /// <summary>The clipboard forms a field knows and the order paste reads them in (ADR 0029). Plain text is always available under
    /// <see cref="PlainTextId"/> and is written on every copy; a form is only read when its id is in <see cref="PasteOrder"/>.</summary>
    public sealed class ClipboardForms : IEnumerable<IClipboardForm>
    {
        /// <summary>The id of the built-in plain-text form in <see cref="PasteOrder"/> and in "Copy as".</summary>
        public const string PlainTextId = "text/plain";

        /// <summary>The id FieldWorks' TsString form (registered through the LCModel adapter) uses.</summary>
        public const string TsStringId = "TsString";

        /// <summary>The id the FieldWorks Markdown form (registered through the Markdown library) uses.</summary>
        public const string FieldWorksMarkdownId = "FieldWorks Markdown";

        readonly List<IClipboardForm> _forms = new List<IClipboardForm>();

        public ClipboardForms()
        {
            PasteOrder = new List<string> { TsStringId, FieldWorksMarkdownId, PlainTextId };
        }

        /// <summary>The ids paste tries, first to last: the first one the clipboard holds and that reads wins. The default is FieldWorks'
        /// own order, TsString, then FieldWorks Markdown, then plain text. The host may replace the list or edit it; an id with no
        /// registered form (and not <see cref="PlainTextId"/>) is skipped, and a form not in the list is never read.</summary>
        public IList<string> PasteOrder { get; set; }

        /// <summary>Registers a form; one with the same <see cref="IClipboardForm.Id"/> is replaced.</summary>
        public ClipboardForms Add(IClipboardForm form)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            Remove(form.Id);
            _forms.Add(form);
            return this;
        }

        public bool Remove(string id) => _forms.RemoveAll(f => f.Id == id) > 0;

        public IClipboardForm? Find(string id) => _forms.FirstOrDefault(f => f.Id == id);

        /// <summary>The forms "Copy as" lists, in registration order, not counting plain text, which is always offered.</summary>
        public IEnumerable<IClipboardForm> CopyAsForms => _forms.Where(f => f.OffersCopyAs);

        public IEnumerator<IClipboardForm> GetEnumerator() => _forms.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>What <see cref="ClipboardWritingSystems.MapMissing"/> did.</summary>
    public sealed class WritingSystemMapping : EventArgs
    {
        public WritingSystemMapping(IReadOnlyList<string> missing, string target)
        {
            Missing = missing;
            Target = target;
        }

        /// <summary>The writing systems of the pasted text that the target lacks, each once, in the order they appear. An empty id
        /// (a payload that named none) is listed as an empty string.</summary>
        public IReadOnlyList<string> Missing { get; }

        /// <summary>The writing system they were mapped to: the one of the selection the text replaced.</summary>
        public string Target { get; }

        /// <summary>True when any writing system was mapped (so the host may show a notice).</summary>
        public bool Any => Missing.Count > 0;
    }

    /// <summary>The writing-system rule of paste (ADR 0029, as FieldWorks does it): text in a writing system the target project lacks
    /// takes the writing system of the selection it lands in.</summary>
    public static class ClipboardWritingSystems
    {
        /// <summary>Maps every run of the fragment whose writing system <paramref name="isKnown"/> refuses (an empty id is always
        /// refused) to <paramref name="target"/>, and every paragraph default likewise; runs that become alike merge. The fragment is
        /// changed in place, so it must be new objects (a form's <see cref="IClipboardForm.Read"/> gives that).</summary>
        public static WritingSystemMapping MapMissing(IEnumerable<Paragraph> fragment, Func<string, bool> isKnown, string target)
        {
            var missing = new List<string>();
            bool Unknown(string? ws)
            {
                string id = ws ?? string.Empty;
                bool bad = id.Length == 0 || !isKnown(id);
                if (bad && !missing.Contains(id)) missing.Add(id);
                return bad;
            }
            foreach (var p in fragment)
            {
                bool changed = false;
                foreach (var r in p.Runs)
                    if (Unknown(r.Ws)) { r.Ws = target; changed = true; }
                if (Unknown(p.DefaultWs)) p.DefaultWs = target;
                if (changed) p.Normalize();
            }
            return new WritingSystemMapping(missing, target);
        }
    }

    /// <summary>The field's own debug and test dump (<see cref="Interchange"/>) as a clipboard form. A host never registers it for
    /// users (ADR 0029 retired it from the clipboard); tests and the playground do, so that formatting travels without liblcm or the
    /// Markdown library.</summary>
    public sealed class InterchangeClipboardForm : IClipboardForm
    {
        public string Id => "SIL.TextEditing.Runs.v1";
        public string DisplayName => "Runs (debug dump)";
        public string FormatName => Interchange.ClipboardFormat;
        public bool OffersCopyAs => true;
        public string? Write(IReadOnlyList<Paragraph> fragment) => Interchange.ToXml(fragment);
        public IReadOnlyList<Paragraph>? Read(string payload) => Interchange.FromXml(payload);
    }
}
