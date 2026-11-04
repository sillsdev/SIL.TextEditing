# LCModel lives in a separate adapter package

`SIL.TextEditing` and `SIL.TextEditing.Avalonia` never reference LCModel. A third package, `SIL.TextEditing.LCModel`,
converts between `ITsString` and runs and reads and writes FieldWorks' "TsString" clipboard XML. Only the LCModel-side
code of FieldWorks and Motif references it.

This keeps three things working. FieldWorks' `EngineIsolationAuditTests` forbids direct LCModel references from
FwAvalonia. Motif's GUI reaches LCModel only through its host project. The playground and headless tests run without
a project.

Owner decision, 2026-11-03.

Refined 2026-11-04: the clipboard forms, and the order paste reads them in, are in ADR 0029.
