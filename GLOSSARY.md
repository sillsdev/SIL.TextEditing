# SIL.TextEditing

The rich text field FieldWorks uses to edit multi-writing-system text, and that Motif uses inside FieldWorks.

## Text

**Run**:
A stretch of text in one writing system with one set of text properties.
_Avoid_: span, segment (a segment is an interlinear unit in FieldWorks)

**Paragraph**:
A sequence of runs with its own paragraph properties and a paragraph key.

**Paragraph key**:
The host's identity for a paragraph, kept through edits by FieldWorks' split and join rules.
_Avoid_: paragraph ID, GUID

**Text property**:
A numbered formatting or data value on a run or paragraph, using FieldWorks' property numbers.

**Carried property**:
A text property the field keeps unchanged through every edit but does not draw, because FieldWorks does not draw it either.
_Avoid_: opaque, unknown, lossy

**Embedded object**:
A picture, link or other object that sits inline in a run as a single placeholder character.
_Avoid_: ORC (in prose)

## Writing systems

**Writing system**:
A language written in one particular way, with its own font, direction and keyboard, as libpalaso defines it.
_Avoid_: WS (in prose), language, script

**Analysis writing system**:
A writing system used to describe and translate the language being studied.

**Vernacular writing system**:
A writing system of the language being studied.
_Avoid_: grammar (for vernacular)

**Default analysis**, **default vernacular**:
The first analysis, or the first vernacular, writing system of a project.

## Roles

**Host**:
The application that owns the data, the undo history, the styles and the keyboards, and embeds fields: FieldWorks, Motif or the playground.

**Field**:
One `RichTextField`: one editable text on screen.
_Avoid_: editor (for a single control), text box
