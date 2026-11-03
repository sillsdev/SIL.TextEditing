# Agent rules for SIL.TextEditing

- **Read first.** `GLOSSARY.md` for the words, `docs/adr/` for the decisions. Don't reverse an ADR silently: propose a
  new one that supersedes it.
- **Licence (ADR 0010).** This repository is MIT. FieldWorks code is LGPL. Never copy FieldWorks code here, not even
  edited. Re-implement FieldWorks behaviour from documentation, standards, tests and observed behaviour. Property
  numbers, run shapes, paragraph-key rules and clipboard formats are facts; the code is ours.
- **Layering.** `src/SIL.TextEditing` is UI-free and has no Avalonia or liblcm reference. `src/SIL.TextEditing.Avalonia`
  holds `RichTextField` and everything that touches Avalonia. Samples are samples and do not ship.
- **One source tree, two Avalonia versions.** netstandard2.0 builds against Avalonia 11.3.17 (FieldWorks, net48);
  net10.0 builds against Avalonia 12 (Motif). Versions live in `Directory.Packages.props` only. A per-target
  difference goes behind `#if NET10_0_OR_GREATER`, is small, and is listed in the README.
- **Packages.** Only libpalaso's `SIL.WritingSystems` (ADR 0002), Avalonia (and its Headless, Skia, Desktop, Themes.Fluent and Win32.Interoperability packages; Desktop, Themes.Fluent and Win32.Interoperability are samples-only) and the
  xunit test stack. Ask before adding anything else.
- **Warnings are errors, nullable is on.** `IsTrimmable` and `IsAotCompatible` stay on for net10.0 libraries.
- **No "Fw" prefixes** in names. Namespaces are `SIL.TextEditing` and `SIL.TextEditing.Avalonia`.
- **Run model.** `Run` is shaped like one ITsString run (Ws and ObjData are fields, mapped by the adapter): text, writing-system id, an opaque `PropertyBag` keyed by FieldWorks
  property numbers, and `ObjData`. The editor never interprets a bag; it carries it through every edit.
- **Tests.** Build and test on Windows (`dotnet test SIL.TextEditing.sln`): net48 only runs there. A test that pins a
  known Avalonia fault says so in its name; when it starts failing, remove the workaround, not the test.
- **Commits.** One concern per commit, a one-sentence message saying what is now true.
