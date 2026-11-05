# LCModel lives in a separate adapter package

`SIL.TextEditing` and `SIL.TextEditing.Avalonia` never reference LCModel. A third package, `SIL.TextEditing.LCModel`,
converts between `ITsString` and runs and reads and writes FieldWorks' "TsString" clipboard XML. Only the LCModel-side
code of FieldWorks and Motif references it.

This keeps three things working. FieldWorks' `EngineIsolationAuditTests` forbids direct LCModel references from
FwAvalonia. Motif's GUI reaches LCModel only through its host project. The playground and headless tests run without
a project.

Owner decision, 2026-11-03.

Refined 2026-11-04: the clipboard forms, and the order paste reads them in, are in ADR 0029.

Amended by ADR 0031 (2026-11-05): `SIL.TextEditing.Project` may also reference `SIL.LCModel`, at FieldWorks' pin (hosts may float up). It is not trimmable or AOT-marked, as this adapter is not; the library still has no Avalonia or liblcm reference in the core projects.

Amended 2026-11-05 (issue 136): the adapter never hands liblcm a property its reader refuses. liblcm's text-props XML writer writes some integer properties that its reader then rejects ("Unknown Prop attribute"), and FieldWorks reports a project holding one as a corrupted record. Reproduced against the real serializer in `LiblcmPersistenceTests`: hyphenate (150), maxLines (151), cellBorderWidth (152), cellSpacing (153), cellPadding (154), setRowDefaults (159), relLineHeight (160), directionDepth (129) and ktptMarginTop (50, written `MarginTop`, read only as `marginTop`) throw on read; ktptEditable (155) throws in a paragraph's StyleRules and is ignored on a run. Defaults:
- The opaque bag rule stands: the run model carries these properties unchanged through every edit. The refusal is at the adapter's output only. `ToTsString`, `ToStyleRules`, `ToTextParagraphs` and `FragmentToXml` leave a refused property out of the `ITsString` or StyleRules they build and report each one to an optional `Action<DroppedProperty>` (id `textediting.lcm.unstorable-property`, with the paragraph and run). With no callback they still drop, silently; they never throw. The host decides whether to show a finding. Markdown `p<N>` attributes and a pasted `ITsString` can put these properties in a bag, so the one filter at the adapter covers both.
- A property liblcm silently loses (ktptTableRule 161, unknown numbers, tabList, tabDef, legacy charStyle and paraStyle) is not refused. It cannot corrupt a project, so it stays on the output; liblcm's own save loses it, and the bag keeps it for in-memory uses. A host that must persist such a property needs its own storage.
- The refused list is a fact about liblcm, pinned by tests against its reader. When liblcm loads one back, the test fails and the property leaves the list.
