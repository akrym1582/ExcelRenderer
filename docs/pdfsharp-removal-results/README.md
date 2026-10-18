# Recorded removal evidence

These compact files accompany [the validation report](../pdfsharp-removal-validation.md).
Frozen original PDFs, source projections and metric samples live in
`tests/Shared/PdfRemovalBaseline`; their manifest hashes are checked before comparisons.

- `A-C.json` / `A-C-differences.json`: final user-facing comparison, retaining the
  first-stage 51 reviewed drawing differences with no new broad exception.
- `B-C.json` / `B-C-differences.json`: measurement/writer comparison without exceptions.
- `core-comparison.json`: independent text, glyph origins, outlines and regional
  144/300dpi evidence for three frozen Core fixtures (four pages).
- `core-antialiasing-review.png`: old / new / difference crops for the eleven
  exactly bounded Core renderer antialiasing differences. These preview crops
  are scaled for reading; comparison metrics use original pixels without resizing.
- `core-raster.json`: 17 unchanged historical Core golden pages and current full
  reference comparisons; one exact-pixel print-scaling review is explained in the report.
- `api-diff.json` / `core-api-diff.json`: assembly metadata signatures, without
  loading the old product runtime.
- `font-formats.json`: independently extracted Unicode from the six format probes.

Full PDFs, native-prefix captures, per-region PNGs and interactive HTML reports
are reproducible under `artifacts/pdfsharp-removal-current` and uploaded by CI.
Large full-font comparison intermediates are deliberately not committed.

- `benchmark-final.json` / `benchmark-final-samples.csv`: all 21 final B/C
  conditions, with 910 measured process records; medians, maxima, phases, RSS,
  managed allocations, output sizes and page counts. The 1.25 median gate passes;
  two maximum-time ratios exceed it and remain recorded.
- `benchmark-final-{conditions,assemblies,inputs}.json`: alternating stage order,
  rounds/warm-ups, unchanged baseline/final DLL identities and exact XLSX hashes.
- `benchmark-initial*` / `benchmark-inputs.json`: all 1,365 initial A/B/C records,
  including both failed initial targets. `benchmark-diagnostic.json` records the
  measurement-only CPU/GC investigation; it is separate from formal results.
- `benchmark-final.svg` / `.png`: all final median time/RSS ratios, with the 1.25
  target. Each ratio compares B and C under the same workload/parallelism.
- `retention.json` / `retention-{100,200}.csv`: resource creation/disposal totals
  and every post-GC observation from final same-process runs. The 200-run plots
  show native high-water behavior as well as stable late managed memory.
- `font-sharing.json`: installed CLI multi-page sizes and font-object counts.
- `package-inventory.json` / `consumer-graphs.json`: final four-package identities
  and the nine isolated consumers' 18 restore/runtime graphs.
- `loaded-assemblies-linux.json`: assemblies observed during real conversions.
- `environment.json`: final implementation commit, source and assembly hashes;
  `environment-initial.json` retains the initial snapshot for comparison.
- `verification.json`: final counts and platform status. Windows remains blocked
  pending fresh authorization after automatic review rejected remote publication;
  Edge/Adobe manual checks remain unperformed with procedures in the report.
