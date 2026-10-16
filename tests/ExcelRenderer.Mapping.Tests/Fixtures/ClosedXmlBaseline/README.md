# Parent Mapping inputs and outputs

Captured from `24c9dea4d67190951d152eef56f76abc943ef733` using the parent's default
writer. `baseline.json` fixes 20 successful cases (ten features, zero/three items).
The new StructuralMappingTests run both OpenXml and Auto against these inputs and
compare values, names, references, covered CF/DV cells, comments, media hashes and
schema errors. Internal link targets intentionally follow the final referenced row.

`advanced-baseline.json` records four successful flat-pivot cases and two sparkline
preflight rejections. The old pivot output keeps stale source ranges/cache values;
current tests assert corrected references and current source records instead.
Sparkline rejection remains unchanged. Generator sources and the source revision
are retained under `docs/validation/closedxml-removal`. Do not regenerate expected
files with current code.
