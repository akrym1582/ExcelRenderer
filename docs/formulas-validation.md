# Formula integration validation

Status: implementation and verification in progress; this record is updated before
final commit. Base `origin/main`: `d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4`,
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

Final command results, counts and artifact paths will be entered here after the
last source changes settle. Prior intermediate green runs do not certify later edits.

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
