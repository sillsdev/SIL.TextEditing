# Edit-rule choices the earlier ADRs left open

ADRs 0003, 0012, 0019, 0021 and 0023 set the shape of editing, leaving a field, editability and undo steps. The edit
rules in the field had to settle some details they did not. This records what the field does. Where FieldWorks'
behaviour is known (ADR 0001), the field matches it. Where it was not confirmed, the choice is a default, marked as
one, and can change without superseding anything.

- **Shift+Enter inserts a hard line break in both modes.** It puts a line separator (U+2028) at the caret, over any
  selection, without splitting the paragraph, as FieldWorks does. This is the same in single-paragraph mode: Shift+Enter
  never raises `EnterRequested`. Only plain Enter (and Enter with Ctrl, Alt or Meta) does, because the single-paragraph
  field cannot split. The break ends its line: the caret before it stays on that line, a break at the end of a
  paragraph leaves an empty last line the caret can sit on, and the break is never drawn. It is one undo step of its
  own. Matches FieldWorks.
- **Tab and its variants leave only when the field has nowhere to go.** Tab, Shift+Tab, Ctrl+Tab and Ctrl+Shift+Tab
  first move to the next, previous, last or first editable place; they raise `LeaveRequested` only when there is none
  (ADR 0021). The event reports a logical direction (Next or Previous; in a right-to-left paragraph Left is Next) and
  also the key pressed (`LeaveKey`), so the host can tell an arrow from a Tab. Ctrl+Tab and Ctrl+Shift+Tab set `ToEnd`.
  Tab with Alt or Meta is not handled. Matches FieldWorks for the direction rule; reporting the key too is ours, because
  the host (DataTree today) must tell the two apart.
- **Semi-editable text takes typing, deletion and paste; it cannot be formatted.** The field treats a semi-editable
  run as editable for everything except formatting (ADR 0019). `CanFormatSelection` is true only when every run in the
  selection is fully editable. Matches FieldWorks, which uses semi-editable for type-ahead boxes.
- **Paste is one undo step; consecutive deletes are not grouped.** A paste, with the selection it replaces and any
  paragraph breaks it makes, is one step that neither joins the typing before it nor the typing after it. Each Backspace
  or Delete is a step of its own and ends any typing step (ADR 0023). Default: FieldWorks makes a step per delete
  and per paste, but whether LCModel merges consecutive units was not confirmed (feature inventory, group 4).
- **Paste in single-paragraph mode joins paragraphs with a space.** Non-empty pasted paragraphs become one, with one
  plain space between them unless whitespace is already there; empty paragraphs add nothing, so a trailing newline
  leaves no trailing space. Separators inside a run (CR, LF, NEL, U+2029) become spaces; U+2028 is kept as a hard line
  break. Typed text gets the same treatment. Default: FieldWorks' result for a single-line view was not confirmed.

**Still open:** whether a host closes its undo task when it receives `UndoGroupEnded`, or when the next delta with
`StartsUndoGroup` arrives. The field supports the first (it closes the open step before it raises an undo or redo
request, so the event never fires in the middle of an undo), and the second is not ruled out. Not decided here.

Owner decision, 2026-11-03.
