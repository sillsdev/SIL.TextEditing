# MIT licence, clean-room from FieldWorks

This repository is MIT. FieldWorks is LGPL, so none of its code is copied here, not even when edited. FieldWorks'
behaviour and data shapes are facts and may be re-implemented: property numbers, run structure, paragraph-key rules
and clipboard formats. The code is written fresh, from documentation, standards (such as UAX #29), tests and
observed behaviour.

**Consequences:** MIT lets Motif ship the field in a single Native AOT binary without LGPL relinking duties. The rule
has already been enforced once: the first grapheme helper was too close to FieldWorks' code and was rewritten from
UAX #29. AGENTS.md carries the rule for agents.

Owner decision, 2026-11-03.
