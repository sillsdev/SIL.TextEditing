# Manual testing

The automated tests cannot do these: real keyboards, input method editors, Keyman, a screen reader, the mouse, other
applications' clipboards and focus handoff in a real WinForms form. A person does them with the **playground**, the
shared sample UI that all three hosts show. It is meant for a tester who has not seen the code.

Write **PASS** or **FAIL** in the Result column of every step you do, and put what you saw in Notes when it differs
from Expected. A step you could not do is **SKIP** with the reason. Copy the inspector or the event log (select the
text, Ctrl+C) into a bug report; they are plain text and show exactly what the model holds.

## Start

From the repository root on Windows (the .NET 10 SDK and the .NET Framework 4.8 targeting pack are needed):

| Command | What it is | Avalonia |
|---|---|---|
| `run desktop10` | Plain Avalonia desktop window on .NET 10: Motif's shape | 12.1.2 |
| `run desktop48` | The same window on .NET Framework 4.8 | 11.3.17 |
| `run winforms48` | A WinForms form: WinForms TextBox, the playground, WinForms TextBox. FieldWorks' shape | 11.3.17 |

`run <host> --scenario "Inline picture (ORC)"` opens a scenario directly. The first run builds the host.

Where a step says **all hosts**, do it on each of the three and write one result per host (D10, D48, W48). Steps for
`W48` only need the WinForms host.

## The window

* **Top**: scenario picker; field count (for the Many fields scenario); **B I U**; Undo, Redo; Picture, Cut, Copy,
  Paste. The toolbar never takes focus from the field.
* **Writing-system bar**: `ws:` shows the writing system at the caret. Default analysis (en) and default vernacular
  (swh) buttons, then every sample writing system: en, fr (analysis) and ar, swh, hi, ta, th (vernacular). With a
  selection a button changes the selected text; with a caret it sets what you type next, as a keyboard switch would.
  Sample data only; nothing is read from a project.
* **Field** (left): the editor. Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+Z, Ctrl+Y, Ctrl+A, Ctrl+C, Ctrl+X and Ctrl+V work in it.
* **Inspector** (top right): caret, anchor and selection as paragraph:offset; undo counts; every paragraph with its id,
  direction, style and runs. A run reads `r1 [9,13) ws=ar "text" props{3=1, 9001=42@1}`: offsets, writing system,
  text with escapes (`́` a combining mark, `\U0001F600` a surrogate pair, `￼` an object), then the property
  bag as `number=value`, `@n` for a variation. Documents over 30 paragraphs show a window around the caret.
* **Event log** (bottom right): `caret ws en -> ar: would switch keyboard to ar` (the library only reports; it never
  switches a keyboard), undo and redo requests, clipboard actions, edits other than typing.

## 1. Basics (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 1.1 | Start. Click in the text of the first paragraph and type a few Latin letters. | Letters appear at the caret; the caret moves; `ws:` stays `en`; the inspector's run text and caret offset update with each key. | | | | |
| 1.2 | Pick each scenario in turn. | Each loads without a delay you notice; the description under the toolbar changes; the inspector shows the new document; no text is clipped or overlaps. | | | | |
| 1.3 | In Latin and Arabic, click inside the Arabic word, then inside an English word. | `ws:` follows the caret (`ar`, then `en`); the log gets `would switch keyboard to ar` and `... to en`. | | | | |
| 1.4 | Put a caret in English text, click the `fr` button, type `bonjour`. | `ws: fr`; the typed text is a new run `ws=fr` in the inspector. | | | | |
| 1.5 | Click Default vernacular, type `habari`; then Default analysis. | `ws: swh`, then `ws: en`; run `ws=swh "habari"` in the inspector. | | | | |
| 1.6 | Select a word with Shift+Right, click the `hi` button. | Only the selected text becomes `ws=hi` and is drawn in the Hindi font; the selection stays. Undo restores it. | | | | |
| 1.7 | Select a word, press B, then B again; then Ctrl+I and Ctrl+U. | Bold, italic, underline are drawn; the inspector shows `props{3=1}` (bold), `2=1` (italic), `5=1` (underline); pressing B again removes it and the runs merge back. Paragraph ids do not change. | | | | |
| 1.8 | Type, press Undo, Undo, Redo, Redo. Then Ctrl+Z and Ctrl+Y in the field. | Text, caret and the inspector's `undo n done, n undone` follow; the log says `undo requested (button)` or `(Ctrl+Z in the field)`. | | | | |
| 1.9 | Press Enter in the middle of a paragraph, then Backspace at the start of the new one. | Two paragraphs, then one. The first keeps its id on a split and on the join; the new right-hand paragraph has a new id (compare in the inspector). | | | | |
| 1.10 | Press Enter at the very start of a paragraph. | A new empty paragraph appears above; the original id stays on the paragraph that holds the text. | | | | |
| 1.11 | Scenario *Inline picture*: put the caret after a picture, Backspace; press Picture to insert one; Delete before a picture. | A picture is deleted or inserted as one unit (one character, `￼`), never half. Works in the Arabic paragraph too. | | | | |
| 1.12 | Scenario *Unknown properties*: select the whole second paragraph, press B twice, then split and join it with Enter and Backspace. | Properties 9001, 9002, 9003 and the paragraph style are still in the inspector, unchanged. | | | | |

## 2. Keyboard layout switching on caret moves (all hosts)

The library reports the writing system at the caret; it does not switch layouts yet. This checks what a host would
be told.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 2.1 | Add an Arabic keyboard in Windows. In Latin and Arabic, arrow through a boundary between English and Arabic text. | At the boundary the log gets exactly one `would switch keyboard to ...` line per change of writing system, none while the writing system is unchanged. | | | | |
| 2.2 | Change the Windows layout with Win+Space while the caret is in an `fr` run. | Nothing happens in the field or the log, and the typed text stays `fr`: switching layouts does not change the writing system yet. Record what you see; this is the gap a future host wiring closes. | | | | |

## 3. Real typing and IME (all hosts)

Add the input methods under Settings, Time and language, Language: Japanese (Microsoft IME) and Chinese (Pinyin).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 3.1 | Type a sentence of English, French accents (AltGr or a French layout) and punctuation. | Everything appears as typed; dead keys compose (`e` after `^`); no doubled or lost letters. | | | | |
| 3.2 | Japanese IME: in the *Empty* scenario type `nihongo`. | The composition shows inline with an underline; the candidate window sits at the caret; Space cycles candidates; Enter commits the text into the field; the inspector shows it only after the commit. | | | | |
| 3.3 | Same, then press Esc during composition. | The composition disappears and nothing is inserted. | | | | |
| 3.4 | Chinese Pinyin: type `zhongwen`. | Same as 3.2. | | | | |
| 3.5 | Compose while the caret is inside Arabic text, then in the RTL paragraph. | The composition appears at the caret, the candidate window is not left behind, the committed text lands at the caret. | | | | |
| 3.6 | Compose with text selected. | The selection is replaced on commit, not on the first keystroke. | | | | |
| 3.7 | Move the window while a composition is open. | The candidate window follows the caret. | | | | |

## 4. Keyman (all hosts; needs Keyman Desktop and a keyboard that reorders or deletes, such as Khmer or Tamil 99)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 4.1 | Select the Keyman keyboard, type a sequence in the *Empty* scenario. | The result matches typing the same keys in Notepad. | | | | |
| 4.2 | Type a sequence in which a later key rewrites earlier characters (reorder, or replace the last syllable). | The earlier characters are changed correctly: the field reports its surrounding text to the keyboard. Check the inspector for stray characters. | | | | |
| 4.3 | Move the caret into the middle of existing text and type the same sequence. | Same result as at the end of the text. | | | | |

## 5. Right-to-left: caret and selection by mouse and arrows (all hosts)

Scenario *Latin and Arabic*. **Known:** on Avalonia 11.3.17 (D48 and W48) clicking in an RTL paragraph can put the
caret about two characters from where you clicked (fixed in Avalonia 12). Record it either way.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 5.1 | In the all-Arabic paragraph press Home, then Right repeatedly. | The caret moves to the right on screen with each Right press, one cluster at a time, never inside a joined letter. | | | | |
| 5.2 | Press Left and Right across an English word inside the RTL paragraph. | The caret moves one letter on screen per key, across the language boundary without a jump, and never skips or repeats a position. | | | | |
| 5.3 | Click in the middle of an Arabic word. | The caret is where you clicked (D10). On D48 and W48 it may be two characters off: note it (known). | | | | |
| 5.4 | Click and drag across the boundary between Arabic and English text. | The selection is one visual block or the correct set of blocks for the logical range; the inspector's `selection a -> b` matches the highlight. | | | | |
| 5.5 | Shift+Right and Shift+Left from the middle of the RTL paragraph. | The selection grows and shrinks on screen in the key's direction; the inspector offsets move accordingly. | | | | |
| 5.6 | Scenario *Inline picture*: arrow across the picture in the Arabic paragraph. | The caret passes the picture in one step. (Known: the picture may sit in the wrong place in RTL text on both Avalonia versions.) | | | | |
| 5.7 | Scenario *Devanagari, Tamil, Thai*: arrow and Backspace through conjuncts and stacked marks. | The caret stops between clusters, never inside; Backspace removes what the language expects (compare with Notepad). | | | | |

## 6. Tab and focus in and out of the WinForms host (W48)

Tab order is: first WinForms TextBox, the playground field, second WinForms TextBox. The toolbar and the inspector
are not tab stops (click them).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 6.1 | Click the first TextBox, press Tab. | Focus enters the playground field: the caret shows and typing goes into the field. | n/a | n/a | | |
| 6.2 | Press Tab again. | Focus moves to the second TextBox. | n/a | n/a | | |
| 6.3 | Press Shift+Tab twice. | Focus returns to the field, then to the first TextBox. | n/a | n/a | | |
| 6.4 | In the field press Left, Right, Up, Down, Home, End, Backspace, Delete, Ctrl+A, Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+Z, Ctrl+Y. | Each acts in the field; none moves WinForms focus or beeps; Enter makes a new paragraph and does not press a default button. | n/a | n/a | | |
| 6.5 | Click a toolbar button (Bold), then type. | Focus stays in the field; typing continues. | | | | |
| 6.6 | In the Many fields scenario click different fields, then Tab. | The inspector and `ws:` follow the clicked field; Tab leaves the control rather than walking through every field (note what it does). | n/a | n/a | | |
| 6.7 | Alt+Tab to another program and back. | The caret returns to the field where it was; typing works at once. | | | | |
| 6.8 | D10 and D48: press Tab in the field. | Focus leaves the field (the toolbar is not a tab stop), or stays; note which. | | | n/a | |

## 7. Screen reader (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 7.1 | Start Narrator (Win+Ctrl+Enter), focus the field. | **Known gap:** Narrator reads the field as an edit named "Text field" and not its content; there is no TextPattern. Record exactly what it says. | | | | |
| 7.2 | Navigate to the toolbar buttons and the writing-system buttons. | Each is announced with its name (B, I, U, Undo, Redo, Picture, Cut, Copy, Paste, en, fr, ...). | | | | |
| 7.3 | W48 only: look for any Avalonia content in the Narrator tree. | **Known:** the Avalonia subtree is empty inside the WinForms host. Record what Narrator or Accessibility Insights shows. | n/a | n/a | | |

## 8. Clipboard with other applications (all hosts)

The playground copies two formats: the library's `SIL.TextEditing.Runs.v1` (runs, writing systems and properties) and
plain text.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 8.1 | Select text in *Latin and Arabic* that crosses the English/Arabic boundary, Copy, paste into Notepad. | The plain text arrives, in logical order, with paragraph breaks as line breaks. | | | | |
| 8.2 | Same, paste into Word. | The text arrives. (Formatting need not.) | | | | |
| 8.3 | Type text in Notepad and in Word (with some Arabic), copy it, Paste in the field. | The text appears at the caret in the active writing system; the inspector shows it as one run; a selection is replaced. | | | | |
| 8.4 | Select bold and fr text in the field, Copy, move the caret, Paste. | The pasted runs keep their writing systems and bold (compare the inspector before and after). | | | | |
| 8.5 | *Unknown properties*: copy a run with 9001/9002/9003, paste it into another paragraph. | The properties come with the text, unchanged. | | | | |
| 8.6 | Copy in one host window, paste in another (run `run desktop10` and `run desktop48`). | The runs and properties arrive. | | | | |
| 8.7 | Cut with a selection across paragraphs, then Undo. | The text and paragraph ids return. | | | | |
| 8.8 | From a FieldWorks build, if you have one: copy styled text out of a FieldWorks field, paste here, and the reverse. | Record what arrives; FieldWorks does not read our format yet. | | | | |

## 9. Look and feel (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 9.1 | Drag-resize the window quickly; scroll the *Long document* with the wheel and the scrollbar. | No white flashes or tearing; text re-wraps; the caret stays on the same character. | | | | |
| 9.2 | Move the window to a monitor at 150 percent (or change the scale), or set the scale to 150 percent. | Text is sharp; the caret and selection stay aligned with the glyphs; the picture is not blurry. | | | | |
| 9.3 | *Long document*: press Ctrl+End, type, press Ctrl+Home. | The view follows the caret; typing is not slower than in a short document; the inspector shows a window around the caret. | | | | |
| 9.4 | *Many fields* with 1000 fields (Fields picker, then pick the scenario again): scroll and type in a field far down. | The window stays usable. Record how long it takes to load and whether scrolling stutters. | | | | |
| 9.5 | Select all (Ctrl+A) in the *Long document* and press B. | It finishes in a reasonable time; Undo restores it in one step. | | | | |
| 9.6 | Dark or high-contrast Windows theme. | The field stays readable (it draws its own colours); record anything unreadable. | | | | |

## 10. Mouse and keyboard selection (all hosts)

Scenarios *Latin and Arabic* and *Long document*. Double-click is the system double-click speed; click the same spot.
The word rule: a word is a run of letters, digits and marks (an apostrophe between letters stays in the word, so
`don't` is one word); a run of spaces or of punctuation counts as one unit; there is no dictionary, so Thai and Chinese
are not split into words. Ctrl+Left and Ctrl+Right go by the same rule in logical order, so in a right-to-left paragraph
Ctrl+Left goes to the next word.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 10.1 | Click in the middle of an English word, then on the left half and the right half of one letter. | The caret appears at the nearer side of the letter; no selection; the inspector shows `selection none`. | | | | |
| 10.2 | Click at the edge between English and Arabic text, first on one side of the boundary and then on the other. | The caret is drawn on the side you clicked (two different places can be the same offset); typing there continues the text you clicked next to. | | | | |
| 10.3 | Click a word, then Shift+click further on, then Shift+click before the anchor. | The selection grows from the first click, flips when you pass the anchor, and the inspector's `anchor` never moves. | | | | |
| 10.4 | Drag from the middle of one paragraph to the middle of a later one, then drag back up past the start of the document and down past the end. | Everything between is highlighted, including the whole middle paragraphs; going past the edges selects to the start or the end. | | | | |
| 10.5 | Double-click an English word, an Arabic word, a space and a comma. | The word, the Arabic word, the space run and the punctuation are selected, nothing more. Double-click on the last letter of a word, right half, still picks that word. | | | | |
| 10.6 | Double-click a word and, holding the second press, drag over two more words. | The selection grows a whole word at a time, in both directions. | | | | |
| 10.7 | Triple-click in an English paragraph, an Arabic paragraph, and a wrapped one. | The whole paragraph is selected, over all its lines; holding the third press and dragging adds whole paragraphs. | | | | |
| 10.8 | Click in a long wrapped paragraph, press End, then Home, then End again; then Shift+End. | End puts the caret at the end of the visual line, drawn on that line, not at the start of the next; Home goes to the start of the same line; Shift+End selects to the line end only. | | | | |
| 10.9 | From the End position in 10.8 press Up, Down, Down. | Each key moves one visual line. After a short line the caret returns to about the original horizontal position (the preferred position), not to the short line's end. | | | | |
| 10.10 | Click in a wrapped line just right of its last word. | The caret is drawn at the end of that line, not at the start of the next line. | | | | |
| 10.11 | Ctrl+Right and Ctrl+Left through an English sentence, then through the Arabic paragraph; add Shift. | The caret jumps a word at a time (English: to the start of the next word); at the end of a paragraph the next press enters the neighbouring paragraph; with Shift the selection follows. | | | | |
| 10.12 | Ctrl+Home, Ctrl+End, Ctrl+A, and with Shift for the first two. | The caret goes to the very start and end of the document; Ctrl+A selects everything, and the inspector shows `selection 0:0 -> last:end`. | | | | |
| 10.13 | Select across three paragraphs with the mouse and press Delete (or Backspace, or type a letter). | One paragraph is left holding the text before and after the selection; its id in the inspector is the first paragraph's id (unless the first part was empty, then the last paragraph's id). Undo restores all three. | | | | |

## Record

| | |
|---|---|
| Tester | |
| Date | |
| Windows version | |
| Commit (`git rev-parse --short HEAD`) | |
| Input methods and keyboards installed | |
| Display scale | |
| Screen reader and version | |
