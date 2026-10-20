# PostScript image comparison performance

The test-only comparator in `PostScriptGhostscriptTests` now transfers the RGB
difference image in one `SKBitmap.Pixels` assignment instead of drawing each
pixel with `SetPixel`. It caches bitmap dimensions and stops interior/nearby
searches once their result is determined. The geometry/color checks still run in
both directions with the same radius, tolerances, tile sizes and failure budgets.

Early exit preserves the metrics: interior detection is a conjunction, and
geometry/color matching accumulates booleans with OR. Further candidates cannot
reverse a determined result. Diff pixels still contain opaque absolute RGB
channel differences. Four additional tests cover BGRA/RGBA inputs with premultiplied
and unpremultiplied alpha, including partially transparent pixels and an odd width.

## Measurement protocol

The benchmark invokes the actual private `Compare` method from saved Release
test assemblies. It uses one saved set of 1500 × 1667 PNG inputs for all runs:
the normal 300 dpi fixture and its missing-glyph, displaced-text and missing-line
mutations. It measures comparison, diff construction, PNG encoding and file writing.
Input decoding and result/diff hashing are outside the timer. Each process performs
three 32 × 32 warm-ups and then three measured rounds per case, with garbage
collection before each round. Builds, tests and rendering do not overlap measurements.

The baseline assembly was built at `d5fa038`; its complete comparator source is
byte-identical to the source at main `f73676c`, which became the branch parent
when PR #102 merged during this work. The optimized assembly is built on `f73676c`
with this change. The saved input PNG bytes are reused, without rerendering between
versions. No workbook conversion runs inside the benchmark.

The environment is Linux x64, Debian 13, .NET 10.0.12, SDK 10.0.401, with four
reported processors. Runs are sequential on a shared host, rather than interleaved
or CPU-pinned. Timings describe this run; the raw ranges should be considered when
comparing results. This does not measure Ghostscript rendering or CLI process startup.

## Results and verification

Per-case medians of three samples, including diff PNG encoding/writing:

| Case | Before | After | Observed speedup |
| --- | ---: | ---: | ---: |
| Normal drawing | 45.723 s | 1.498 s | 30.52× |
| Missing glyph | 48.766 s | 0.908 s | 53.72× |
| Displaced text | 30.410 s | 1.065 s | 28.56× |
| Missing line | 33.393 s | 3.874 s | 8.62× |

Individual times range from 27.931–78.076 s before and 0.894–11.096 s after.
The managed allocation total during each comparison falls from approximately
500 MB to 30 MB (about 94% less); these are cumulative allocations, not peak RSS.
The bulk diff buffer adds one managed array of about 10 MB for this fixture,
while eliminating millions of per-pixel native drawing wrappers.

All 12 optimized samples exactly match the baseline's complete contour, color
and region metrics, including compared/unmatched counts, maximum unmatched pixels
per tile and failed-tile arrays. The decoded diff pixel hashes and encoded PNG
hashes also match in every case. The normal drawing passes; all three mutations
fail. Managed Skia and native dependency hashes are unchanged.
The full regression run also regenerates all five input PNGs byte-for-byte and
produces the same four comparison results. Its finalized-command test, including
Ghostscript rendering and all mutations, completes in 12.605 s.

[results.json](results.json) retains both environments, all 24 raw samples,
warm-up counts, per-case summaries and DLL/native/input/source/harness hashes.
The five exact input PNGs are retained in [fixtures](fixtures); their Ghostscript
generator version is 10.05.1.
The runner's reference validation was also checked with deliberately altered
metrics, pixel hashes, input hashes and harness hashes; each was rejected.

Final verification:

- Full solution Release Rebuild: **0 warnings, 0 errors**.
- Full solution tests with fixed fonts and required Ghostscript: **2,656 passed,
  0 failed, 0 skipped** (Core 344, Formulas 833, Mapping 172, OpenXml 213,
  main 976, Tool 118).
- Source namespace check passed: 685 declarations and two required compiler shims.
- Benchmark reference validation passed for all four cases and three rounds.

The full suite includes many font-loading, conversion and CLI process tests;
the comparison speedups above describe the measured comparator, not total suite
runtime. [test-summary.json](test-summary.json) retains per-project counters/times
and the five selected test results; [full-tests.log](full-tests.log) and
[release-rebuild.log](release-rebuild.log) retain the verification output.

Commands used for the final solution checks:

```bash
dotnet build ExcelRenderer.slnx -c Release -t:Rebuild --no-restore --verbosity:minimal -m:1
source scripts/test-fonts.sh
EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1 dotnet test ExcelRenderer.slnx \
  -c Release --no-build --no-restore -m:1 \
  --logger 'trx;LogFilePrefix=image-comparison' \
  --results-directory artifacts/postscript-image-comparison/verified-tests
python3 scripts/check-source-namespaces.py
```

## Reproduction

Build the unchanged and optimized test projects in separate checkouts using
Release, and save each complete `tests/ExcelRenderer.Tests/bin/Release/net10.0`
directory outside active build output. Check free space first. Reuse the PNGs in
`docs/validation/postscript-image-comparison/fixtures` for exact reproduction.
To generate a new corpus, run the existing test once with fixed fonts and required
Ghostscript, then reuse those bytes for both versions:

```bash
source scripts/test-fonts.sh
EXCELRENDERER_REQUIRE_GHOSTSCRIPT=1 dotnet test tests/ExcelRenderer.Tests/ExcelRenderer.Tests.csproj \
  -c Release --no-build --filter FullyQualifiedName~Finalized_commands_compare_with_png
```

For a new corpus, copy `drawing-png.png`, `drawing-ps.png`, `missing-glyph.png`,
`displaced.png` and `missing-line.png` from `SampleOutputs/PostScript` into one
fixture directory. The following uses the retained corpus and runs the checked-in
harness against each saved runtime, sequentially:

```bash
python3 scripts/benchmark-postscript-comparison.py \
  --runtime artifacts/postscript-image-comparison/before-runtime \
  --fixtures docs/validation/postscript-image-comparison/fixtures \
  --output artifacts/postscript-image-comparison/before --repetitions 3
python3 scripts/benchmark-postscript-comparison.py \
  --runtime artifacts/postscript-image-comparison/after-runtime \
  --fixtures docs/validation/postscript-image-comparison/fixtures \
  --output artifacts/postscript-image-comparison/after --repetitions 3 \
  --reference artifacts/postscript-image-comparison/before
```

Each output directory must be new. The harness builds an isolated console runner,
uses the saved test dependency manifest to resolve native Skia, and records raw
samples, managed allocation totals, runtime/DLL/native/input hashes and PNG/diff
pixel hashes. `--reference` requires exactly equal comparison metrics and decoded
diff pixels for every sample. Keep the saved runtimes and fixtures unchanged until
both runs finish.
