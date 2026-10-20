# Independent Core tests

This project references only Core and its Reader dependency. It compiles no files
from `tests/Shared` and reads no assets from other test projects. The Core CI job
uses a sparse checkout containing only these projects and the supplied Japanese
font. Python, Poppler, a reference renderer and system font setup are unnecessary.

`TestWorkbook` creates small XLSX packages directly with the Open XML SDK.
`TestImages` creates deterministic images, including a valid-header PNG with
corrupt pixels. `Fonts` owns the second font and tiny format fixtures. The nine
previously captured workbooks in `Baselines/Inputs` remain immutable local smoke
inputs; their provenance and historical raster files are preserved.

## PDF verification

PdfPig opens saved PDFs with strict parsing. Tests use its letters, paths, image
pixels, dictionaries and annotations directly. `PdfContentProbe` only converts
coordinates from bottom-left to top-left; it does not interpret PDF operators or
use the product parser. Checks cover text/baselines/underlines, wrapping/shrinking,
variation selectors, embedding/ToUnicode/ActualText, page sizes, fills/colors,
solid/dashed/dotted/double borders, image placement/pixels and finalizer metadata.
The finalizer unit tests additionally use the product parser to check internal
object preservation, followed by independent PdfPig assertions.

These are semantic and numeric PDF checks, not raster similarity measurements.
The historical PNGs are retained as historical evidence and are not regenerated
or accepted automatically by this suite.

## Run

From the repository root, with .NET 10:

```sh
dotnet restore tests/ExcelRenderer.Core.Tests --locked-mode
dotnet build tests/ExcelRenderer.Core.Tests -t:Rebuild -c Release --no-restore
dotnet test tests/ExcelRenderer.Core.Tests -c Release --no-build --filter "Category!=Performance" -p:CollectCoverage=true
dotnet test tests/ExcelRenderer.Core.Tests -c Release --no-build --filter "Category=Performance"
```

The coverage run fails below the previous Core line/branch percentages
(82.73% / 70.08%, measured at `513ddd4d424d9afa6e92ed154abdc631841ba302`
with Coverlet 6.0.4, Release, .NET 10 on Linux). No production files or attributes
are excluded. JSON/Cobertura results are under `TestResults/Core/Coverage`.
Current validation: 194 correctness tests, 82.88% lines / 70.27% branches.

## Performance measurements

The separate performance category covers text and repeated-image XLSX inputs at
100, 1,000, 5,000 and 10,000 rows. Each case generates its input once, performs one
reported warm-up and three sequential measured conversions, then records the
raw samples and median. CI runs this category after coverage restores the clean
assembly. Run it without overlapping builds/tests/rendering or coverage/profiling.

The timed and allocated-byte intervals contain only `ExcelConverter.ConvertAsync`.
Input generation, stream setup, PdfPig verification, assertions, hashing and JSON
writing occur outside both intervals. The observer is enabled consistently to
check two page builds per page, one save and matching native font disposal.
PdfPig checks every row label in page order and image count after each conversion.
Manual page breaks specify 50 rows per page independently of the renderer.

`SampleOutputs/Performance` contains each XLSX and a JSON report with runtime/OS,
architecture, CPU count, GC mode, input/font/Core DLL hashes, warm-up and trials.
Allocated bytes are process-wide; test parallelism is disabled. Managed heap,
working set and handle readings are snapshots outside the timed interval, not
peak-memory or leak measurements. No forced GC or machine-dependent time limit
is used. Use the raw observations to compare runs on the same machine; these tests
do not claim a before/after speedup or a fixed performance regression percentage.
