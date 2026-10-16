# Formula integration validation

Local verification completed against implementation commit
`56d7eb575c90fb0575e86174055ceb02a6d42b48`; subsequent changes are documentation
and recorded measurements. Base `origin/main`: `d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4`,
fetched 2026-10-16. Branch: `feature/formulas-integration-main-20261016`.
.NET SDK 10.0.401, runtime 10.0.12, Debian GNU/Linux 13, x64.

The saved task contract and checklist are in
[plans/formulas-integration-task.md](plans/formulas-integration-task.md).
The newer integration request supersedes the original standalone-only, no-reference-
rewriting and nonpackable restrictions. The core remains independent and BCL-only.

## Evidence policy

Fixed expected results come from the supplied specification, hand calculation or
explicit v1 profile; fixture JSON labels `expectedSource` and its basis. No fixture
claims an Excel desktop observation. Excel desktop comparison was not performed.
Binary rounding boundaries, Unicode collation, extreme G15 number conversion and
broader coercions remain unverified against Excel.

The two dependency scripts retain exact allowed project/package boundaries.
OpenXml's former zero-project-reference assertion now permits exactly Formulas;
all existing no-rendering/no-ClosedXML ownership assertions remain. No mutation
assertions or image thresholds are relaxed.

## Distribution

Formulas is packable at the repository's existing development version 1.8.0.
OpenXml's NuGet dependency carries it to standalone Mapping/main consumers; the
.NET tool includes its assembly once. Core remains nonpackable and bundled in the
main package. This is necessary to avoid an omitted runtime dependency. It is a
local packaging validation, not a new published release.

`validate-formulas-packages.py` inspects nuspec/DLL inventories and restores fresh
consumers outside the repository with no project references. It exercises engine,
OpenXml cache roundtrip, Mapping, main Markdown, bundled Core PDF and installed CLI.
The source fixture uses the Open XML SDK, never ClosedXML.

## Final checks

| Check | Result |
| --- | --- |
| `dotnet restore ExcelRenderer.slnx --locked-mode` | Passed |
| `dotnet build ExcelRenderer.slnx -t:Rebuild -c Release --no-restore --verbosity:minimal` | Passed, 0 warnings, 0 errors |
| `dotnet test ExcelRenderer.slnx -c Release --no-build` | 1,199 passed, 0 failed, 0 skipped |
| Formulas / OpenXml / Mapping | 110 / 171 / 190 passed |
| Core / main / CLI | 82 / 555 / 91 passed |
| Both dependency scripts | Passed |
| `check-output-mutations.py` | 19/19 detected; all source hashes restored |
| `check-core-shared-mutations.py` | 2/2 detected; restored tests passed |
| `check-core-mutations.py` | 4/4 detected |
| `validate-core.py --visual-only` | 17 raster pages, maximum baseline MAE 0.0 (unchanged threshold 0.25) |
| `dotnet pack ExcelRenderer.slnx -c Release --no-build --no-restore -p:BuildProjectReferences=false` | Six product/tool packages created locally |
| `validate-formulas-packages.py` in a fresh directory | Five isolated consumer scenarios passed, including main and bundled Core |
| Invoice sample, 0/1/3/4 lines | Saved totals 0/11/66/110; PDF, PNG, SVG, Markdown generated for each |

Logs, test-count JSON, restored-source mutation hashes, package inventory and visual
report are retained under [validation/formulas/](validation/formulas/).
The visual report's historical `source_commit` identifies golden-image provenance;
its `validation_commit` identifies this implementation. No baseline images changed.

The last 3 OpenXml tests specifically preserve diagnostic path/span/feature and
empty-string/false/zero cache types. The fixture suite also runs 2,000 deterministic
malformed inputs. Exact page selection tests exclude cycles and unknown functions
on unrendered pages, and measure actual calculated repeated-title values.

The two Core visual helper projects are outside the solution. Their lockfiles were
also updated for the new project dependency and then successfully restored in locked
mode by the visual script. Existing external package lock entries were not changed.
An initial locked restore caught these omitted helper lock updates; they were fixed,
not bypassed. Parallel development initially raced Rebuild output deletion, and an
intermediate CLI Mapping/Markdown test exposed a print-area selection mismatch;
the final ordinary parallel solution Rebuild and full suite above both passed.
The first no-build pack required the repository's established
`BuildProjectReferences=false` switch. An isolated tool check also required setting
`DOTNET_ROOT` from the installed runtime, now handled in the reproducible script.

Draft PR: https://github.com/akrym1582/ExcelRenderer/pull/80 .
GitHub Actions run [38900325144](https://github.com/akrym1582/ExcelRenderer/actions/runs/38900325144)
passed all five jobs at head `81a211be9bc4047d29cf9084b11e8d092344e39a`:
full integration/mutation/pack-consumer validation, Core on Windows/Linux and
isolated Formulas on Windows/Linux. The source is unchanged from the local
implementation SHA above. This subsequent report/package-table update changes
documentation only. The final delivery head is checked again; its authoritative
status is available in [PR checks](https://github.com/akrym1582/ExcelRenderer/pull/80/checks).
The PR remains a draft; nothing has been merged, marked ready or released.

## Performance

See [formulas-performance.md](formulas-performance.md) and its raw JSONL records.
Every benchmark checks actual results, including every saved formula cache. There
is no environment-specific hard timing threshold. Large-workbook recalculation
currently uses the Open XML worksheet DOM when persisting caches; working-set
measurements include that cost.

## Known API boundaries

- Formula sessions require immutable sources and are not intended for concurrent mutation.
- Unsupported OOXML formula kinds, relative/formula names and calculation metadata
  are rejected when reached. Adapter failures are currently wrapped as
  `DataSourceFailure` with cell/feature/`UnsupportedSyntax` in the message.
- Mapping default behavior still rejects formulas. Explicit recalculation permits
  supported cell formulas, not calculated tables, external references or array constructs.
- Completely deleted references become `#REF!`. For a zero-line SUM, a range
  containing a surviving heading cell has an empty numeric population and yields 0.
- Staging protects output until final destination writes; I/O failure during the
  final stream copy can still leave partial output, as documented by the API.
