# Spell-checking rules the earlier ADRs left open

ADR 0020 says the host supplies the dictionaries and the field checks visible paragraphs when idle, draws the squiggles
and applies FieldWorks' skip rules. This records how the field does it. Where FieldWorks' behaviour was read in its
sources (ADR 0001), the field matches it. Where it was not confirmed, or not clear, the choice is a default, marked as
one, and can change without superseding anything. Entries marked as defaults are not owner decisions.

**Matches FieldWorks**
- **The host owns the dictionaries.** `ISpellCheckHost.GetChecker(ws)` returns an `ISpellChecker` per writing system, or
  null (that writing system is not checked). The checker is asked `Check(word)` with the word in NFC and, for the
  menu, `Suggest(word)`. A writing system with no checker is skipped, but a word that mixes writing systems is still
  marked.
- **A word is a maximal run of word-forming characters, across runs.** Word-forming is asked of the host per writing
  system (`ISpellCheckHost.IsWordForming`; a host with no opinion returns `SpellScanner.DefaultIsWordForming`: letters,
  combining marks and modifier symbols). Digits always form words, and so does an object character, so "abc123" is one
  word and a word split over two runs of one writing system is one word. FieldWorks' own writing systems can make an
  apostrophe or a hyphen a letter; the host says so through `IsWordForming`.
- **Skipped text.** A run whose spell-check property (26) is do-not-check is never checked. A run that may not be edited
  is not checked unless the property says force-check; such a run is cut off the ends of a word. The property is read
  from the paragraph style, the paragraph, the character style and the run, later layers winning.
- **Two kinds of problem word are not spellings.** After the ends are cut, a word with text that is not checked inside
  it, or text of more than one writing system, is `Mixed` (blue) and the dictionary is not asked. A word that holds an
  embedded object is `Embedded` (red; FieldWorks offers "insert missing spaces" for it). Neither offers "Add to
  dictionary".
- **When.** One paragraph per idle step, in document order, only paragraphs in view (the paragraph's top and bottom
  against the scroll viewer the field is in, or all of it when there is none). Scrolling, an edit to a paragraph, a new
  host, `RestartSpelling` and a change of the read-only state make paragraphs due again.
- **Menu.** The first seven suggestions go in the menu and the rest in an "Additional suggestions" submenu
  (`SpellingSuggestions.MaxInline`, `Inline`, `Additional`); a wrong word with none answers an empty list ("No
  suggestions"), distinct from not being wrong. "Add to dictionary" hands the word to the host's checker
  (`ISpellChecker.Add`), which does its own undo, and the field then checks again.
- **Red squiggle, no change to the model.** The marks are drawn like the squiggle underline and are not text properties.

**Defaults (ours; not confirmed in FieldWorks)**
- **Results are dropped with their paragraph on an edit.** An edit replaces the paragraph's results with none until the
  next idle step, so a squiggle is never drawn on text it was not made for. They are not shifted: FieldWorks also
  checks a changed paragraph again from scratch. A result also stores the text it was made for and is ignored if the
  paragraph text no longer matches.
- **Apostrophes and joiners inside a word.** An apostrophe (U+0027, U+2019) or a zero-width joiner or non-joiner (U+200D,
  U+200C) with word-forming characters on both sides stays in the word, so "don't" is one word. FieldWorks asks the
  writing system, whose valid characters decide; this is what the field does when the host says nothing, and it agrees
  with the double-click word rule. Hyphens end a word ("well-known" is two words).
- **Numbers alone are not asked.** A word of only numbers goes to no dictionary; Hunspell accepts them. "abc123" is asked.
- **All capitals are asked like any word.** No special case.
- **A read-only field checks only forced text.** The field's read-only flag counts as "may not be edited"; changing
  it checks again. Semi-editable text counts as editable.
- **U+FEFF is not word-forming.** FieldWorks counts the character it inserts before footnote callers as part of a word.
- **A word with an object inside is not asked.** FieldWorks asks the dictionary with the object character in the word,
  which never matches; the field marks it `Embedded` without asking.
- **Choosing a suggestion keeps the first run's properties.** `ApplySpellingSuggestion` replaces the word with a run
  in the word's writing system carrying the first run's properties (FieldWorks' replacement keeps only the writing
  system). It is one undo step of its own, refused when the text can no longer be edited or is not the word the menu
  was made for. The caret ends after the replacement.
- **The menu is the host's.** The field offers `SpellingSuggestionsAt(position)` as the spelling part of the context
  information ADR 0021 describes; building the menu, "Ignore" and the wording are the host's. FieldWorks' menu has no
  "Ignore" item, so `ISpellChecker.Ignore` is ours, for hosts that want one.
- **Idle means background priority.** The field posts a step at the dispatcher's background priority, one pending at a
  time, and a test replaces the post (`SpellIdlePost`).
- **A host's exception is the host's.** An exception from a checker propagates out of the step.

Written at the author's request, 2026-11-04.
