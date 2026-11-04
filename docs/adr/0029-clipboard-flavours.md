# Copy puts five forms on the clipboard; paste takes the richest one it can read

**Copy** puts these on the clipboard:
- FieldWorks' TsString format;
- FieldWorks Markdown, with its front matter (ADR 0028);
- HTML;
- RTF;
- plain text, in NFC (ADR 0018).

**Paste** prefers TsString, then FieldWorks Markdown, then plain text. HTML and RTF are written for other programs but
not read, for now.

A writing system that the target project lacks is mapped to the writing system of the selection, as FieldWorks does,
and the user sees a notice that it happened.

A **Copy as ▸** menu forces one form: FieldWorks Markdown, plain text or HTML.

**Consequences:** Markdown on the clipboard always carries its front matter, so unmarked text never takes its writing
system from the target project's defaults. This refines ADR 0016, which put only the TsString format in the adapter
package.

Owner decision, 2026-11-04.
