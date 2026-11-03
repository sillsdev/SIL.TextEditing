# Writing systems come from libpalaso

The field reads writing-system facts from libpalaso's `SIL.WritingSystems` (`WritingSystemDefinition`): font,
direction, font features and keyboard. FieldWorks and Motif already use the same definitions, so a separate
`IWritingSystemInfo` would only copy them.

**Considered options:** our own small interface, filled in by each host. Rejected by the owner: "Let's not reinvent
the wheel."

**Consequences:** the package depends on libpalaso, and through it on ICU. Its versions must match what FieldWorks and
Motif pin, and the net10.0 build must stay trimmable and AOT-compatible. If libpalaso blocks that, the fix belongs
upstream, not in a local copy.

Owner decision, 2026-11-03.
