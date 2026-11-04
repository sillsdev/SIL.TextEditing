# Spell-checking rules the earlier ADRs left open

ADR 0020 says the host supplies the dictionaries and the field checks visible paragraphs when idle, draws the squiggles
and applies FieldWorks' skip rules. This records how the field does it. Where FieldWorks' behaviour was read in its
sources (ADR 0001), the field matches it. Where it was not confirmed, or not clear, or the field deliberately differs,
the choice is listed under defaults or deviations and can change without superseding anything. Entries there are not
owner decisions.

**Matches FieldWorks**
- **The host owns the dictionaries.** `ISpellCheckHost.GetChecker(ws)` returns an `ISpellChecker` per writing system, or
  null (that writing system is not checked). The checker is asked `Check(word)` with the word in NFC and, for the
  menu, `Suggest(word)`. A writing system with no checker is skipped, but a word that mixes writing systems is still
  marked.
- **A word is a maximal run of word-forming characters, across runs.** Word-forming is asked of the host per writing
  system (`ISpellCheckHost.IsWordForming`; a host with no opinion returns `SpellScanner.DefaultIsWordForming`: letters,
  combining marks and modifier symbols). Digits always form words, and so does an object character, so "abc123" is one
  word and a word split over two runs of one writing system is one word. FieldWorks' writing systems can make an
  apostrophe or a hyphen a letter; the host says so through `IsWordForming`.
- **Skipped text.** A run whose spell-check property (26) is do-not-check is never checked. A run that may not be edited
  is not checked unless the property says force-check; such a run is cut off the ends of a word.
- **Two kinds of problem word are not spellings.** After the ends are cut, a word with text that is not checked inside
  it, or text of more than one writing system, is `Mixed` (blue) and the dictionary is not asked. A word that holds an
  embedded object next to text is `Embedded` (red; FieldWorks offers "insert missing spaces" for it). Neither offers "Add to
  dictionary".
- **Menu.** The first seven suggestions go in the menu and the rest in an "Additional suggestions" submenu
  (`SpellingSuggestions.MaxInline`, `Inline`, `Additional`); a wrong word with none answers an empty list ("No
  suggestions"), distinct from not being wrong. "Add to dictionary" hands the word to the host's checker
  (`ISpellChecker.Add`), which does its own undo.
- **Insert missing spaces.** For a mixed or embedded word the host calls `SpellingSuggestions.WithMissingSpaces()` and
  `RichTextField.ApplySpellingSuggestion(suggestions, runs)`: a space goes between parts that a dictionary cannot judge
  together, each part keeps its writing system and properties, and an object stays. A string replacement is for a
  misspelling (or to make a mixed word one writing system) and is refused for an embedded word.
- **Red squiggle, no change to the model.** The marks are drawn like the squiggle underline and are not text properties.

**Defaults (ours; not confirmed in FieldWorks)**
- **Which layers set the spell-check property.** The field reads the paragraph style, the paragraph's own properties, the
  character style and the run's own properties, later layers winning, as FieldWorks' drawing engine layers its
  properties. FieldWorks' menu code disagrees: there the character style overrides the run's own value and the
  paragraph style is not read. The field has one answer for marks and menu and follows the first.
- **Results are dropped with their paragraph on an edit.** An edit replaces the paragraph's results with none until the
  next idle step, so a squiggle is never drawn on text it was not made for. They are not shifted: FieldWorks also
  checks a changed paragraph again from scratch. A result also stores the text it was made for and is ignored if the
  paragraph text no longer matches.
- **Apostrophes and joiners inside a word, switchable.** An apostrophe (U+0027, U+2019) or a zero-width joiner or
  non-joiner (U+200D, U+200C) with word-forming characters on both sides stays in the word, so "don't" is one word.
  FieldWorks has no such rule: there "don't" splits into "don" and "t" unless the writing system's valid characters make
  the apostrophe a letter. The host decides through `ISpellCheckHost.JoinsWords`; `SpellScanner.DefaultJoinsWords` is
  the rule above and a host that returns false for everything gets FieldWorks' behaviour. The rule agrees with the
  double-click word rule. Hyphens end a word ("well-known" is two words).
- **Supplementary-plane letters are word-forming (a deviation).** The field walks code points. FieldWorks walks UTF-16 units, so
  it splits a word at a surrogate pair. The field's answer is better for scripts outside the basic plane, and a host
  that wraps a UTF-16 based writing-system test receives the whole code point.
- **Numbers alone are not asked.** A word of only numbers goes to no dictionary; FieldWorks asks, and relies on Hunspell
  accepting them. "abc123" is asked.
- **All capitals are asked like any word.** No special case.
- **A read-only field checks only forced text.** The field's read-only flag counts as "may not be edited"; changing
  it checks again. Semi-editable text counts as editable.
- **Objects.** An object, or a no-break space (U+FEFF, the character FieldWorks puts before a footnote marker) and an
  object, is not a word to mark: a lone object, an object between spaces and a footnote marker get no squiggle. FieldWorks
  counts the object and U+FEFF as word-forming and asks the dictionary about them, which the field does not. A word with an object
  next to text is `Embedded` and is not asked.
- **Choosing a suggestion keeps the first run's properties.** The string `ApplySpellingSuggestion` replaces the word with a run
  in the word's writing system carrying the first run's properties (FieldWorks' replacement keeps only the writing
  system). Either overload is one undo step of its own, refused when the text can no longer be edited, is hidden by a line
  limit, or is not the word the menu was made for. The caret ends after the replacement.
- **The menu is the host's.** The field offers `SpellingSuggestionsAt(position)` as the spelling part of the context
  information ADR 0021 describes; building the menu and its wording are the host's.
- **Ignore is optional.** FieldWorks' dictionaries have no ignore operation and its menu has no "Ignore". A checker that
  can ignore a word also implements `ISpellIgnorer`; `SpellingSuggestions.CanIgnore` says so and the host offers the item
  only then.
- **The host announces dictionary changes.** `ISpellCheckHost.DictionariesChanged` is raised by the host (any thread) when an
  add, the undo of an add, or a dictionary change alters what its checkers answer; every field using the host checks
  again. A field listens only while it is in the visual tree. The field that asked for an add also checks again itself.
- **When, and where.** One paragraph per idle step, in document order, only paragraphs in view and only while the field is in
  the visual tree: the paragraph's top and bottom against the scroll viewer the field is in, else against its window, else
  against a fixed span (2000 device-independent pixels) below its top. FieldWorks checks the whole view, including
  paragraphs out of sight; the field's choice is ADR 0020's, and it is bounded so a long document in a host that scrolls
  some other way is not checked whole. Scrolling, an edit to a paragraph, a new host, `RestartSpelling` and a change of the
  read-only state make paragraphs due again. Idle means the dispatcher's background priority, one request pending at a
  time; a test replaces the post (`SpellIdlePost`).
- **Hidden text is not checked.** Text a line limit hides is not checked, no menu is offered for it and no replacement is
  applied to it. A word that crosses the limit is checked whole. FieldWorks checks it. A narrower or wider field makes a
  limited paragraph due again.
- **A failing host does not end the application.** An exception from a checker or from the host's word tests is caught in the
  idle step; the field stops checking that writing system (until `RestartSpelling` or a new host), takes the failing word as
  right, and raises `RichTextField.SpellCheckerFailed` once with the writing system and the exception. A failure while a menu is made gives
  no suggestions. An exception from `Add` or `Ignore` is the host's to see.
- **The word of a mixed word is NFC.** `SpellingSuggestions.Word` is NFC for every kind; an embedded word keeps its objects.
- **A mark does not cover the input method's composing text.** One that starts where composing text is inserted starts after
  it, one that ends there ends before it, and one with the composing text inside it is not drawn.

Written at the author's request, 2026-11-04; amended the same day after review of the first version.
