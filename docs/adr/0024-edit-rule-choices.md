# Edit-rule choices the earlier ADRs left open

ADRs 0003, 0012, 0019, 0021 and 0023 set the shape of editing, leaving a field, editability and undo steps. The edit
rules in the field had to settle some details they did not. This records what the field does. Where FieldWorks'
behaviour is known (ADR 0001), the field matches it. Where it was not confirmed, the choice is a default, marked as
one, and can change without superseding anything.

- **Shift+Enter inserts a hard line break in both modes.** It puts a line separator (U+2028) at the caret, over any
  selection, without splitting the paragraph. In multi-paragraph mode this matches FieldWorks. In single-paragraph mode
  it is our default: a FieldWorks view that does not accept Return hands Shift+Enter to its parent too, and its one
  single-line widget that does insert a break does so for plain Enter. Either way Shift+Enter never raises
  `EnterRequested`; only plain Enter (and Enter with Ctrl, Alt or Meta) does, because the single-paragraph field cannot
  split. The break ends its line: the caret before it stays on that line, a break at the end of a paragraph leaves an
  empty last line the caret can sit on, and the break is never drawn (a selection that covers only a break shows a small
  block). It is one undo step of its own. Shift+Enter is refused when the paragraph it would leave, laid out, has more
  lines than its line limit allows or ends in a break whose empty line the limit hides: the field lays out the result of
  the replace (the head of the first paragraph of the selection, the break and the tail of the last) in the format of the
  paragraph that survives it, so a break that reflows the text into the same number of lines is allowed, and one whose
  reflow needs another line, or a selection spanning paragraphs whose joined result does not fit, is refused. The survivor
  is the first paragraph of the selection, except that when the selection starts at the start of the first paragraph and
  leaves text in the last, the last one survives (the non-empty side wins), so its limit decides. Text can still end up
  hidden behind a break from another source (paste, typed U+2028, a join of paragraphs with different limits, host
  data): Delete at the visible end removes that break and the text shows again. A trailing
  break whose empty line the limit hides is not hidden text: the caret stops before it, typing there works, and Delete
  removes it.
- **Tab and its variants leave only when the field has nowhere to go.** Tab, Shift+Tab, Ctrl+Tab and Ctrl+Shift+Tab
  first move to the next, previous, last or first editable place; they raise `LeaveRequested` only when there is none
  (ADR 0021). The event reports a logical direction (Next or Previous; in a right-to-left paragraph Left is Next) and
  also the key pressed (`LeaveKey`), so the host can tell an arrow from a Tab. Ctrl+Tab and Ctrl+Shift+Tab set `ToEnd`.
  Tab with Alt or Meta is not handled. Matches FieldWorks for the direction rule; reporting the key too is ours, because
  the host (DataTree today) must tell the two apart.
- **Semi-editable text takes typing, deletion and paste; it cannot be formatted.** The field treats a semi-editable
  run as editable for everything except formatting (ADR 0019). `CanFormatSelection` is true only when every run in the
  selection is fully editable. Matches FieldWorks, which uses semi-editable for type-ahead boxes. Pasted text takes the
  editable value of the run it lands in when that run is semi-editable (the run before the insertion point, else the one
  after), as typed text does, so a paste into the middle of a semi-editable run leaves it whole; anywhere else it is editable.
- **Paste is one undo step; consecutive deletes are not grouped.** A paste, with the selection it replaces and any
  paragraph breaks it makes, is one step that neither joins the typing before it nor the typing after it. Each Backspace
  or Delete is a step of its own and ends any typing step (ADR 0023). Default: FieldWorks makes a step per delete
  and per paste, but whether LCModel merges consecutive units was not confirmed (feature inventory, group 4).
- **Paste in single-paragraph mode turns each separator into one space and removes tabs.** This is FieldWorks' rule for
  a view that cannot hold paragraphs, and nothing more: each separator (CR, LF, NEL, U+2029; CR LF counts as one)
  becomes one space, across run and paragraph boundaries, and a pasted paragraph break is one too. Separators that end
  the pasted text are dropped. One at the start still gives a space, even at the start of the paragraph. Nothing else is
  collapsed: pasting "a", two newlines and "b" gives "a" and two spaces and "b", white space beside a separator stays, and
  a no-break space is kept. Tab characters are removed (FieldWorks does the same; the field used to keep them, issue #35), and in
  every mode, not only this one: see the next entry.
  U+2028 is kept as a hard line break and is not a separator for this. NEL and U+2029 count as separators here (ADR 0012),
  whereas FieldWorks converts only CR and LF. A separator followed only by tabs is dropped too (tabs are removed first, so
  "a", a newline and a tab gives "a"); this is deliberate and differs slightly from FieldWorks, which keeps a space there.
  Typed text gets the same treatment. CR LF pairs only when the two are next to each other, so CR, tab, LF gives two
  spaces on every path (as in FieldWorks). Pasting only separators and tabs, however many, over a selection replaces the
  selection with nothing. Because a trailing separator is dropped, pasting
  "a" and a newline at the start of "bcd" gives "abcd": the words fuse. That is the FieldWorks result, not a bug.
- **Paste removes tab characters in every mode.** FieldWorks strips tabs from pasted text before it looks at
  paragraphs, so a field that takes paragraphs removes them too; the field used to keep them there (issue #39). It
  applies to a pasted fragment (each paragraph of it) and to pasted plain text. Typed text gets it too, as a default
  (the Tab key is navigation and never types a tab, above). Read from FieldWorks' paste behaviour, not an owner decision.
- **Enter at an editable and read-only boundary can leave a paragraph the field cannot join back.** Enter between an
  editable and a read-only run can make a paragraph that is entirely read-only. Backspace into it is refused (ADR 0019),
  so only the host's undo removes it. Delete in an empty editable paragraph beside read-only text is refused for the
  same reason.
- **The caret never rests in hidden text, and a hidden editable run is not an editable place.** A line limit hides the
  text after its last visible line (ADR 0019 says where editing is allowed; the limit says what can be reached). The caret
  stops at the visible end and the search for the nearest editable place stops there too. When the only editable text is
  hidden behind a non-editable run (a non-editable U+2028 at the start of a MaxLines 1 paragraph, say), the field acts as
  having no editable place: the caret stays at the visible end, typing, paste and Delete are refused (Delete cannot remove a
  non-editable break), and Tab leaves the field. That is host data the field cannot repair; it settles and does not loop.
  Our choice: FieldWorks was not checked for this combination.

**Still open:** whether a host closes its undo task when it receives `UndoGroupEnded`, or when the next delta with
`StartsUndoGroup` arrives. The field supports the first (it closes the open step before it raises an undo or redo
request, so the event never fires in the middle of an undo), and the second is not ruled out. Not decided here.

Written at the owner's request, 2026-11-03. Entries marked as defaults are not owner decisions and can change without
superseding this ADR.
