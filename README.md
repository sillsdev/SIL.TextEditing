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
| `samples/Host48` | net48 | WinForms host through `WinFormsAvaloniaControlHost`, Avalonia 11.3.17: FieldWorks' shape. |
| `samples/Host10` | net10.0 | Plain Avalonia 12 desktop host: Motif's shape. |

One source tree serves both Avalonia generations. The netstandard2.0 target compiles against **Avalonia 11.3.17**
(what FieldWorks pins; it runs on net48) and the net10.0 target against **Avalonia 12.1.2** (what Motif pins). The
versions are set per target framework in `Directory.Packages.props`. There is currently no `#if` in the source: the spike's one per-version spot (the clipboard API) is not needed,
because Avalonia 11.3.17 already has the `DataTransfer` API that 12 uses, and `FontFeature` parsing is written
without `FontFeatureCollection.Parse` (absent in 11.3) for both. Keep any future per-version difference small, behind
`#if NET10_0_OR_GREATER`, and list it here.

Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Win32.Interoperability are used by the samples only; the
libraries and tests do not reference them.

## Build and test

Windows with the .NET 10 SDK and the .NET Framework 4.8 targeting pack (net48 builds and tests run only on Windows):

```
dotnet build SIL.TextEditing.sln
dotnet test  SIL.TextEditing.sln      # both target frameworks of both test projects
```

From WSL, run the same commands through `powershell.exe`. The samples are built by the solution; run
`bin\Host48\Debug\net48\Host48.exe` or `bin\Host10\Debug\net10.0\Host10.exe` to try them. The headless tests use the
Windows fonts Segoe UI, Nirmala UI, Leelawadee UI and Constantia.

## Known gaps

- **Accessibility**: no UIA `TextPattern`; the field is an Edit with a name and a value only. Inside a net48
  `WinFormsAvaloniaControlHost` Avalonia's UIA subtree was empty in the spike (unexplained).
- **Input**: real IME and Keyman interaction are untested (the headless tests drive the IME client directly). Missing:
  double-click word and triple-click paragraph selection, drag across paragraphs, caret affinity at wrapped line ends,
  scrolling inside long fields, drag and drop, spelling squiggles, find highlights, touch.
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
