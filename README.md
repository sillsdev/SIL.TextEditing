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
| `src/SIL.TextEditing` | netstandard2.0, net10.0 | UI-free run model (`Run`, `Paragraph`, `Document`, `PropertyBag`), editing (`DocumentEditor`), paragraph-identity rules, clipboard/debug interchange, grapheme helpers. No Avalonia, no liblcm. |
| `src/SIL.TextEditing.Avalonia` | netstandard2.0, net10.0 | `RichTextField`: layout with Avalonia's `TextFormatter`, caret, selection, hit testing, bidi caret stops, IME client, clipboard. |
| `tests/SIL.TextEditing.Tests` | net48, net10.0 | Unit tests for the model. |
| `tests/SIL.TextEditing.Avalonia.Tests` | net48, net10.0 | Avalonia.Headless tests for the surface (Skia, real shaping). |
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
- **Paragraph and character properties**: only bold, italic, underline, font size and the writing system's font are
  drawn; other bag entries are carried untouched. Per-run OpenType features are not read (the writing system's
  features are).
- **Graphemes**: `GraphemeClusters` is `StringInfo` plus Indic conjuncts (GB9c, six scripts; .NET 10 lacks it). The
  netstandard2.0 build also fuses Extend, SpacingMark, emoji ZWJ sequences and flags per UAX #29, with approximated
  property tables; caret stops in the field come from the shaper, not from this class.
- **Undo** is a host stand-in (`SimpleUndoStack`); no LCModel glue exists.

## Licence

MIT, copyright SIL Global. See `LICENSE`. FieldWorks code is LGPL and is not copied here; see `AGENTS.md`.
