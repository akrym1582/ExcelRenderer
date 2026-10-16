# Fixed inputs for Core raster comparisons

These nine workbooks are the output of the unmodified `normalize` command in
`tools/ExcelRenderer.Core.Validation/Program.cs` at parent commit
`24c9dea4d67190951d152eef56f76abc943ef733`. They retain the exact normalization
behavior used for the existing 17 golden raster pages. The generator source hash,
original sample hashes and captured workbook hashes are in `provenance.json`.

The old normalizer saved the samples through ClosedXML, which also rewrote
header/footer formatting strings and other workbook metadata. The replacement
SDK normalizer does not reproduce all those incidental changes. Generating a new
input during each raster comparison would therefore compare different inputs
against the historical images. `scripts/validate-core.py` now verifies the source
and captured hashes, then renders these fixed inputs with both current products.
The existing golden PNGs and comparison thresholds are unchanged.

To reproduce the capture, build the Core validation tool in a separate checkout
of the recorded parent commit, then invoke its DLL for every original sample:

```bash
dotnet /path/to/parent/tools/ExcelRenderer.Core.Validation/bin/Release/net10.0/ExcelRenderer.Core.Validation.dll \
  normalize /path/to/parent/tests/ExcelRenderer.Tests/SampleInputs/01-japanese.xlsx \
  /path/to/capture/01-japanese.xlsx
```

Repeat for each file listed in the manifest. ZIP container metadata can vary
between captures; compare decompressed entries when checking reproduction.
Do not regenerate these fixtures with the replacement normalizer or add the
removed library to the active build/test graph. Changes to the source samples
require explicit review of the corresponding captured input and golden images.
