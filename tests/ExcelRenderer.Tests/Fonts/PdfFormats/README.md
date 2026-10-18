# Original PDF format fixtures

These tiny fonts contain original geometric test glyphs, created for ExcelRenderer
under the repository MIT license. They contain no third-party font outlines.

Reproduce the original static/CFF/variable/collection/COLR v0/v1 assets with
`python3 scripts/create-pdf-font-fixtures.py` (fonttools 4.61.1). Additional
`collection-distinct.ttc`, `collection-v2.ttc` and `collection-mixed.ttc` are
reproduced with `python3 scripts/create-collection-font-fixtures.py`. Digests are
in the parent SHA256SUMS. Generators are validation-only, never invoked by tests.

The additional v1/v2 collections share glyf tables but have different regular/bold
metrics and cmap mappings. The mixed collection contains static TrueType, CFF,
variable, COLR v0 and COLR v1 faces. Tests exercise nonzero indices, normalized
metrics/glyph equality, color drawing, and PDF refusal of CFF/variable faces.
Noto Color Emoji separately exercises the existing CBDT drawing route.
