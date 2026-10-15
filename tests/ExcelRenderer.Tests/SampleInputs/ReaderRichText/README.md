# Synthetic partial-format specimen

`representative.xlsx` contains eight rows of synthetic text with red bold, 16pt italic,
and blue underlined runs. No customer workbook is used.

Regenerate with:

```sh
dotnet run --project tools/ExcelRenderer.Performance -c Release -- reader-fixtures /tmp/reader-fixtures
cp /tmp/reader-fixtures/representative.xlsx tests/ExcelRenderer.Tests/SampleInputs/ReaderRichText/
```

`RichTextTests.SyntheticReaderFixtureReachesFinalizedPageRuns` checks plain text and
finalized page runs. `PdfPngAndSerializedSvgUseRunSizesColorsAndBaseline` checks
PDF text operators, sizes, shared baseline and offsets, colored PNG pixels, and
rasterized serialized SVG against PNG with the fixed test font.

These are library regression specimens, not Excel-generated reference outputs.
Excel application parity has not been verified.
