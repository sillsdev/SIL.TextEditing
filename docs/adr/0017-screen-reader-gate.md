# A UIA text provider is required before FieldWorks turns the field on by default

Screen readers (Narrator, NVDA, JAWS) read a text through UI Automation's text pattern: the text at the caret, the
selection and each typed character. Without it, a blind user hears only "edit". Legacy FieldWorks Views exposes an
older accessibility interface, so shipping without one would break ADR 0005. Avalonia supplies no text pattern for
custom text controls, so the field implements one.

The gate applies only to FieldWorks' default. Motif's developer preview and an opt-in FieldWorks preview may ship
without it.

Owner decision, 2026-11-03.
