# Conditional formatting: Phases 1 and 2

Implemented in the main ExcelRenderer library and its PDF/PNG/SVG conversion paths,
including XLSX files produced by Mapping. **ExcelRenderer.Core does not evaluate or
apply conditional formatting.** It continues to render ordinary saved cell styles.
No new product project, package or Core calculation dependency is introduced.

## API and value policy

`ExcelRenderer.Excel.ExcelReadOptions.ApplyConditionalFormatting` defaults to `true`.
Pass `new ExcelReadOptions { ApplyConditionalFormatting = false }` to `ExcelReader.Read`,
or set `RenderRequest.ReadOptions` for conversion. Existing high-level options that
carry a RenderRequest use the same setting. There is no Core option or new CLI flag.
Markdown remains plain text; no color syntax is added.

In the default `FormulaDisplayMode.CachedValue`, conditions are evaluated against
saved **typed** values. Source formulas are not recalculated. A formula without a
saved result is unknown, unlike a saved empty string, numeric zero or missing cell.
In `Recalculate`, conditions and displayed formulas share one workbook snapshot and
one calculation session. Dependencies can be on unselected sheets or outside the
print area. Existing explicit-recalculation failures still throw for display targets.
No calculation snapshot is made for a cached read with no rules, or for a cached read with disabled
conditional formatting. Explicit display recalculation still creates its normal session. A geometry-only page-selection read disables conditional
evaluation; the real read evaluates only selected candidates.

## Supported conditions and styles

| Saved rule | Supported conditions |
| --- | --- |
| `cellIs` | equal, notEqual, greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual, between, notBetween; formula operands use the existing engine |
| `expression` | Existing engine operators, functions, A1 references and supported defined names; Boolean TRUE or a nonzero numeric result matches |
| `containsText`, `notContainsText`, `beginsWith`, `endsWith` | Case-insensitive text comparisons; numeric values use invariant G15 and Boolean values use TRUE/FALSE |
| `containsBlanks`, `notContainsBlanks` | True missing cells, saved empty strings and strings containing only ASCII spaces are blank; zero, FALSE and tabs are not blank |
| `containsErrors`, `notContainsErrors` | Typed Excel errors; the string `#N/A` is ordinary text |
| `duplicateValues`, `uniqueValues` | Typed value frequency over the entire sqref union; the first duplicate matches too |
| `top10` | Top/bottom N items or N percent, boundary ties included |
| `aboveAverage` | Above/below mean, inclusive comparisons, and mean ± 0–3 population standard deviations |
| `colorScale` | Standard two/three-point scales; min/max/num/percent/percentile/formula thresholds, RGB solid backgrounds |

A legitimate error returned by a condition is a nonmatch. Parser failures,
unsupported functions, missing saved values and resource failures are diagnostics,
not FALSE results. Text conditions use the saved `text` attribute. An empty search
string matches any non-error text for contains/begins/ends. Text matching does not
add Excel wildcard syntax; these semantics require desktop Excel comparison before
claiming full Excel compatibility.

Supported differential properties are solid background fill, font color, bold,
italic, single underline, and left/top/right/bottom border edges. Source theme,
indexed and tint metadata uses the Reader's existing color resolution. Differential
backgrounds saved as bgColor (with automatic foreground or omitted patternType)
are retained as colors; explicit patternType none clears the fill. Explicit
false, no fill, no underline and empty border edges remain distinct from omission.
All other ordinary cell properties and unmentioned rich-text run properties survive.
Each rich-text run receives the same explicit font overrides as ordinary text.

Rules are ordered across the entire worksheet by ascending priority. Matching
higher priorities reserve individual font properties, the fill and each border edge;
lower priorities may supply other properties. Matching `stopIfTrue` stops even when
its differential is empty. The first saved sqref rectangle supplies the formula
anchor for **all** rectangles; relative/mixed references move through parsed syntax,
without reparsing for each cell. Stable source order breaks invalid-priority ties,
but affected cells retain ordinary formatting rather than claiming an exact result.

The effective style is interned before shared merged-border aggregation and layout.
A generic Core reader completion hook defaults to no work. Core does not interpret
rules, and ordinary style-index caches are never overwritten by conditional results.
The same finalized model feeds all three visual output formats.

## Population rules (Phase 2)

A population is the union of **all** saved sqref rectangles for that rule, including
hidden rows/columns and cells outside print areas, selected ranges and selected pages.
Overlapping/noncontiguous rectangles share one population; coordinates are counted
once. Different coordinates with the same value are separate observations. Repeated
print titles and merged display area never multiply a stored value. Appearance still
uses the existing finite candidate and merge-leader policies and never grows page geometry.

OpenXml's `WorkbookCalculationSession.EnumerateValueCells` streams stored value/formula
coordinates through its existing sparse range index. It preserves formulas without
caches and cells with unsupported calculation metadata: `ReadValue` reports their
failure instead of silently removing them from a population. True blanks (including
explicit empty cells and absent coordinates) are counted by checked `long` union area
minus indexed cells, without materializing missing coordinates. A saved empty string
is retained as typed text. Saved-value mode never recalculates source formulas;
Recalculate reaches population formulas, including selected-out cells, on the same
session used by display and Phase 1. Unrelated workbook formulas are not eagerly evaluated.

Populations are lazy: a selected target must reach the rule before its values are read.
Cache keys use disjoint normalized ranges within one worksheet/session/value policy;
no cache survives a conversion. Matching ranges share values, frequency counts,
numeric values, extrema and mean/variance. Numeric sorting is shared at most once,
and only for top/bottom ranks or percentiles. Mean-only and min/max scales do not sort.
A missing cache, unsupported evaluation or budget failure invalidates the complete
population, preserving the existing conservative priority behavior.

### Duplicate equality profile

The following is an explicit typed profile with hand-authored expectations, **not a
desktop-Excel compatibility claim**. Formatting and font differences do not change keys.

| Pair | Equal? |
| --- | --- |
| Numeric 1 and numeric 1 in different number formats | Yes |
| Numeric 1 and text "1" | No |
| TRUE and numeric 1 | No |
| Numeric date serial and the same number | Yes; ISO dates use the snapshot date system |
| "FILE" and "file" | Yes, `StringComparer.OrdinalIgnoreCase`, independent of current culture |
| "file" and Turkish dotless "fıle" | No |
| Typed #N/A and typed #N/A | Yes |
| Typed #N/A and text "#N/A" | No |
| Explicit blank and missing coordinate | Yes |
| True blank and formula/saved text "" | No |
| Empty text and a single ASCII space | No; spaces are not trimmed |

A frequency ≥2 matches `duplicateValues`; frequency 1 matches `uniqueValues`.
Numeric equality uses exact finite double equality, including equal signed zeros.
Unverified linguistic/Excel coercion cases remain listed in the Phase 2 validation report.

### Ranks and averages

`top10` defaults to rank 10, bottom false and percent false. Explicit ranks must be
integers 1–1000 (1–100 for percent). Invalid attributes are diagnosed, not clamped.
Only typed numbers (including numeric formula results and date serials) participate;
text, Boolean, true blank and typed Excel errors are excluded. Percent uses
`ceil(n * rank / 100)` observations. N greater than the population includes all numbers;
all ties at the selected boundary match. `[100,90,90,80]`, top 2 → first three cells.

`aboveAverage` defaults to above true, equal false, stdDev 0. Explicit stdDev is an
integer 0–3. Shifted Welford statistics use population variance (denominator n), with
no matching epsilon. The threshold is mean + Nσ for above, mean − Nσ for below;
equalAverage includes the threshold. Empty numeric populations match nothing; one
number/all equal numbers obey the same comparison, with σ=0. Arithmetic overflow or
nonfinite statistics are diagnostics. `[0,10,20]` → mean 10, σ=√(200/3), above selects 20,
inclusive above selects 10/20. This divisor/rounding profile requires Excel confirmation.

### Color scales

Source theme/indexed/tint colors are resolved by Reader, using the same path as dxf
colors. cfvo and color sequences correspond by index. Two or three points are required.

| Threshold | Computation |
| --- | --- |
| min / max | Numeric extrema |
| num | Finite saved number |
| percent | Linear interpolation of min/max; value between 0 and 100 |
| percentile | PERCENTILE.INC profile: h=(n−1)p, interpolate adjacent sorted observations |
| formula | Existing engine, evaluated once at the first saved sqref anchor; must return a number |

For `[0,1,2,100]`, percent 50 is 50 and percentile 50 is 1.5. Threshold formulas in
`cfvo/@val` retain Reader length limits and Mapping's existing formula rewriting.
They are not moved/re-evaluated for each painted cell. Non-numeric cells, including
real Excel errors, are not painted. Missing formula results remain failures.

A scale produces a solid background only, through the same priority pass as dxf.
Higher dxf fills (including explicit clear) block lower scales; higher scales block
lower dxf fills; font-only rules leave fill available. No per-cell XML is generated.
A true `stopIfTrue` or dxfId on a colorScale is diagnosed as an invalid combination.

RGB byte components are interpolated directly in sRGB (no gamma conversion), then
rounded once with `MidpointRounding.AwayFromZero`. Values outside the thresholds are
clamped. Black 0 / white 100 gives 25 → RGB(64,64,64), 50 → RGB(128,128,128).
Descending/nonfinite thresholds, wrong color/point counts, nonnumeric formula results,
and collapsed adjacent thresholds are indeterminate. Without an independent Excel
oracle, **all-equal min/max data and collapsed midpoint intervals retain ordinary fill
with a diagnostic**. They do not abort the conversion or invent a fallback color.
Excel interpolation/degeneracy equivalence has not been measured.

## Bounds, diagnostics and unsupported features

Rule application is clipped to existing finite selected, printed or used regions.
Whole-column references do not create a million rows or change page geometry.
An otherwise empty sheet only considers its existing A1 page. A range selected inside
a merge still uses its original leader. Hidden candidates and unselected sheets are
excluded from appearance evaluation;
dependency values remain accessible. Merge leaders own visible text and fill, and
shared Core code still owns border aggregation and suppression of subordinate cells.

Reader's `MaxStyleRecords` bounds differential records, worksheet rule count and
application-range count. `MaxCellTextCharacters` bounds saved condition text, and
existing part and formula-engine limits remain enforced. The main evaluator also
caps intersected candidate visits at 2,000,000 per sheet; exceeding this leaves that
sheet's ordinary appearance and reports `ConditionalFormattingLimitExceeded`. No partial candidate-budget application
occurs. Phase 2 separately limits aggregate stored-cell visits/retained values to
2,000,000 per worksheet, source rectangles to 100,000 per normalization, remaining rectangles after row/column
interval coalescing to 4,096, range inspections
to 1,000,000 per normalization, and normalized rectangles to 65,536 both per population
and cumulatively per worksheet. Frequency entries and numeric lists are bounded by the
retained observations (plus one implicit-blank key). Exceeded budgets never publish
partial statistics. Cancellation is checked during union construction, value/key
collection and statistics, and before/after sorting; it propagates as cancellation.

Enable `DiagnosticOptions.DetectUnsupportedFeatures` for source-rule and differential
diagnostics, using existing output-selection filters. Evaluation failures are deferred
cell-read diagnostics with one representative cell and an occurrence count per rule
and code. Existing suppression, aggregation, strict-mode and preflight behavior apply.

| Code | Meaning |
| --- | --- |
| `ConditionalFormattingDisabled` | Intentionally disabled; informational |
| `UnsupportedConditionalFormatting` | Rule type outside Phases 1–2 |
| `ConditionalFormattingExtensionUnsupported` | x14/extension rule or extension-linked legacy rule; not applied twice |
| `ConditionalFormattingStyleUnsupported` | Unsupported differential items; supported items are applied and unsupported winning items block lower replacements |
| `ConditionalFormattingPriorityInvalid` | Missing, nonpositive or duplicate priority; uncertain affected appearance is retained as ordinary |
| `ConditionalFormattingDifferentialInvalid` | Unresolved saved dxfId |
| `ConditionalFormattingFormulaUnsupported` | Unsupported or invalid condition syntax/operator |
| `ConditionalFormattingFunctionUnsupported` | Engine cannot evaluate a reached function |
| `ConditionalFormattingCachedValueMissing` | A condition needs an absent formula cache |
| `ConditionalFormattingLimitExceeded` | Reader/candidate/engine budget exceeded; Reader input-limit failures follow its existing InvalidDataException policy |
| `ConditionalFormattingEvaluationFailed` | Other dependency or evaluation failure |
| `ConditionalFormattingAttributesInvalid` | Invalid rank/Boolean/stdDev or forbidden scale attribute combination |
| `ConditionalFormattingThresholdInvalid` | Invalid, descending, collapsed or nonnumeric color/average threshold |
| `ConditionalFormattingPopulationInvalid` | Nonfinite mean or population variance |

An unsupported condition encountered before a lower-priority match makes the result
uncertain; that cell retains its complete ordinary style. Unsupported style properties
are reported even when other properties are usable. Unsupported fills/underline/edges
reserve their priority and leave their ordinary property intact.

Not implemented: timePeriod, data bars, icon sets, extension rendering, conditional
numFmt, patterned or
gradient fills, diagonal borders, strikeout, superscript/subscript or additional Excel
functions. External workbooks are never fetched.

## Mapping and source ownership

Reader owns immutable `ConditionalFormattingRuleInfo` / `DifferentialStyleInfo`,
ordered sqrefs, raw attributes/identifiers/formulas and color resolution. No
IWorkbookDataReader interface change is required. `WorkbookCalculationSession` in
OpenXml owns the shared dependency snapshot, arbitrary parsed-expression evaluation
and saved-value mode. `FormulaRewriter.Copy(ParsedFormula, ...)` in the BCL-only
Formulas project moves relative references without text substitution.

The main library alone owns `ConditionalFormattingSession` and `ConditionalStyle`.
Mapping reuses its existing XLSX structural rewriting; split clones receive unique
priorities in the original priority order, preserving stopIfTrue conflicts. Invalid
original priority lists are not silently repaired. Mapping has no rendering evaluator.
Population-dependent rules retain one expanded sqref union. The existing Mapping
RequiresPopulation/RewriteRule path and FormulaStores include cfvo formula val attributes.
Combinations that cannot preserve one application set still throw NotSupportedException;
they are never silently split into different rankings/color distributions. Existing
unrendered data bars, icon sets and extension metadata retain their save behavior.
Core still references only Reader, and Reader has no product-project references.

## Verification and independent expectations

Tests hand-specify expected cell attributes and value semantics, independently of
implementation output. Added coverage includes all cellIs operators, typed text/blank/
error conditions, mixed and noncontiguous references, cached/recalculated differences,
missing caches, priority/stop/composition/clearing, rich text, merged leaders, off-sheet
names, finite blank materialization, Mapping expansion to PDF/PNG/SVG and Core isolation.
Reader tests cover immutable differentials, explicit false, indexed colors, extension
metadata, invalid dxfId, metadata bounds and parsed-reference movement.

Desktop Excel is unavailable in this Linux environment. **No Excel DisplayFormat or
Excel-exported PDF comparison has been performed.** Text/blank coercion and the first
sqref anchor are explicit hand-authored expectations, not desktop-observed guarantees.
Existing frozen references and hashes are unchanged. Windows execution is not claimed.
See [the validation report](validation/conditional-formatting/README.md) for actual
commands, measurements, artifacts and remaining verification limits.


Phase 2 tests additionally fix typed frequencies, ranking ties and percent rounding,
population σ, percent versus percentile, dynamic/static fill priority, sparse whole-sheet
blanks, overlaps, selection/hidden/formula policies, budgets and Mapping save/reload to
all visual formats. See [Phase 2 evidence](validation/conditional-formatting-phase2/README.md).
