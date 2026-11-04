# Host-hook choices ADR 0021 left open

ADR 0021 says find and replace, context menus and paging go through host hooks. This records what the field does for each.
Where FieldWorks' behaviour is known (ADR 0001) the field matches it. Where it was not confirmed, the choice is a default,
marked as one, and can change without superseding anything.

## Find and replace

The host's dialog owns the options, the buttons and the wrap prompts. The editor (UI-free) searches, selects and replaces; the
field draws the highlights. `DocumentEditor.TryFind`, `FindAll`, `SelectRange`, `ReplaceMatch`, `ReplaceCurrent` and `ReplaceAll`
take a `FindOptions`; a match is a `TextRange` inside one paragraph.

- **Options that apply to one field's text.** Match case, match diacritics, whole word, a writing system, a character style and
  regular expressions. Not offered: compatibility matching (NFKC; FieldWorks does not implement it either), searching by tags
  and the finding of text across several fields (the host runs one search per field).
- **Matching is ordinal over NFD text, with our own folding.** The pattern and the text are both turned into NFD, so a
  precomposed letter and its decomposed form find each other whatever form the document stores, and offsets are mapped back to
  the document's own text. A match never starts inside a precomposed letter (the accent alone does not find the accent of
  "é" stored precomposed, but does find it in decomposed text) and never splits a surrogate pair. Case is folded per UTF-16
  unit with the invariant culture, and combining marks are removed from the comparison by our own rule, not by the platform's
  culture-aware comparison, because .NET Framework and .NET disagree on those and the field must give one answer on both.
  This is a default: FieldWorks compares with the writing system's own ICU collation, which the field does not have, so a
  language whose rules treat "ch" or "ñ" as one letter will not match as it does in FieldWorks.
- **Case and diacritics, as FieldWorks' collation strengths do.** Default is to ignore case and match diacritics. Matching case
  also matches diacritics exactly, whatever the diacritics option says. With diacritics matched, a match that a combining mark
  follows is refused ("e" does not find the first letter of "é" in decomposed text). With them ignored, marks are skipped in the
  text and the pattern, and a match swallows the marks that follow it, so the whole letter is selected.
- **A combining mark is a character of general category Mn, Mc or Me.** FieldWorks uses ICU's diacritic and extender properties,
  which .NET does not expose; modifier letters and modifier symbols are treated as letters here. A default.
- **Whole word** uses the field's word rule (`WordBreaks`), the same one as double-click: a match must start and end where a
  word, a run of white space or a run of punctuation starts or ends. A default for the rule; FieldWorks uses the writing
  system's ICU word breaker. Whole word and diacritics are not used with regular expressions (FieldWorks disables them too).
- **Embedded objects are ignorable.** A match may span one (the pattern "abcd" finds "ab", an object and "cd"), and one never
  forms the start or end of a match. Replacing keeps the objects of the match, after the new text, as FieldWorks does. Read from
  FieldWorks' replace, which re-appends the objects of the replaced range; whether its search ignores them was not confirmed,
  so ignoring them is a default.
- **An empty pattern searches by properties.** With a writing system or character style set and no text, each stretch of
  consecutive runs that has them is a match; replacing it with nothing keeps the text and changes only the writing system or
  style (the replacement's own writing system and style, when given). With neither set, an empty pattern finds nothing.
- **Regular expressions** are .NET's, over the NFD text, with case folded unless match case is on. FieldWorks uses ICU's
  syntax, which differs in the corners. A pattern that does not compile matches nothing, and `FindOptions.IsValid` says why. A
  zero-length match is skipped (FieldWorks widens one to a character); a default.
- **Find Next and Find Previous start from the selection** (after its end, before its start), as FieldWorks does. Wrapping is
  the host's to ask for: `TryFind(..., wrap: true, ...)` continues from the other end and says it wrapped. A backward search
  finds overlapping matches a forward one steps over (the forward set is greedy, left to right); a default.
- **Selecting a match selects exactly it**, in read-only text too; it does not move to the nearest editable place the way a
  caret does.
- **Text a line limit hides is not searched**, because a selection cannot reach it (ADR 0024). A default.
- **Replace** needs the whole match to be fully editable: FieldWorks refuses a replacement in read-only and in semi-editable
  text alike, as it refuses formatting there. The replacement takes the properties of the first run of the match; the host can
  give it another writing system or character style. It is normalised to the form the host stores. A line break in the
  replacement becomes a hard line break (U+2028), never a paragraph break; a default. The replacement is one undo step, and
  the caret ends after it; the host then asks for the next match, as FieldWorks' Replace button does.
- **Replace All is one undo step** (one task in FieldWorks too) and replaces the matches from the last to the first, so no
  replacement moves another. Text that was put in is not searched again, so replacing "a" with "aa" cannot loop. The selection
  is kept and moves with the text (FieldWorks restores the old selection without adjusting it for the change in length); a
  default. Matches that cannot be replaced are left, and the count says how many were replaced.
