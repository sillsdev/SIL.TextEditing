# Record files: a YAML frame in a strict profile, typed by a schema that is data

A FieldWorks record (an entry, a notebook record, a list item) can be one file: YAML front matter for the tree of objects and fields, FieldWorks
Markdown for every rich value, and for some record kinds a Markdown body. `SIL.TextEditing.Markdown` reads and writes the frame (ADR 0031). This is the
first slice (R1a): the frame, its schema, its findings and its fixtures. Export from liblcm (R1b), diff and shape checks (R2) and Motif come later.

**Owner decisions** (2026-11-05, `decisions-2026-11-05-records.md`; they override research report 17 where they differ):
- **R-2:** YamlDotNet parses the frame, in `SIL.TextEditing.Markdown` only: its parser layer, never the reflection serializer. Our own writer quotes every
  string, writes rich values as `|-` blocks and fixes key order by the schema, `guid` first. The reader is lenient (decision 9) and types values by the
  schema's field path, not by YAML (`en: no` in a text field is "no").
- **R-5:** the front-matter keys are `fwmd`, `analysis`, `vernacular`, `writingSystems` (decision 32) and `record`, `schema`, `root`, `base`,
  `record-data`. An unknown key is an error in a record file only.
- **R-6:** every rich value is a `|-` block in place. A record kind whose schema names a body field writes that field as the Markdown body.
- **R-7:** the schema is data and types each value by path; no per-value tags. It publishes a JSON Schema with `contentMediaType` per text value.

The rest is **defaults**: how this version does it where the owner has not decided. They can change without superseding anything (as in ADR 0026).

**API** (`SIL.TextEditing.Markdown.Records`)
- `RecordSchema`, built by `RecordSchemaBuilder`: object types (fields in canonical order, `HasGuid`), record kinds (root type, `PathSegments`,
  `BodyField`), field kinds `Text`, `Rich` (one paragraph, or `Paragraphs`), `Notation(name)`, `Integer`, `Boolean`, `Guid`, `Reference`, `Sequence`,
  `Object`, any text kind optionally per writing system, and a default writing system for a field in one. `Id` is what `schema:` says and the JSON Schema's
  `$id`. `ToJsonSchema()` writes draft-07.
- `RecordDocument`: the front-matter keys and `Data`, a tree of `RecordObject` (with `Guid`), `RecordAlternatives`, `RecordList` and `RecordScalar` (the
  value as text; a rich value read from a file also has its `Paragraphs`, a reference its `Target`). Values carry their field path
  (`senses/1/definition.en`) and range. `Find(path)`, `Objects()`, `AreEquivalent`.
- `RecordFileReader.Read(text, schema, RecordReadOptions)` gives a `RecordReadResult` (document, `MarkdownDiagnostic` findings in source order).
  `RecordFileWriter.Write(document, schema)` gives canonical text or throws `RecordWriteException`. `INotationChecker` is the notation hook; it returns
  the language service's `LanguageDiagnostic`s (ADR 0030), placed in the file by the reader.

**The profile, written**
- Block style, two-space indentation, `\n`. Front-matter keys in the order `fwmd`, `record`, `schema`, `root`, `base`, `analysis`, `vernacular`,
  `writingSystems`, `record-data`. In each object `guid` first, then the schema's order; alternatives in the document's order. Empty values are left out;
  an empty object in a list cannot be written and is refused. The body follows one blank line and ends with a line end.
- Strings are double-quoted with `\"` and `\\` the only escapes. A canonical file holds no raw control character (tab included), DEL, C1 control,
  U+2028, U+2029, U+FEFF, U+FFFE or U+FFFF: one YAML reader breaks lines at U+2028 and another does not (both YamlDotNet and Pandoc 3.9 fail on it).
  Plain text that holds one is a `|-` block of one paragraph of inline FieldWorks Markdown, which spells it as a character reference (report 17, 3.3
  rule 2). A rich value with one of them inside a quoted attribute value has it as a character reference too, which reads back the same.
- Integers and Booleans plain; GUIDs lowercase with hyphens. A key is bare when it is a letter then letters, digits, `-`, `_` and `/`, and not a word
  YAML 1.1 reads as a Boolean or null; so `no` (Norwegian) is written `"no"`.
- `schema:` is a string, the schema's id (R-7 points it at the JSON Schema; report 17's `schema: 1` is superseded). Another id is an error.
- `root:` names what the file holds. Its first `PathSegments` segments name the record; each further segment is an owned-object field, followed by a
  1-based index for a sequence; the fragment names a field and after a dot one writing system. For the record itself the kind's body field is the body. A
  fragment that names one rich value (a field in one writing system, or one alternative) makes that value the body; any other fragment makes
  `record-data` hold that field only.

**The profile, read**
- Values are typed by the schema. Plain text where text is expected is read as it is spelled, with information (`fwmd.yaml.unquoted`); `null` in a text
  field is "null". `~`, an empty value, `""`, `[]` and `{}` mean empty, with information and a fix that removes the line. An integer may be quoted or have
  leading zeros (`012` is 12), with information; a Boolean is only `true` or `false`, and the YAML 1.1 words (`yes`, `Off`, ...) are errors whose fix is
  the Boolean they meant. Single quotes, other escapes, other block headers (`|`, `|+`, `|2-`), uppercase GUIDs and needlessly quoted keys are information
  with the canonical spelling as fix. Flow collections and JSON are read, with information. Comments are dropped, with information and a fix that removes them.
- Errors: tabs in indentation (fix: two spaces per tab; the rest is read with each tab as a space), raw U+2028, U+2029 or U+0085 (fix in a rich value:
  the character reference), duplicate keys (the first is read), anchors (fix: remove), aliases (fix: the quoted text, `"*palit"`), tags (fix: remove),
  merge keys, folded blocks (fix: the value they read as, canonically), a second document (a `---` line with text after it, or a YAML block in the body,
  which Pandoc would merge into the metadata), nesting deeper than 64, and YAML that does not parse (nothing more is read). Duplicate keys, merge keys and
  second documents have no fix: which value was meant is not clear, as `MarkdownDiagnostic` says for an ambiguous intent.
- Information is `MarkdownSeverity.Suggestion`. Finding ids are `fwmd.yaml.*` and `fwmd.record.*` (`RecordDiagnosticIds`); the reader also reports
  `fwmd.version.unsupported`, `fwmd.ws.unknown` and the FieldWorks Markdown reader's own ids inside rich values, placed in the file.
- A rich value is read with `ParseInline` (one paragraph) or `Parse` (paragraphs). Unmarked text means the alternative's writing system, or the field's
  default (the file's `analysis` or `vernacular`); `.v` is the file's vernacular.
- Not read here: whether a reference resolves, its target's class, plain-text markup in a quoted value, collection order, deletes and moves. They are R2.

**Considered**
- Our own strict YAML reader (report 17's recommendation). The owner chose YamlDotNet (R-2).
- YamlDotNet's representation model in the library: it throws on a duplicate key and keeps neither comments nor the places of anchors and tags, which
  the findings need. The tests use it as an oracle. The reflection serializer is excluded by R-2 and by AOT.

**Consequences**
- YamlDotNet 18.1.0 (MIT; netstandard2.0, net47, net8.0, net10.0) is a package of `SIL.TextEditing.Markdown` only. Its parser and scanner publish clean
  under NativeAOT; `tests/AotCheck` runs a record read and write.
- The fixtures in `tests/conformance/records` (README "Record files") define the profile with the schema as data, so a second implementation can run them.
- Pandoc reads every canonical record file, but it reads a `|-` value (a string with no final line end) as inline Markdown: a frame value with several
  paragraphs, a heading or a list loses its blocks in Pandoc's metadata view. The body and one-paragraph values agree. Owner question below.
- VS Code's YAML extension does not validate Markdown front matter by itself, and a canonical file has no room for a `# yaml-language-server` comment.

**Open questions for the owner**
1. Rich values of paragraphs in the frame (a notebook record's other StText fields): keep `|-` and accept Pandoc's inline view, or write them as `|`
   (clip), which Pandoc reads as blocks, and read the final line end as no content?
2. How an editor finds the JSON Schema of a record file: a workspace `yaml.schemas` mapping by file name, an extension, or a comment the profile allows.

Owner decisions R-2, R-5, R-6 and R-7, 2026-11-05; the rest defaults, 2026-11-05.
