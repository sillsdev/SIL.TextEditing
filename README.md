# SIL.TextEditing

A rich text editor for [Avalonia](https://avaloniaui.net/) in which **every run carries its FieldWorks writing
system**. The model is shaped like FieldWorks' `ITsString`: runs with a writing-system id, an opaque property bag
keyed by FieldWorks property numbers, and object data for inline objects. Ws and ObjData are fields of `Run`, not bag
entries (ITsString holds them as properties 1 and 6), and the Ws is a language-tag string, not an integer handle; the
LCModel adapter maps them. The editor keeps no undo stack; the host owns
it, as FieldWorks' LCModel does.

**Status: bootstrap.** This is the native-editor prototype made into a repository. It is not released, the API will
change, and the list under "Known gaps" is the work still to do.

## Projects

| Project | Targets | What it is |
|---|---|---|
| `src/SIL.TextEditing` | netstandard2.0, net10.0 | UI-free run model (`Run`, `Paragraph`, `Document`, `PropertyBag`), editing (`DocumentEditor`), paragraph-key rules (`Paragraph.Key`, opaque, null until the host assigns one), clipboard/debug interchange, grapheme helpers. No Avalonia, no liblcm. |
| `src/SIL.TextEditing.Avalonia` | netstandard2.0, net10.0 | `RichTextField`: layout with Avalonia's `TextFormatter`, caret, selection, hit testing, bidi caret stops, IME client, clipboard. |
| `src/SIL.TextEditing.LCModel` | netstandard2.0, net10.0 | The LCModel adapter (ADR 0016). `TsStringConversion` maps `ITsString`, and an IStText-shaped list of paragraphs, to runs and a `Document` and back, carrying every int (with its variation) and string text property by number, ObjData and NamedStyle included. `TsStringClipboard` reads and writes FieldWorks' "TsString" clipboard XML through LCModel's own `TsStringSerializer` and builds the NFC plain text that goes beside it. A multi-paragraph copy joins paragraphs with "; " (a parameter), as FieldWorks' `EditingHelper.CopySelection` does. The host puts the XML on the clipboard in the "TsString" format wrapped as FieldWorks does, with `TsStringWrapper.FromXml` (SimpleRootSite), next to the NFC plain text. The serializer needs no ICU setup (Hangul and composition-exclusion strings tested, net48 and net10.0). The host supplies the writing-system handle mapping (`IWritingSystemHandles`) and the paragraph keys. References `SIL.LCModel.Core`. **Not trimmable or AOT-compatible, and deliberately not in `tests/AotCheck`: it is for FieldWorks' and Motif's LCModel-side host code, not Motif's GUI.** |
| `tests/SIL.TextEditing.Tests` | net48, net10.0 | Unit tests for the model. |
| `tests/SIL.TextEditing.Avalonia.Tests` | net48, net10.0 | Avalonia.Headless tests for the surface (Skia, real shaping). |
| `tests/SIL.TextEditing.LCModel.Tests` | net48, net10.0 | Round trips of the adapter against real `TsStrFactory` strings, edits that leave undrawn properties alone, and the clipboard XML. |
| `src/SIL.TextEditing.Markdown` | netstandard2.0, net10.0 | The Markdown core (ADR 0011): `MarkdownConverter` writes the run model as GDR Markdown and reads it back, with the notation of Motif's `2026-11-03-gdr-markdown-design` spec (blocks, `::: {...}` fences, pipe tables, `[text]{...}` attribute blocks, links, pictures, escaping). The parser is `Markdig.Signed` 0.37.0 plus a small inline extension; the writer is our own and reads back every paragraph it writes, so writing then reading gives the same paragraphs and reading then writing is stable. **Unicode:** `Parse` and `ParseInline` return text as NFD, the form the editor stores (ADR 0018, issue 75), each run's text normalised on its own (attribute values such as `s6`, `url` and `cs` are opaque and never touched; `MarkdownOptions.NormalizeText = false` turns it off; a mark at the edge of a span stays in its run); finding and object offsets, lines and columns still refer to `Source`, the text as written, not to the normalised runs. `ToMarkdown` writes the text as stored and does not normalise, and `AreEquivalent` treats two texts as equal when they are equal as NFD. Everything project-aware reaches it through `IWritingSystemMapper`, `MarkdownStyleNames`/`IStyleCatalog` and `IObjectMapper`, with optional `IObjectResolver` (a link problem with its id, severity, choices and nearest names, such as `gdr.link.ambiguous` or a `gdr.link.broken` warning), `IKnownWritingSystems` and `IKnownStyles` (the host's lists, for the choices and nearest of `gdr.ws.unknown` and `gdr.style.unknown`), `IWritingSystemChecker` and `IStyleChecker` (the host's own problem for a tag or a style, a warning too for one it accepts); the parser reports the spec's syntax findings (`gdr.syntax.*`, `gdr.table.*`, `gdr.heading.*`) with line and column, lists the objects it built with their ranges (`MarkdownParseResult.Objects`) and makes located findings for the host (`CreateDiagnostic`), and the checker's project rules stay in Motif. `Parse` returns findings for any text: input past Markdig's depth limit (a 5,000-row table, 10,000 emphases in one paragraph) is a `gdr.syntax.too-deep` finding and its lines are kept as plain text; the writer reads back everything it writes, so a table that large is written as fences, which read back. **Table limit:** Markdig reads a pipe table only while rows x (columns + 1) stays under about 10,200 (it fails at 5,119 rows of one column, 3,412 of two, 2,047 of four, 1,136 of eight); a bigger table is one `gdr.syntax.too-deep` finding on `Parse` and fences on `ToMarkdown`, never an exception. A bracket a cell leaves open can pair with one in another row, so when rows disturb one another the writer escapes every optional character in the table, and only a table that still does not read back is left to the fences; a one-column table may have empty rows; a delimiter row with another cell count than the header is `gdr.table.ragged`. No libpalaso, liblcm or Avalonia reference. Trimmable and AOT-compatible on net10.0. `tests/AotCheck` (`dotnet publish tests/AotCheck -r win-x64`, warnings are errors) roots this library and Markdig whole, so the analyser sees all their code, then runs `MarkdownCheck.cs` with the public API only: every block (headings 1 to 3, paragraph, bulleted and numbered list, block quote, pipe table, fence with properties, `{empty}`, `{empty v}`) and inline form (bold, italic, both, spans with `ws`, `v`, `cs` and properties, external link, internal links, picture, generic `s6` object data, hard break, entity, escapes), findings of each family, the abstract (`ParseInline`, `ToInlineMarkdown`), a text past the depth limit, renamed style names, and two hosts, one implementing every optional interface and one only the required ones. AotCheck runs with invariant globalization, where `string.Normalize` does not decompose, so the NFD import is run there but its result is not asserted; a host that needs NFD must not turn invariant globalization on. **net48 binding redirects:** Markdig.Signed 0.37.0 brings System.Memory 4.5.5 (assembly version 4.0.1.2), System.Buffers (4.0.3.0) and System.Runtime.CompilerServices.Unsafe (4.0.4.1), so a net48 host needs a binding redirect to those versions for each (the app.config of `tests/SIL.TextEditing.Markdown.Tests` is the model: `System.Memory` 0.0.0.0-4.0.5.0 to 4.0.1.2); FieldWorks has them already. |
| `tests/SIL.TextEditing.Markdown.Tests` | net48, net10.0 | Table-driven tests of every construct, the attribute grammar and every property key, plus 600 seeded random models and 600 random texts per run (`TEXTEDITING_MARKDOWN_SEEDS` raises the count) that check the round trip and the stability of the canonical form. |
| `samples/SIL.TextEditing.Playground` | netstandard2.0, net10.0 | The shared playground UI: a `RichTextField`, scenario picker, writing-system bar, format and undo buttons, live model inspector and event log. Both hosts and the integration tests use it. |
| `samples/Playground.Desktop` | net48, net10.0 | Plain Avalonia desktop app showing the playground: Avalonia 11.3.17 on net48, Avalonia 12 on net10.0 (Motif's shape). |
| `samples/Host48` | net48 | WinForms form hosting the playground through `WinFormsAvaloniaControlHost` between two WinForms TextBoxes, Avalonia 11.3.17: FieldWorks' shape. |
| `tests/SIL.TextEditing.Playground.Tests` | net48, net10.0 | Avalonia.Headless integration tests that drive the playground the way a person would. |

One source tree serves both Avalonia generations. The netstandard2.0 target compiles against **Avalonia 11.3.17**
(what FieldWorks pins; it runs on net48) and the net10.0 target against **Avalonia 12.1.2** (what Motif pins). The
versions are set per target framework in `Directory.Packages.props`. There is currently no `#if` in the source: the spike's one per-version spot (the clipboard API) is not needed,
because Avalonia 11.3.17 already has the `DataTransfer` API that 12 uses, and `FontFeature` parsing is written
without `FontFeatureCollection.Parse` (absent in 11.3) for both. Keep any future per-version difference small, behind
`#if NET10_0_OR_GREATER`, and list it here.

Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Win32.Interoperability are used by the samples only; the
libraries do not reference them. The playground tests load the Fluent theme so the sample's buttons and pickers have
templates.

## Build and test

Windows with the .NET 10 SDK and the .NET Framework 4.8 targeting pack (net48 builds and tests run only on Windows):

```
dotnet build SIL.TextEditing.sln
dotnet test  SIL.TextEditing.sln      # both target frameworks of both test projects
```

From WSL, run the same commands through `powershell.exe`. The samples are built by the solution. The headless tests use
the Windows fonts Segoe UI, Nirmala UI, Leelawadee UI and Constantia.

## Try it

```
run desktop10     # Avalonia desktop window on .NET 10, Avalonia 12
run desktop48     # the same window on .NET Framework 4.8, Avalonia 11.3.17
run winforms48    # WinForms form: TextBox, playground, TextBox (FieldWorks' shape)
```

`run.cmd` and `run.ps1` build the host and start it; add `--scenario "Inline picture (ORC)"` to open a scenario.
The playground loads ready-made documents (Latin and Arabic, Indic and Thai shaping, NFD and surrogates, inline
pictures, unknown properties, 200 paragraphs, many fields) and shows what the model holds in a live inspector. A
person tests with the checklist in [docs/manual-testing.md](docs/manual-testing.md).

## FieldWorks editing semantics (ADR 0018)

- **Arrow keys are logical by paragraph direction.** Left goes backwards in a left-to-right paragraph and forwards in a
  right-to-left one; Right is the reverse. Inside mixed-direction text the caret may therefore move the other way on
  screen. Shift+arrows extend the same way, Ctrl+arrows go by word in logical order, Up/Down and Home/End are unchanged.
  The caret is drawn at the visual place of its logical offset (trailing edge of the cluster passed after a forward step,
  leading edge of the next cluster after a backward step).
- **Backspace** removes one code point (a surrogate pair is one); an embedded object goes whole.
- **Delete** removes one base character and the combining marks (Mn, Mc, Me) after it.
- **Normalisation.** `Document.Normalization` (also `RichTextField.Normalization`) is a nullable `NormalizationForm`, null by
  default (text is inserted as it arrives). A host sets `NormalizationForm.FormD` for FieldWorks: typed text, IME commits
  and pasted text (plain or rich) are converted before insertion and the caret lands after the converted text. Plain
  text the field puts on the clipboard is always NFC; the internal rich format keeps the stored form.

## Known gaps

- **Accessibility**: no UIA `TextPattern`; the field is an Edit with a name and a value only. Inside a net48
  `WinFormsAvaloniaControlHost` Avalonia's UIA subtree was empty in the spike (unexplained).
- **Input**: real IME and Keyman interaction are untested (the headless tests drive the IME client directly). Missing:
  scrolling inside long fields, drag and drop, spelling squiggles, find highlights, touch. Selection by mouse (click
  with bidi and soft-wrap affinity, Shift+click, drag across paragraphs, double-click word, triple-click paragraph) and
  keyboard (Shift+arrows, Ctrl+arrows by word, Home/End by visual line, Ctrl+Home/End, Ctrl+A, Up/Down with the
  preferred x) is done. The word rule (`WordBreaks`) is deliberately simple: runs of letters, digits and marks, with
  an apostrophe between letters inside the word; no dictionary, so Thai and Chinese are not split, and it is not UAX #29
  word breaking. Left/Right and Ctrl+Left/Right are logical. Home/End in a right-to-left paragraph are the logical line start and end.
- **Avalonia faults, pinned by tests**: hit testing in a right-to-left paragraph is wrong on 11.3.17 (fixed in 12.1.2);
  an inline object in right-to-left text is mis-ordered on both. `RichTextField.ObjectAsGlyph` is the workaround for the
  second.
- **Paragraph and character properties**: drawn, with the host's `IStyleResolver` supplying named styles (ADR 0004): weight (any 100 to
  900, invert), italic, foreground and background colour, underline type and colour (dotted, dashed, single, double,
  strikethrough; the squiggle is not drawn), superscript and subscript, font size (absolute, relative, named), font
  family and per-run font features; for a paragraph, alignment, first-line and hanging indents, margins, space before
  and after, line height, paragraph colour, borders and padding, a line limit, bullets and numbers. Not drawn: the baseline
  offset (Avalonia cannot shift one run's baseline), justification (Avalonia's justifier is internal, so a justified
  paragraph is laid out against the leading edge) and the squiggle. Pagination properties and the rest are carried untouched.
  Lengths in points are drawn as device-independent pixels, as font sizes are.
  Text a line limit hides stays in the model but cannot be reached: the caret, Right, End, Up/Down and mouse stop at the end of
  the last line shown, as FieldWorks puts an insertion point in truncated text at the last visible position. Typing, Delete, Enter, paste
  and composition are refused at that point, and Backspace at the start of the next paragraph does not join it onto the hidden end.
  Select All and Copy still take the hidden text, because the selection model holds it. (FieldWorks also
  draws an ellipsis on the last line; this field does not yet.)
- **Graphemes**: `GraphemeClusters` is `StringInfo` plus Indic conjuncts (GB9c, six scripts; .NET 10 lacks it). The
  netstandard2.0 build also fuses Extend, SpacingMark, emoji ZWJ sequences and flags per UAX #29, with approximated
  property tables; caret stops in the field come from the shaper, not from this class.
- **Undo** is a host stand-in (`SimpleUndoStack`); no LCModel glue exists.

## Licence

MIT, copyright SIL Global. See `LICENSE`. FieldWorks code is LGPL and is not copied here; see `AGENTS.md`.
