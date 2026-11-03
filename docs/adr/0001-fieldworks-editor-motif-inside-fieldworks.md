# The editor is FieldWorks' editor; Motif uses it as part of FieldWorks

SIL.TextEditing is built for FieldWorks only, not as a general SIL text editor. Motif uses it because Motif will
live inside FieldWorks once FieldWorks moves to Avalonia 12 and .NET 10. Any program that needs several writing
systems in one text is already working with LCModel data, so there is no second audience to design for.

The field inherits everything FieldWorks text editing does today (ADR 0005). It may make small usability improvements
that WinForms made hard, but it never drops a FieldWorks behaviour.

**Consequences:** FieldWorks words and shapes are fine in the API: runs, numbered text properties, writing systems,
styles and paragraph keys. Whether the library may reference LCModel types directly is a separate, open question,
because FieldWorks' `EngineIsolationAuditTests` keeps LCModel out of FwAvalonia.

Owner decision, 2026-11-03. This supersedes the earlier "general library with no FieldWorks concepts" proposal.
