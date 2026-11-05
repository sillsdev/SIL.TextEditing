# FieldWorks Markdown conformance fixtures

These JSON files define FieldWorks Markdown together with the spec (Motif's `gdr-markdown-design`, ADR 0028 and ADR 0029 here). The parser does not
define the format: a reader or writer in any language conforms when it passes every case. `SIL.TextEditing.Markdown` runs them in
`ConformanceTests` (net48 and net10.0), and `PandocAgreementTests` reads the same cases with Pandoc.

## Files

- `NN-name.json`: an array of cases. They are loaded in file-name order. Case names are unique within a file.
- `projects.json`: named projects (`"standard"`) that cases share through `"project": "standard"`. It is an object, not an array of cases.

## A case

```json
{
  "name": "vernacular span in an analysis paragraph",
  "markdown": "The prefix [mang-]{.v}.\n",
  "paragraphs": [
    { "style": "Normal", "props": {},
      "runs": [
        { "text": "The prefix ", "ws": "en", "props": {} },
        { "text": "mang-", "ws": "ceb" },
        { "text": ".", "ws": "en" }
      ] }
  ],
  "findings": [],
  "canonical": null
}
```

| Field | Meaning |
|---|---|
| `name` | What the case shows, as a sentence. A case named `ok: <finding id> ...` is a negative case for that id: the construct is fine and the id must not appear. |
| `markdown` | The input text. Absent for a **write case** (the model alone). |
| `frontMatter` | A `markdown` (or, for a write case, a `canonical`) that starts with `---` is a whole file, as spec 6.3 shows it: the runner adds nothing and the writer writes its front matter. Otherwise the runner puts the standard front matter (`fwmd: 1`, analysis `en`, vernacular `ceb`) before `markdown` and `canonical`, and compares the body only; finding lines are counted from the body. `"explicit"`: `markdown` and `canonical` are whole files and the writer writes its front matter. |
| `mode` | `"inline"` for the grammar of a multi-string field (spec 5.1): one paragraph of inline text, no front matter, and block syntax is `fwmd.field.block-syntax`. |
| `paragraphs` | The expected paragraphs of a read case, or the model of a write case. |
| `findings` | The expected findings, in source order. Absent means none. |
| `canonical` | Absent: not checked. `null`: `markdown` is already canonical, so writing what was read gives it back. A string: the canonical text. A write case needs a string. |
| `project` | A project the host knows: a name from `projects.json`, or the object itself. |
| `options` | `linkForm` (`"A"` to `"E"`), `linkBase`, `projectName`, `normalize` (false: keep the text as written), `unknownGuids` (`"rebind"`). |
| `pandoc` | Absent or `true`: Pandoc must read the case (the Markdown, and the canonical text) as the paragraphs say. A string: it need not, and the string says why (a test fails for a case without an error that switches Pandoc off with no reason). A case with an error finding is never read by Pandoc. |

For every read case the runner also checks that writing the paragraphs gives `canonical` (when given), that the result reads back to the same paragraphs, and that
writing that result again changes nothing.

### Paragraphs and runs

- A paragraph: `style` (default `Normal`), `props` (paragraph properties other than the style), `runs`, and for an empty paragraph `typingWs`, the writing system
  it has no run to carry.
- A run: `text` (exact UTF-16; JSON carries NUL and lone surrogates as `\u0000` and `\uD800`), `ws` (the resolved writing-system id, never "default"), `props`, and
  object data.
- `props` spell a property by its attribute key: `bold`, `fontsize`, `fontsizeUnit`, `p50`, `s6x`. A character style is `"s133"`, which holds the style name.
- Object data is `objData` (the string, as FieldWorks stores it), or the shorthand `link` (`{"guid", "tool", "tag"?, "database"?}` or `{"url"}`: the `silfw://` URL
  FieldWorks stores for a link, with U+0004 in front) or `picture` (a GUID).
- Encodings of that data: the `silfw://` query is percent-encoded with lowercase hex (`%2f`, never `%2F`), and only the unreserved characters of
  FieldWorks (letters, digits and `- _ . ! * ( )`) go unencoded. A picture's object data is U+0008 followed by the GUID's 16 bytes as 8 UTF-16 characters
  (`Guid.ToByteArray()` read as little-endian code units); `"picture": "<guid>"` builds that.

### Strict JSON

The files are strict JSON (RFC 8259): no comments, no trailing commas, and no raw control character inside a string (a newline is `\n`, a tab `\t`). A
test parses every file with a strict parser, so a file that only the lenient parser of the runner would take fails.

### Findings

`id`, `severity` (`error` by default, or `warning`, `suggestion`), the range as `line`, `column`, `endLine`, `endColumn` (1-based, UTF-16 code units, the end
exclusive), and optionally `fix` (the one exact replacement of the observed text; `null` when there must be none), `choices` and `nearest`.

### The project

```json
{ "analysis": "en", "vernacular": "ceb",
  "writingSystems": { "ceb-x-old": { "lang": "ceb", "dir": "ltr", "font": "Doulos SIL" } },
  "tableStyles": { "header": "GDR Table Header", "row": "GDR Table Row" },
  "styles": { "paragraph": ["Example"], "character": ["Hyperlink"] },
  "objects": [
    { "guid": "...", "path": "/lexicon/palit(2)", "tool": "lexiconEdit", "name": "palit", "example": false, "deleted": false },
    { "guid": "...", "kind": "picture", "file": "/files/pictures/hut.jpg", "caption": "A hut" } ] }
```

The host a case runs against knows exactly this: which writing systems and styles exist, each object's GUID, path, FLEx tool and current name, and which pictures
there are. A path resolves to its object, and a path with no homograph number matches every homograph of it (an ambiguity with a choice for each). A deleted
object has no path. The object data of a link is the `silfw://` URL as FieldWorks writes it (whole query URL-encoded, `database=this$` first, `tag=` always).

The project names the two table styles (`tableStyles`); the format only says that a pipe table's header row and body rows take the host's table styles, and Motif's are the GDR template's. A project that gives none has no table styles: the library has no default, so a pipe table is an `fwmd.syntax.unsupported-markup` finding and the writer puts table-style paragraphs in divs.

## What the cases cover

Every rule of the format has a case: the attribute grammar and the quoting rules, classes and their order, `sNx` hex, NUL and surrogates, the escape table of
spec 3.4, the star rule of spec 3.2, divs, empty paragraphs, tables, the front matter and its version, normalisation to NFD, the five link forms and the path rules
of spec 4.3, homographs and GUID suffixes, `url=` and its rebuilding, pictures and glossed examples, the reserved field fragment, and a positive and a negative case
for every finding id. The Pandoc check reads them all.

## Adding a case

Write the case from the spec, run the tests, and read the failure: the runner prints the findings and paragraphs it got. Put the case in the file for its topic.
Check a new canonical spelling with Pandoc (`pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t native`); the Pandoc test does
the same for every case.
