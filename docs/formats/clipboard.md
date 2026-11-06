# Clipboard

A field copies a selection in several **clipboard forms** at once and pastes the best one the clipboard holds (ADR 0029). The host
registers the forms. The field itself depends on neither liblcm nor the Markdown library.

## The forms

| Form | Id | Clipboard format name | Payload | Provided by | Default paste order | "Copy as" | Status |
|---|---|---|---|---|---|---|---|
| FieldWorks TsString | `TsString` (`ClipboardForms.TsStringId`) | `TsString` | a host object: FieldWorks' .NET-serialised `TsStringWrapper` | the FieldWorks host | 1st | no | The id ships. The host writes the form and a clipboard backend that can store an object. |
| FieldWorks Markdown | `FieldWorks Markdown` (`ClipboardForms.FieldWorksMarkdownId`) | chosen by the form | text: [FieldWorks Markdown](fieldworks-markdown.md) with its front matter | the host, around `MarkdownConverter` | 2nd | yes | The id ships; no form class ships yet. The Markdown library is to provide one. |
| Plain text | `text/plain` (`ClipboardForms.PlainTextId`) | the system's text format | text: the paragraphs' text joined with `\n`, in NFC | built into the field | 3rd | always | Shipped. |
| TsString XML | `SIL.TextEditing.TsStringXml` (`TsStringClipboard.XmlFormatName`) | `SIL.TextEditing.TsStringXml` | text: the XML of one `ITsString` | `TsStringClipboardForm` in `SIL.TextEditing.LCModel` | not in it; the host adds the id | no | Shipped. |
| Runs dump | `SIL.TextEditing.Runs.v1` | `SIL.TextEditing.Runs.v1` (`Interchange.ClipboardFormat`) | text: `Interchange` XML | `InterchangeClipboardForm` in `SIL.TextEditing` | not in it | yes | Shipped, for tests and debugging only. |

### FieldWorks TsString

FieldWorks puts a .NET-serialised `TsStringWrapper` on the clipboard under `TsString`. That class lives in FieldWorks' own SimpleRootSite
assembly. This library neither references nor copies it, and nothing here writes under `TsString`. A FieldWorks host registers its own
form with the id and format name `TsString`:

- its `Write` builds the wrapper from `TsStringClipboard.FragmentToXml`;
- its `Read` unwraps it and calls `TsStringClipboard.FromXmlToFragment`;
- its host clipboard backend stores and reads the object, since Avalonia's clipboard cannot.

A TsString copy of several paragraphs joins them with `"; "` and pastes as one paragraph, as FieldWorks does. With TsString first in
the paste order, a copy of several paragraphs from one field to another loses its paragraph breaks unless the FieldWorks Markdown form is
tried first.

### FieldWorks Markdown

The payload is a whole FieldWorks Markdown text **with its front matter**, so unmarked text never takes its writing system from the
target project's defaults. Read it with `MarkdownConverter.Parse` and write it with `MarkdownConverter.ToMarkdown`. FieldWorks Markdown
is a back end first (agents, files, drafting); on the clipboard it is an extra form, not the default.

### Plain text

Every copy puts the plain text on the clipboard: the paragraphs' text joined with `\n`, in NFC (ADR 0018). The field stores NFD; text
meant for other programs is NFC. The rich forms keep the stored form.

### TsString XML

`TsStringClipboardForm` carries the XML of one `ITsString`, written by liblcm's own `TsStringSerializer`. It is **not** FieldWorks' wire
format, so it uses its own id and format name, `SIL.TextEditing.TsStringXml`, and can sit beside a host's `TsString` form.

- Copy joins the paragraphs with `WriteParagraphSeparator` (default `"; "`, as FieldWorks does).
- Paste gives one paragraph, as FieldWorks does, unless `PasteParagraphSeparator` is set; then it splits there.
- A writing system the host's handles do not know keeps its id during the read, so the paste rule below can map it.
- A property liblcm cannot load back is left out and reported to `OnDropped` (see [Findings](findings.md#lcmodel-adapter)).
- The id is not in the default paste order. A host that wants it adds it.

### Runs dump (tests and debugging)

`Interchange.ToXml` writes every run, writing system and property, with nothing lost. ADR 0029 retired it from the user's clipboard;
`InterchangeClipboardForm` exists so that tests and the playground can carry formatting without liblcm or the Markdown library. Its
shape, from `src/SIL.TextEditing/Interchange.cs`:

| Element | Attributes |
|---|---|
| `Runs` | the root |
| `Para` | `style`, `rtl` (`1` or `0`), `defws`, then one attribute per property: `i_<number>="<variation>:<value>"` for an integer property, `s_<number>="<text>"` for a string property |
| `Run` | `ws`, `objdata` when the run has object data, and the property attributes; the text is the content |

A backslash, a control character, a lone surrogate and U+FFFC are escaped as `\\` and `\uXXXX` in text and string values, so any XML
reader gets the exact UTF-16 back. `Interchange.Json` is a JSON dump of a document for exact comparison in tests; it is not a clipboard
form.

## Copy

- `RichTextField.CopyAsync` asks every registered form for its payload and puts them all on the clipboard with the plain text.
- A form that returns null, returns an empty string or throws is left out. The others still go.
- The backend reports the format names it could not store. The field raises `ClipboardFormsNotStored` with them.

## Paste

`RichTextField.PasteAsync` tries the ids of `ClipboardForms.PasteOrder` in turn and uses the first form that the clipboard holds and that
reads.

- The default order is FieldWorks' own: `TsString`, `FieldWorks Markdown`, `text/plain`.
- The host may replace or edit the list. An id with no registered form is skipped. A form whose id is not in the list is never read.
- A payload a form cannot read (null, or an exception) is skipped, so paste falls through to the next form.
- The editor normalises the pasted text to the form the host stores (`Document.Normalization`; NFD for FieldWorks).
- A block the field does not allow is flattened, and the field raises `BlocksFlattened` (ADR 0033).

## The writing-system rule

Text in a writing system the target project lacks takes the writing system of the selection it replaces, as FieldWorks does
(`ClipboardWritingSystems.MapMissing`).

- The field asks `RichTextField.KnowsWritingSystem`. By default that is its `WritingSystems` registry, and an empty registry accepts every
  writing system.
- After a paste that mapped something, the field raises `WritingSystemsMapped` with the missing ids and the target, so the host can show a
  notice.
- A run with no writing-system id takes the target's too, but is not listed as missing.
- The rule runs for every rich form. Plain text has no writing system; it is pasted as typed text is.

## Copy as

`RichTextField.CopyAsAsync(formId)` puts one form on the clipboard as text: the plain text (`text/plain`), or a form whose
`OffersCopyAs` is true, such as FieldWorks Markdown. Only a form whose payload is a string can be copied this way. `CopyAsAsync` returns
false for any other, and for a form with nothing to say. `ClipboardForms.CopyAsForms` lists the forms a menu offers.

## Backends

The field reads and writes the clipboard only through `IClipboardBackend` (`RichTextField.ClipboardBackend`):

| Member | Does |
|---|---|
| `SetAsync(plainText, forms)` | Replaces the clipboard with the plain text and the named payloads. Returns the names it could not store. |
| `GetAsync(formatName, kind)` | The payload under a name, read as the form's `ClipboardPayloadKind`, or null. Must not throw on data it does not understand. |
| `GetTextAsync()` | The plain text, or null. |

- Payloads pass through untouched. A form says what it holds (`PayloadKind`): `Text` (a string), `Bytes` (a `byte[]`) or `Object` (a
  host object). The backend reads a format back as that kind and never guesses text from bytes.
- The default backend is Avalonia's clipboard of the field's window (`AvaloniaClipboardBackend`). It stores strings and `byte[]`s. It
  cannot store a host object and reports it as not stored.
- FieldWorks' WinForms host plugs in its own backend, so it can keep the clipboard itself and store the `TsStringWrapper` object.

## Planned: Word HTML and RTF

> **Planned. Nothing in this section is built.** It records owner decisions and research proposals so that integrators know what is coming.

**Decided by the owner** (2026-11-05, `decisions-2026-11-05-records.md`):

| Decision | |
|---|---|
| R-3 | Word-compatible copy and paste, "as much as that will work". This reverses ADR 0029's "No HTML or RTF" (amended by ADR 0031). TsString stays first on paste between FieldWorks fields. |
| Q65 | Copy writes HTML and RTF. Paste reads HTML only. Reading RTF is decided after testing with Word. |
| Q66 | Paste from Word keeps meaning (emphasis, links, styles, language, writing system) and drops fonts, sizes and colours. A host setting allows full formatting. |
| Q67 | Writing systems travel as character styles named `<Style>[lang=<ws>]`, as FieldWorks' Word export does. A host switch turns this off. |
| Q68 | Host callbacks for footnotes and pictures are defined in v1's API and built right after it. |
| Q69 | Paste order: TsString, FieldWorks Markdown, HTML, RTF, plain text. |
| Q70 | No "Copy as HTML source". |
| Q71 | HTML is read with AngleSharp (package approved for `SIL.TextEditing.WordInterchange`), subject to the AOT check. |
| Q72 | One new project, `SIL.TextEditing.WordInterchange`, holds the importer and the exporter. |

**Proposed in research report 20** (`20-word-clipboard.md`), not decided:

- Two forms in the new project. `HtmlClipboardForm`: id `HTML`, format name `HTML Format` (CF_HTML). `RtfClipboardForm`: id `RTF`, format
  name `Rich Text Format`. Both carry `Bytes`, so neither can be "Copy as". `IClipboardForm` and `IClipboardBackend` need no change.
- Host contracts for writing systems, style names and object text, and paste options (mode, size limit).
- A conformance fixture set for HTML and RTF, like the Markdown fixtures, with captured Word samples.
- The report recommended its own HTML reader and no package. The owner's Q71 chose AngleSharp instead.

## Planned: tables on the clipboard

> **Planned.** See [Tables](tables.md) for the storage and editing design.

- Q74: on the Markdown side a table stays a standard pipe table.
- Q78: tab-separated text pasted at a paragraph boundary becomes a table, and "Paste as table" is offered too.
- Research report 19 proposes table copy and paste in every form: TsString, Markdown, HTML, RTF and tab-separated text.
- Today a paste into a field that does not allow tables already flattens a table to one plain paragraph per row, cells joined by
  ` | ` (ADR 0033).
