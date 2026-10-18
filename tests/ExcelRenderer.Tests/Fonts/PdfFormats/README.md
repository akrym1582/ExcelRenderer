# Original PDF format fixtures

These tiny fonts contain original geometric test glyphs, created for ExcelRenderer
under the repository's MIT license. They contain no third-party font outlines.

Reproduce them with `python3 scripts/create-pdf-font-fixtures.py` (fonttools 4.61.1).
The files cover static TrueType, CFF OpenType, a TrueType variable face with a
`wght` axis and `gvar`, a two-face TTC, and COLR v0/v1. Public font selection has
no collection-index or variation-coordinate option, so these tests use face 0 and
the default axis position. Existing Noto Color Emoji fixtures exercise CBDT.

The backend test checks C# native preparation, drawing, parsed PDF output and
retained original outlines for non-color faces. It does not claim that PDFsharp
can measure every format: format reachability and measurement restrictions are
reported separately in the PDF validation report.
