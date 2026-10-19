# Conditional formatting: Phase 1

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
occurs. Cancellation is propagated.

Enable `DiagnosticOptions.DetectUnsupportedFeatures` for source-rule and differential
diagnostics, using existing output-selection filters. Evaluation failures are deferred
cell-read diagnostics with one representative cell and an occurrence count per rule
and code. Existing suppression, aggregation, strict-mode and preflight behavior apply.

| Code | Meaning |
| --- | --- |
| `ConditionalFormattingDisabled` | Intentionally disabled; informational |
| `UnsupportedConditionalFormatting` | Rule type outside Phase 1 |
| `ConditionalFormattingExtensionUnsupported` | x14/extension rule or extension-linked legacy rule; not applied twice |
| `ConditionalFormattingStyleUnsupported` | Unsupported differential items; supported items are applied and unsupported winning items block lower replacements |
| `ConditionalFormattingPriorityInvalid` | Missing, nonpositive or duplicate priority; uncertain affected appearance is retained as ordinary |
| `ConditionalFormattingDifferentialInvalid` | Unresolved saved dxfId |
| `ConditionalFormattingFormulaUnsupported` | Unsupported or invalid condition syntax/operator |
| `ConditionalFormattingFunctionUnsupported` | Engine cannot evaluate a reached function |
| `ConditionalFormattingCachedValueMissing` | A condition needs an absent formula cache |
| `ConditionalFormattingLimitExceeded` | Reader/candidate/engine budget exceeded; Reader input-limit failures follow its existing InvalidDataException policy |
| `ConditionalFormattingEvaluationFailed` | Other dependency or evaluation failure |

An unsupported condition encountered before a lower-priority match makes the result
uncertain; that cell retains its complete ordinary style. Unsupported style properties
are reported even when other properties are usable. Unsupported fills/underline/edges
reserve their priority and leave their ordinary property intact.

Not implemented: top10, aboveAverage, duplicate/unique values, timePeriod, color
scales, data bars, icon sets, extension rendering, conditional numFmt, patterned or
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
