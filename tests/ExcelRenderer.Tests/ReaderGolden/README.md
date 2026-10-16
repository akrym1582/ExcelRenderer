# Parent Reader goldens

Generated with the unmodified rendering assemblies from parent commit
`24c9dea4d67190951d152eef56f76abc943ef733` (ClosedXml default), using .NET
10.0.401 / runtime 10.0.12, Debian 13 linux-x64, SkiaSharp 4.151.0.
These are synthetic repository sample workbooks, not external user data.

Each model file is named by SHA256 of the repository-relative input path (UTF-8,
forward slashes). `artifacts.json` maps those paths to PNG, SVG and Markdown
artifact hashes. Model projections cover values, styles, spans, formula metadata,
merged borders, print geometry, nondefault dimensions, and image bytes/anchors.
Default row/column dictionary entries are omitted because conversion retains
sparse geometry; separate public-Reader tests require their materialization.
SVG resource IDs are normalized before hashing. PDF timestamps are not hashed.

Rendering uses only `third_party/NotoSansJP/NotoSansJP-Regular.ttf`, with system
fonts and font-pack lookup disabled. Culture is invariant. The font and input
hashes are recorded in `docs/validation/closedxml-removal/golden-provenance.json`.
Golden PNGs require the recorded native renderer; semantic/model tests are the
portable regression evidence. No golden was generated using the replacement Reader.

The baseline generator and exact projection source are retained in
`docs/validation/closedxml-removal/baseline-golden-generator.cs` and
`baseline-reader-snapshot.cs`. Build a detached checkout of the recorded parent,
then compile these two sources in an external net10.0 console project referencing
all DLLs in that checkout's Performance Debug/net10.0 directory. Copy its
runtimes/linux-x64/native/libSkiaSharp.so beside the generator. Invoke with the
repository root and an empty output directory as its two arguments; compare the
results before replacing any checked-in golden. Never regenerate expected output
with the implementation under test.
