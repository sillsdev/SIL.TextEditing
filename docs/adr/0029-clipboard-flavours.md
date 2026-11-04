# Copy puts TsString, FieldWorks Markdown and plain text on the clipboard; paste prefers TsString

**Copy** puts these on the clipboard:
- FieldWorks' TsString format, as FieldWorks does today;
- FieldWorks Markdown, with its front matter (ADR 0028);
- plain text, in NFC (ADR 0018).

**Paste** prefers TsString, then FieldWorks Markdown, then plain text, so the field behaves as FieldWorks does. The
order is a setting the host can change. The host registers the clipboard forms, so the field itself takes no
dependency on liblcm or on the Markdown converter.

A writing system that the target project lacks is mapped to the writing system of the selection, as FieldWorks does,
and the user sees a notice that it happened.

A **Copy as ▸** menu forces one form: FieldWorks Markdown or plain text.

**Considered options:** HTML and RTF for pasting into word processors (FieldWorks never offered them, and nothing in
the planned scope needs them; either can be added later without changing the format); keeping the field's own
`SIL.TextEditing.Runs.v1` XML (FieldWorks Markdown now carries everything it did, as a documented format, so Runs.v1
stays only as a test and debug dump).

**Consequences:** Markdown on the clipboard always carries its front matter, so unmarked text never takes its writing
system from the target project's defaults. This refines ADR 0016, which put only the TsString format in the adapter
package. FieldWorks Markdown is a back end first (agents, files, drafting); on the clipboard it is an extra form, not
the default.

Owner decision, 2026-11-04.
