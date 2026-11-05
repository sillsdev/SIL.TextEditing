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
| `tests/SIL.TextEditing.SecondDispatcher.Tests` | net10.0 | The tests that make a dispatcher on a second thread (Avalonia 12 allows it). `Dispatcher.UIThread` is process-wide, so they run in a test host of their own and cannot change it for the other tests (issue #72). |
| `tests/SIL.TextEditing.LCModel.Tests` | net48, net10.0 | Round trips of the adapter against real `TsStrFactory` strings, edits that leave undrawn properties alone, and the clipboard XML. |
| `src/SIL.TextEditing.Markdown` | netstandard2.0, net10.0 | The Markdown core (ADRs 0011 and 0028): `MarkdownConverter` writes the run model as FieldWorks Markdown and reads it back. FieldWorks Markdown is a strict profile of Pandoc Markdown: YAML front matter (`fwmd: 1`, the default analysis and vernacular writing systems, and each writing system's language tag, direction and font; a newer version is refused with `fwmd.version.unsupported`), flags as classes (`.v .name .example .broken .empty .nul .surrogate .nostyle`, in that order), `custom-style=` for styles, `[]{.empty}` for an empty paragraph (a styled one is a div around it), `[]{.nul}` and `[]{.surrogate x=D800}` for the two characters CommonMark cannot carry, Pandoc divs (`::: {custom-style=Example}`, paragraphs separated by blank lines), pipe tables (one paragraph per row, cells separated by a tab), `sNx` hex for a string property with a control character or a lone surrogate, minimal quoting (a value is bare when it holds only letters, digits and `- _ . : # /`, otherwise double-quoted with only `\"` and `\\`; `&amp;` only where an ampersand would read as a character reference; `\|` for every pipe that is not a cell separator), attributes in the canonical order, a relative font size as `fontsize=150 fontsizeUnit=rel`, and `^x^` and `~x~` read as superscript and subscript. **Links are project paths**: `[palit](/lexicon/palit(2)){.name guid=...}` in any of five forms (a root path, a path relative to the file, a `silfw://` or `motif://` back-link, `/objects/<guid>`; `MarkdownOptions.LinkForm` picks the one written, the reader takes all and converts without loss), with `palit(2)` homographs, eight-digit GUID suffixes for siblings that share a name, paths written in NFC and compared under canonical equivalence, a reserved `#field.ws` fragment, pictures as `![caption](/files/pictures/hut.jpg){guid=...}` and glossed examples as `[(1)](/texts/Kuwento/3.2){.example guid=...}`. `ProjectPath` is the path grammar; the host turns paths into GUIDs through `IProjectPathResolver` and object data through `IObjectMapper`; `DefaultObjectMapper` reads and writes `silfw://` links with no host. **Read lenient, write strict:** the reader takes any spelling Pandoc reads to the same structure (attributes in any order, single quotes, setext headings, `+` bullets, `<...>` around a destination); the writer writes only the canonical one, by the escape table of the spec and not by asking a parser, then reads every paragraph back and falls back to a safer spelling only if the canonical one does not read back. **Unicode:** `Parse` and `ParseInline` return run text as NFD, the form the editor stores (ADR 0018, issue 75), each run on its own (attribute values and destinations are never touched; `MarkdownOptions.NormalizeText = false` turns it off); finding and object offsets, lines and columns refer to `Source`, the text as written. `ToMarkdown` writes the text as stored, and `AreEquivalent` treats two texts as equal when they are equal as NFD. Everything project-aware reaches the core through `IWritingSystemMapper` (optionally `IKnownWritingSystems`, `IWritingSystemChecker`, `IWritingSystemDetails` for the front matter), `MarkdownStyleNames`/`IStyleCatalog` (optionally `IKnownStyles`, `IStyleChecker`), `IObjectMapper` (optionally `IObjectResolver`) and `IProjectPathResolver`; the parser reports the format's findings, all `fwmd.*` (syntax, table, heading, version, link, picture, style, writing system), with line and column, lists the objects it built with their ranges (`MarkdownParseResult.Objects`) and makes located findings for the host (`CreateDiagnostic`); the checks that need a project stay with the host. `Parse` returns findings for any text: input past Markdig's depth limit is a `fwmd.syntax.too-deep` finding and its lines are kept as plain text. **Table limit:** Markdig reads a pipe table only while rows x (columns + 1) stays under about 10,200; a bigger table is one `fwmd.syntax.too-deep` finding on `Parse` and divs on `ToMarkdown`, never an exception. A table whose header cells are all empty is written as divs, because Pandoc reads such a header as no header. **Markdig's own slow paths:** a few inputs are slow in Markdig itself and the library does not guard them: a run of `[a](` repeated tens of thousands of times is quadratic, reading a table of about 4,000 rows takes seconds, and a list nested a thousand levels deep is slow before it reaches the depth limit; a host that reads untrusted text should bound its size. The writer's own paths (many ampersands, many table rows) are linear. **Table styles:** `MarkdownStyleNames.TableHeader` and `TableRow` have no default; without them a pipe table is an `fwmd.syntax.unsupported-markup` finding and table-style paragraphs are written as divs. **Pandoc constructs:** hand-written math, citations, inline notes, strikeout, heading attributes and the block constructs Pandoc has and the notation lacks (fancy list markers such as `a.` and `(1)`, line blocks, grid tables, definitions after a term line, a caption next to a table) are `fwmd.syntax.unsupported-markup` findings with the escape as the fix, found by Pandoc's own rules and not by the writer's broader escape rule: grammar prose such as `M. Dixon`, `A. Consonants`, `Table: the forms` or `#palit#` is plain text in both and is never reported. A wrapped line that starts with `#`, `-`, `+`, `>` or `1.` stays in its paragraph, as in Pandoc, in the document and in a div. A Motif link read with no `ProjectName` keeps the project it names, and a raw tab in a table cell is still `fwmd.table.tab-in-cell`. No libpalaso, liblcm or Avalonia reference. Trimmable and AOT-compatible on net10.0. `tests/AotCheck` (`dotnet publish tests/AotCheck -r win-x64`, warnings are errors; locally add `-p:NuGetAudit=false` while issue 104 is open) roots this library and Markdig whole, then runs `MarkdownCheck.cs` with the public API only: every block and inline form, the front matter, the five link forms against a host that implements every interface, the findings of each family, the abstract (`ParseInline`, `ToInlineMarkdown`), and two hosts. AotCheck runs with invariant globalization, where `string.Normalize` does not decompose, so the NFD import runs there but its result is not asserted; a host that needs NFD must not turn invariant globalization on. **net48 binding redirects:** Markdig.Signed 0.37.0 brings System.Memory 4.5.5 (assembly version 4.0.1.2), System.Buffers (4.0.3.0) and System.Runtime.CompilerServices.Unsafe (4.0.4.1), so a net48 host needs a binding redirect to those versions for each (the app.config of `tests/SIL.TextEditing.Markdown.Tests` is the model); FieldWorks has them already. |
| `tests/SIL.TextEditing.Markdown.Tests` | net48, net10.0 | The conformance fixtures in `tests/conformance` (JSON, documented in its README: Markdown in, expected paragraphs and findings out, and model to canonical Markdown; they and the spec define the format), run by `ConformanceTests`; `PandocAgreementTests` reads every fixture, its canonical form and 150 generated documents (`TEXTEDITING_PANDOC_SEEDS`) with `pandoc -f markdown-smart-implicit_figures-implicit_header_references-task_lists -t json` and requires the same paragraphs. Pandoc is pinned in the CI workflow; where it is missing the tests skip, except in CI (`CI` or `GITHUB_ACTIONS` set), where they fail. Also: 600 seeded random models and 600 random texts per run (`TEXTEDITING_MARKDOWN_SEEDS` raises the count) that check the round trip and the stability of the canonical form, and tests of the findings' positions, normalisation, size limits and the host interfaces. |
| `samples/SIL.TextEditing.Playground` | netstandard2.0, net10.0 | The shared playground UI: a `RichTextField`, scenario picker, writing-system bar, format and undo buttons, live model inspector and event log. Both hosts and the integration tests use it. |
| `samples/Playground.Desktop` | net48, net10.0 | Plain Avalonia desktop app showing the playground: Avalonia 11.3.17 on net48, Avalonia 12 on net10.0 (Motif's shape). |
| `samples/Host48` | net48 | WinForms form hosting the playground through `WinFormsAvaloniaControlHost` between two WinForms TextBoxes, Avalonia 11.3.17: FieldWorks' shape. |
| `tests/SIL.TextEditing.Playground.Tests` | net48, net10.0 | Avalonia.Headless integration tests that drive the playground the way a person would. |

One source tree serves both Avalonia generations. The netstandard2.0 target compiles against **Avalonia 11.3.17**
(what FieldWorks pins; it runs on net48) and the net10.0 target against **Avalonia 12.1.2** (what Motif pins). The
versions are set per target framework in `Directory.Packages.props`. There is currently no `#if` in the source: the spike's one per-version spot (the clipboard API) is not needed,
because Avalonia 11.3.17 already has the `DataTransfer` API that 12 uses, and `FontFeature` parsing is written
without `FontFeatureCollection.Parse` (absent in 11.3) for both. Keep any future per-version difference small, behind
`#if NET10_0_OR_GREATER`, and list it here. The differences now: `SpellScanner` asks `CharUnicodeInfo` for the category of a code point directly on net10.0 and through a
one-character string on netstandard2.0 (no overload there), which only costs an allocation for a letter outside the basic plane; and `ScriptPlacement` reads a font's
OS/2 and hhea tables with `IGlyphTypeface.TryGetTable` on netstandard2.0 and, as Avalonia 12 gives no access to tables, from the font file's own table directory on net10.0 (once for each font; in a font collection from the member whose family name is the typeface's, and nothing is read whole).

Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Win32.Interoperability are used by the samples only; the
libraries do not reference them. The playground tests load the Fluent theme so the sample's buttons and pickers have
templates.

## The run model

`Paragraph.Runs` is a `RunList`, not a `List<Run>`: it tells its paragraph whenever a run is added, removed or replaced, and each
run tells it when its text changes, so `Paragraph.Length`, `RunBefore`, `RunAfter` and `Spans` are answered from run starts the
paragraph keeps current (a binary search, not a walk over the runs) however a host changes the runs. A host may edit `Run.Text` or
the list directly. `RunList` has `Add`, `Insert`, `RemoveAt`, `RemoveRange`, `AddRange`, `Clear`, an indexer and `IEnumerable<Run>`
(use LINQ for `ConvertAll`, `FindIndex` and the like); assign a `new RunList(runs)` to `Paragraph.Runs` to replace the runs. A
change to a run's `PropertyBag` in place (`SetInt`, `SetStr`, or a write to `IntProps` or `StrProps`) is seen too: the bag tells the paragraphs that
hold it, so `ChangeStamp` moves and a field shows the change (see the spelling section for what a host still calls).

`Paragraph.Runs = list` takes a `List<Run>` directly (the paragraph adopts a new `RunList` over the same runs, not the list itself: the `Run` objects are shared, but a later `list.Add` is not seen by the paragraph). `Run.Text = null` throws
`ArgumentNullException` (use an empty string); so do `Run.Props = null` and `Paragraph.Props = null`. Looping over `Paragraph.Runs` while the list is added to, removed from or has a run
replaced throws `InvalidOperationException`, as it does for a `List<Run>`; changing a run's text or properties meanwhile is fine.
`Paragraph.Text` is safe to read from any thread while nothing changes the paragraph (the model itself is edited on the UI thread only).
The work counters tests use (`TextReads`, `ElementReads`) exist in a build that defines `TEXTEDITING_COUNTERS` (every configuration but
Release) and cost nothing in a Release build.

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

## Keyboards (ADR 0008)

The field never switches keyboards; the host does. Subscribe to `RichTextField.ActiveWsChanged` (old and new writing system)
and switch the OS keyboard or Keyman to the new one. The field raises it only while it has focus, as FieldWorks' views set the
keyboard only with focus; a change made while it is unfocused is not reported, and the focus-gain event below carries the result.
It is raised once per change, never when the writing system stays the same, when the caret moves into text
of another writing system, when a selection's first writing system changes (a selection takes the writing system of the first
text it covers, as in FieldWorks), when an edit or undo leaves the caret in another one, and when the host or the writing-system
toolbar calls `Editor.SetActiveWs`. It is also raised, with old and new equal, when the field gains focus, so the host can
switch to the keyboard of the writing system at the caret even though nothing changed in this field.

**Focus in and out (what FieldWorks does, and what a host does to match).** On Windows, FieldWorks sets the keyboard on focus
gain only when focus comes from a window of the same process, so a keyboard the user picked in the tray or in another
application is kept when focus returns without a click (Alt+Tab, the tray). A click into a view makes a selection, and the
selection sets the keyboard for it, so a click does set the keyboard. (On Linux FieldWorks sets it on every focus gain.) When a
view loses focus to a control elsewhere in the same application that is not a view, it restores the default keyboard; leaving
the application is left to the operating system. The field cannot tell where focus came from (Avalonia's `GotFocusEventArgs`
gives the navigation method and key modifiers, not the source), so it raises the focus-gain event every time and the host
decides: switch only if the previously focused window was its own, otherwise leave the keyboard alone. The field's event
follows changes of writing system, so a click into text of the same writing system gives no event of its own. To restore the
default keyboard, subscribe to the field's `LostFocus` and switch when focus has moved to a control of your own application
that is not a field. The field raises nothing when it loses focus. Avalonia gives the field `LostFocus` when its window is deactivated and `GotFocus` when it is activated again, on both lines (checked in real windows with a second window of the same application; switching to another application was not driven), so a reactivation gives one focus-gain event; the event arguments cannot tell that from any other focus gain, so a host that must uses its window's `Activated`.

## Host hooks (ADR 0021, ADR 0025)

The field owns one text; the window around it belongs to the host. These are the hooks:

- **Find and replace.** The host's dialog builds a `FindOptions` (text, match case, match diacritics, whole word, regular
  expression, writing system, character style). `Editor.TryFind` searches from the selection (next or previous, optionally
  wrapping), `Editor.FindAll` lists the matches, `Editor.ReplaceMatch`, `ReplaceCurrent` and `ReplaceAll` replace as
  single undo steps. `RichTextField.Find` and `SelectMatch` also select the match, mark it as the current match
  (`CurrentMatch`) and ask the host to scroll; `SetFindHighlight` highlights every match while the dialog is open. Text
  is compared as NFD, so composed and decomposed forms find each other.
- **Context menu.** `RichTextField.ContextMenuRequested` fires for a right click, the Menu key and Shift+F10 with a
  `ContextInfo`: the word and its range, its writing system, the run and its properties, any link or embedded object, the
  selection and an (empty, for now) list of spelling suggestions. The host builds the menu. Use the event or Avalonia's
  `ContextMenu` on the field; with both, the event wins.
- **Paging and scrolling.** PageUp and PageDown (with Shift) move the caret by the field's visible height
  (`VisibleBounds`, or `PageHeight` when the host sets it); a drag past the edge extends the selection and asks for
  scrolling. The field never scrolls anything itself: it raises `ScrollRequested` (reason, the part to show, the distance)
  and a scroll viewer round the field answers when no handler marks it handled.

## Spell checking (ADR 0020, 0026)

The host gives the field its dictionaries: set `RichTextField.SpellChecking` to an `ISpellCheckHost` that returns an
`ISpellChecker` (`Check`, `Suggest`, `Add`; `ISpellIgnorer` adds `Ignore` when a dictionary has it) for each writing system, says which
characters form words and which join them, and raises `DictionariesChanged` when a dictionary changes. Derive from
`SpellCheckHostBase` to write only `GetChecker`: it has the default word rules and `RaiseDictionariesChanged()` (any thread). The same word rules (`IWordRules`) serve the double-click, Ctrl+arrows and whole-word find; `RichTextField.WordRules` gives them without spell checking. The
field checks the paragraphs in view when idle, a few words at a time, and draws red squiggles (misspelled, or a word with an object in it) and blue
ones (a word that mixes writing systems or has unchecked text inside). The rules are in `SpellScanner` (UI-free): words
break at characters that are not word-forming, and do-not-check text and text that may not be edited (unless forced) are
skipped. `SpellingSuggestionsAt`, `ApplySpellingSuggestion`, `AddToDictionary`, `IgnoreWord` and `RestartSpelling` are
what a host's context menu and dictionary changes call (`ApplySpellingSuggestion` also takes runs, for "insert missing spaces" and, with
`WithWholeWordIn`, for "make the whole word writing system X" in a word with several writing systems). The word of a context request and the squiggled word
are one definition (an underscore ends both, as in FieldWorks). A checker
that throws raises `SpellCheckerFailed` (written to `Trace` once per writing system when nothing handles it) and is left alone; it does not end
the application. A host that repairs the dictionary raises `DictionariesChanged` (or calls `RestartSpelling`), which tries that writing system again. The playground's "Spell checking" scenario shows it with a
short English and French word list.
A dictionary that is slow can be asked off the UI thread: its checker also implements `IBackgroundSpellChecker` (`Check` may then run on a worker; the field finds the words and applies the answers on its own thread, and drops an answer for a paragraph that has changed since).
`CheckSpellingStep` then returns false while a batch is out, and `IsSpellingComplete` says when everything in view is checked. A loop of
`while (!field.CheckSpellingStep()) { }` on the UI thread therefore never ends with such a checker (the answer is applied by the dispatcher the loop blocks):
let the dispatcher run and wait for `IsSpellingComplete`, or use a checker without the interface where a synchronous check is wanted.

What a host calls (the field restarts checking itself on an edit through the editor, `Apply`, `ExternalReplaceParagraph`, a change of
`IsReadOnly` on the field or its editor, a word added or ignored, and a `DictionariesChanged` notice): nothing, after it changes a paragraph
directly. A paragraph tells the field when its runs, a run's text, writing system, object data or property bag (replaced, or changed in place with
`SetInt`, `SetStr` or a write to `IntProps` or `StrProps`), or its style, direction, default writing system or property bag, change
(`Paragraph.ChangeStamp` moves): the field measures and draws again, lays out that paragraph again (and only that one, keyed by the stamp), numbers
the list again, drops the marks of that paragraph and checks it again when idle. A notice from another thread is marshalled to the field's own, but the model is not thread-safe (ADR 0024): the edit itself must still not overlap a layout, so a host edits on the UI thread.
`RichTextField.RefreshFromDocument()` is for what a paragraph cannot tell: the list of paragraphs (`Document.Paragraphs`) changed, a paragraph
replaced in it. It restarts checking too. When a style resolver's answers change (not the resolver itself, which the field takes again when it is set), `InvalidateAllLayouts()`, and
`RestartSpelling()` when what is checked changed. After repairing a failed dictionary, raise `DictionariesChanged`. Setting
`DocumentEditor.IsReadOnly` directly tells automation as `RichTextField.IsReadOnly` does.

## Colours and themes

Until the host sets `Foreground` (text, and a border with no colour of its own) and `Background` (the page), the field takes them from the
theme: the text colour the caret uses and the theme's window colour, and in a Windows high contrast theme the system's window text and window
colours, as FieldWorks does (the selection takes the system highlight, and selected text the system's highlight text colour, whatever colour its runs have). The text keeps one default brush that the field changes in place when
the theme or the system's colours change, so a theme switch redraws the text and does not lay out the paragraphs again.

A colour the host sets is kept as it is under every theme, high contrast included. So a host that sets one sets the other, or neither: a host
that sets only `Foreground = Black` on a dark theme gets black text on the theme's dark page. `ResetForeground()` and `ResetBackground()` go back
to following the theme. A run's own colours (text property 8 and 9) are the document's and do not change with the theme.

## Known gaps

- **Accessibility**: no UIA `TextPattern`; the field is an Edit with a name and a value only. Inside a net48
  `WinFormsAvaloniaControlHost` Avalonia's UIA subtree was empty in the spike (unexplained).
- **Input**: real IME and Keyman interaction are untested (the headless tests drive the IME client directly). Missing:
  scrolling inside long fields (the host scrolls; see Host hooks), drag and drop, touch. Selection by mouse (click
  with bidi and soft-wrap affinity, Shift+click, drag across paragraphs, double-click word, triple-click paragraph) and
  keyboard (Shift+arrows, Ctrl+arrows by word, Home/End by visual line, Ctrl+Home/End, Ctrl+A, Up/Down with the
  preferred x) is done. The word rule (`WordBreaks`) is deliberately simple: runs of letters, digits and marks, with
  an apostrophe between letters inside the word; no dictionary, so Thai and Chinese are not split, and it is not UAX #29
  word breaking. It is also the host's: one rule (`IWordRules`: which code points form words in a writing system, and which join the words on
  each side of them) serves the double-click, Ctrl+Left and Right, the whole-word find, the word of a context request and the spell checker
  (set `RichTextField.WordRules`, or the rule of `SpellChecking` is used), so a writing system that makes a hyphen a letter makes "foo-bar" one word
  everywhere. Left/Right and Ctrl+Left/Right are logical. Home/End in a right-to-left paragraph are the logical line start and end.
- **Avalonia faults, pinned by tests**: hit testing in a right-to-left paragraph is wrong on 11.3.17 (fixed in 12.1.2), and on
  both 11.3.17 and 12.1.2 the caret, hit testing and selection of a left-to-right run are wrong on a right-to-left paragraph's line
  of two or more runs, and likewise on a left-to-right paragraph's line of two or more runs with a right-to-left run in it (on 12 some of its ranges throw); an inline object in right-to-left text is mis-ordered on both. The layout works around the first two by
  reading such a line from its runs as they are drawn (`LineInfo.UsesRuns`: caret x, hit testing, visual stops and selection boxes
  from each run's glyphs). It is not behind `#if`, because 12 has the second fault too (ADR 0027, which supersedes the 11.3-only scope of ADR 0015).
  `RichTextField.ObjectAsGlyph` is the workaround for the third. On 12 only, the spaces a wrapped line ends with after a run of Arabic in a
  left-to-right paragraph come with glyph clusters numbered against the run's direction and answer distances and hits that do not add up
  (a click at the end of the line lands one space short); `RunSlot` places such a run by counting its spaces (`ClusterOrderTests` names the
  fault and fails when 12 stops doing it).
  On 11.3 only, the formatter throws "Cannot split: requested length N consumes entire run" for some sequences with a zero width joiner, such as one
  after a space before Arabic text in a left-to-right paragraph (`ZwjAfterSpaceTests` names it and fails when 11.3 stops doing it). The layout
  catches the refusal and lays the paragraph out again with the joiners swapped for word joiners, then the other format characters, then (for a space followed by a mark, the other way the formatter refuses) the space swapped for a dotted circle so the mark is still drawn, and as a last resort every format character and mark swapped for a word joiner (the model, the offsets and the grapheme boundaries are untouched).
  On 12 only, asking for the boxes of a range that covers no width (a right-to-left mark alone before the spaces a line ends with) throws "Covered length must
  be greater than zero"; the layout catches it, in the boxes of a range and in the spaces a justified line stretches, and answers that such a range has no box (`RlmWrapTests` names it and fails when 12 stops doing it).
- **Paragraph and character properties**: drawn, with the host's `IStyleResolver` supplying named styles (ADR 0004): weight (any 100 to
  900, invert), italic, foreground and background colour, underline type and colour (dotted, dashed, single, double,
  strikethrough, and the squiggle, which the layout draws itself as a zigzag below the baseline), superscript and subscript (sized, and raised or lowered, as the font's own OS/2 table says, with FieldWorks' two thirds where it gives nothing; the ratios are applied to the size of the text beside it, which is a default), font size (absolute, relative, named), font
  family and per-run font features; for a paragraph, alignment, first-line and hanging indents, margins, space before
  and after, line height, paragraph colour, borders and padding, a line limit, bullets and numbers. A run's baseline offset is drawn by drawing the run
  ourselves, shifted: the line's slot makes room for it and the caret, selection and squiggle follow it. A justified paragraph is stretched by
  the field itself (Avalonia's justifier is internal): each line but the last shares its slack equally among its interior
  spaces, so it reaches both edges of the text area; the last line stays at the leading edge, as in FieldWorks. The caret, selection,
  mouse and run backgrounds follow the stretched text, and each stretched line is drawn piece by piece, with its underlines and strikethrough drawn by the field as lines across the whole stretched run (so a dotted or dashed pattern is not restarted or scaled). Pagination properties and the rest are carried untouched.
  Lengths in points are drawn as device-independent pixels, as font sizes are.
  Text a line limit hides stays in the model but cannot be reached: the caret, Right, End, Up/Down and mouse stop at the end of
  the last line shown, as FieldWorks puts an insertion point in truncated text at the last visible position. Typing, Delete, Enter, paste
  and composition are refused at that point, and Backspace at the start of the next paragraph does not join it onto the hidden end.
  Select All and Copy still take the hidden text, because the selection model holds it. As in FieldWorks, the last
  visible line of a cut-off paragraph ends with "..." in the paragraph style's character format, on the trailing side of the text
  (left of it in a right-to-left paragraph), with room kept for it when the text area is wide enough for one beside text; the
  caret and hit testing stay on the text, and a hard line break that ends the paragraph is not cut-off text.
- **Spaces a wrapped line ends with**: they hang past the line's text, outside the alignment (a line is aligned by its text), and the
  caret across them moves one space width per space, as in FieldWorks, even past the text area. The control draws nothing outside itself, so
  a caret that would be there is held at the control's edge (the End caret at the edge, the carets of the spaces before it 2 px apart inside
  it). When the text itself reaches the edge (a justified line, a right-aligned one, a line that just fits) there is no room for those
  carets to show apart: they crowd on the text's last letters. A host that wants each caret seen gives the text area room on the side the
  spaces hang to, with the paragraph's trailing indent (property 20: the side lines end on, in either direction) of a few spaces' width; a margin around the control does not help, as
  the field cannot draw there. The mouse follows the carets as they are drawn: a click at one gives it, and a click beyond the End caret
  gives the End caret.
- **Very long paragraphs**: runs that look alike are one stretch of text to Avalonia's formatter, and a paragraph's ranges are found by halving, so a layout grows with the runs. Avalonia's
  formatter itself (about nine tenths of a layout's time, on both versions) carries what is left of the text from each line to the next, so a paragraph of tens of thousands of runs
  costs more than its length: 64,000 runs lay out in about 2.0 s on 11.3 and 1.6 s on 12. From 16,000 runs (after runs that look alike are one) a paragraph of Latin text (letters, their marks, punctuation) that is not right to left
  is formatted line by line from a window of its text instead (`ParagraphLayout.FormatWindowed`), which gives the same lines and carets, but for the kerning of a pair across a line start (widths move by a fraction of a pixel), in about 1.1 s on 11.3 and 0.46 s on 12 for those 64,000 runs; a paragraph with any other script is formatted whole, as the directions of right-to-left text are resolved for all of it at once and a neutral character at a line start keeps the fallback font of the text before it (CJK, Thai).
- **Graphemes**: `GraphemeClusters` is `StringInfo` plus Indic conjuncts (GB9c, six scripts; .NET 10 lacks it). The
  netstandard2.0 build also fuses Extend, SpacingMark, emoji ZWJ sequences and flags per UAX #29, with approximated
  property tables; caret stops in the field come from the shaper, except on a right-to-left paragraph's line of several runs
  (`LineInfo.UsesRuns`), where the layout steps by this class's grapheme boundaries and measures each on the run's glyphs (with the
  approximated tables on netstandard2.0).
- **Undo** is a host stand-in (`SimpleUndoStack`); no LCModel glue exists.

## Licence

MIT, copyright SIL Global. See `LICENSE`. FieldWorks code is LGPL and is not copied here; see `AGENTS.md`.
