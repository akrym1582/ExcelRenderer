# Retained workbook inputs

These immutable XLSX inputs were captured from the test builders at parent revision
`24c9dea4d67190951d152eef56f76abc943ef733` before subsequent SDK mutations.
Names identify the original fixture/test and content identity. Parameterized tests
select the appropriate original variant, or explicitly edit dynamic cell text
with the Open XML SDK. Do not regenerate them using the replacement reader/writer.

The capture script, original call sites and byte hashes are recorded in
`docs/validation/closedxml-removal/retained-fixture-provenance.json` and
`baseline-retained-fixture-capture.py`. The historical generator runs outside the
active project/package graph. Current tests do not reference the removed library.
