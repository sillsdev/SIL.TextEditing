# SIL.TextEditing moves into Motif as a sealed folder that can be pulled out again

SIL.TextEditing is copied into the Motif repository (`sillsdev/motif`) as one folder, `textediting/`, in a single import
commit. It keeps its own build and its two Avalonia lines, so that it can be pulled out again by copying the folder:
back into its own repository, or straight into FieldWorks.

**The rules for `textediting/`:**

- **Sealed, one way.** Motif projects may reference projects inside `textediting/`. Nothing inside it references a
  file outside it, except the one shared-settings import below. A check (a test or CI step) fails on any other path
  that leaves the folder.
- **Its own build environment.** `textediting/` has its own `Directory.Build.props`, `Directory.Packages.props`,
  `SIL.TextEditing.sln`, tests, samples and conformance fixtures. MSBuild uses the nearest of these files, so Motif's
  root build (one shared `bin`, a forced runtime identifier, single targeting, implicit usings) never reaches it.
- **Two Avalonia lines, as before (ADR 0009).** netstandard2.0 against Avalonia 11.3 for FieldWorks on .NET Framework
  4.8, and net10.0 against Avalonia 12 for Motif. These two pins stay in the folder's own `Directory.Packages.props`.
- **Shared settings, one import.** Motif splits the settings both sides want (language version, nullable, warnings as
  errors, deterministic builds, licence and authors, analysers) into one common file. `textediting/Directory.Build.props`
  imports it. Pulling the folder out replaces that import with the same few lines.
- **SIL packages track Motif.** libpalaso and liblcm versions come from Motif's `SilVersions.props`. FieldWorks can
  raise SIL package versions easily. What it cannot do easily is move from .NET Framework 4.8 to .NET 10 or from
  Avalonia 11 to 12, so the folder keeps the netstandard2.0 and Avalonia 11 line until FieldWorks makes that move.
- **Its own gate.** A Windows CI job, filtered to `textediting/**`, builds both lines and runs the net10.0 and net48
  tests of `SIL.TextEditing.sln`, as this repository's CI did. Motif's cross-platform matrix and `test.ps1` do not run
  them. The workflow file is part of what moves out again.
- **Its own documents.** `textediting/docs/adr` keeps this numbering (0001 onwards), with `GLOSSARY.md`, `AGENTS.md`
  and `docs/`. Motif records its side of the boundary in an ADR of its own.
- **Names do not change.** Projects, namespaces and package ids stay `SIL.TextEditing.*`, so FieldWorks code written
  against them needs no change when the folder is pulled out.
- **Still MIT and clean-room (ADR 0010).** No FieldWorks code is copied into the folder.

**Pulling it out again**, into its own repository or into FieldWorks: copy `textediting/`, replace the shared-settings
import, take the CI job along, and point SIL package versions at the destination's pins. History stays in Motif; the
import commit names the SIL.TextEditing commit it came from.

**The old repository:** open issues move to `sillsdev/motif` (`gh issue transfer`, with a `textediting` label). The
repository gets a README pointing at `motif/textediting` and is archived, not deleted, so it can be unarchived if the
code moves back.

**Consequences:**
- Amends ADR 0002 and ADR 0016: SIL package versions follow Motif, not FieldWorks' pins. The netstandard2.0 / Avalonia
  11.3 line is the compatibility promise to FieldWorks, not the package versions.
- ADR 0009, ADR 0010 and ADR 0031 stand.
- History before the move stays in the archived repository; there is no subtree history in Motif.

Owner decision, 2026-11-06.
