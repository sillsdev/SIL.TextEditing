# No Graphite support

The field shapes text only through Avalonia's HarfBuzz, with OpenType font features. It does not support Graphite,
even though legacy FieldWorks Views does. A font that depends on Graphite shows its OpenType behaviour, or plain
glyphs if it has none.

**Considered options:** a HarfBuzz native build with graphite2, packaged for net48 and net10. Rejected: it would add
a native build to maintain for a shrinking set of fonts.

Owner decision, 2026-11-03. A deliberate exception to ADR 0005.
