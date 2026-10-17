# Formula calculation

[日本語](formulas.ja.md) · [Syntax and function reference](formulas-reference.md) · [validation](formulas-validation.md) · [performance](formulas-performance.md) · [review fixes](formulas-review-fixes-validation.md)

ExcelRenderer has an opt-in, limited formula calculator. `CachedValue` remains the
reader default and preserves saved values, including stale results. `Recalculate`
uses the independent, BCL-only `ExcelRenderer.Formulas` engine through OpenXml
adapters. It never calls Excel, ClosedXML or an external calculator. This is not a
claim of full Excel compatibility.

```csharp
var document = new ExcelRenderer.Excel.ExcelReader().Read(input,
    new ExcelRenderer.Excel.ExcelReadOptions
    {
        FormulaDisplayMode = ExcelRenderer.Excel.FormulaDisplayMode.Recalculate,
    });
```

Core offers the corresponding `ExcelRenderer.Core.FormulaDisplayMode.Recalculate`
in `PdfExportOptions.ReadOptions`. Main PDF, PNG, SVG and Markdown APIs use their
existing `ReadOptions`. CLI rendering commands accept `--formula-mode recalculate`;
`--formula-mode cached` remains the default. Historical `Legacy` mode is rejected.

## Mapping and saving

```csharp
ExcelRenderer.Mapping.ExcelTemplateMapper.Map(template, output, data,
    new ExcelRenderer.Mapping.MappingOptions
    {
        FormulaCalculationMode = ExcelRenderer.OpenXml.FormulaCalculationMode.Recalculate,
    });
```

The processing order is **input updates and row expansion → reference rewriting →
calculation → number formatting → rendering**. Default Mapping still rejects
value-producing formulas, preserving the existing contract. Explicit recalculation
enables supported cell formulas. Values beginning with `=` remain literal text.

For an existing workbook, `WorkbookRecalculator.Recalculate(input, output)` in
`ExcelRenderer.OpenXml` recalculates all formula cells. An optional collection of
`FormulaCellAddress` targets selects only those cells and reached dependencies.
A single session reuses dependency results. The source snapshot is immutable;
after changing inputs or expanding rows, create a new session.

The staged XLSX writer stores numbers, text, booleans and Excel errors with their
correct OOXML cache types. **All unevaluated formula caches are removed**, including
unsupported formulas outside an explicit target set. The workbook requests full
calculation on load and its obsolete calculation chain is removed. These flags do
not mean the engine evaluated unsupported formulas. Reloading with CachedValue
reports missing caches rather than silently displaying stale results. Recalculation
failure or cancellation before the final copy leaves the destination untouched;
a failure during destination I/O can leave a partial stream.

Shared-formula followers are restored through parsed references. Copying moves
only relative row/column coordinates; `$` flags prevent copy displacement.
Structural row movement moves referenced positions regardless of `$` flags.
Strings containing `A1` are text and are never rewritten as references. Formula
names retain their name identity; their fixed referenced regions move with rows.

## Standalone engine

```csharp
using ExcelRenderer.Formulas;

var builder = new InMemoryFormulaWorkbook.Builder().AddSheet("Invoice");
builder.SetValue(new FormulaCellAddress("Invoice", 1, 1), FormulaValue.FromNumber(3));
builder.SetValue(new FormulaCellAddress("Invoice", 1, 2), FormulaValue.FromNumber(125));
builder.SetFormula(new FormulaCellAddress("Invoice", 1, 3), "ROUND(A1*B1*1.1,0)");
var source = builder.Build();
var session = new FormulaEngine().CreateSession(source);
var result = session.EvaluateCell(new FormulaCellAddress("Invoice", 1, 3));
// result.Success, result.Value.Number == 413 (specified ties away from zero).
```

`FormulaParser.Parse` exposes immutable nodes and UTF-16 source spans. Direct
`Evaluate` and `EvaluateCell` create independent sessions; use `CreateSession`
when calculating many cells from the same immutable snapshot. No result cache is
shared between workbooks or sessions. A1 means coordinate A1, not an offset from
the origin. The origin supplies the current sheet and diagnostic position.

The [formula support reference](https://github.com/akrym1582/ExcelRenderer/blob/main/docs/formulas-reference.md)
lists all 40 functions with argument counts and examples, syntax, reference rules,
coercion, errors and resource limits. Operators are unary
signs, arithmetic, left-associative power, percent, concatenation and comparisons.
The parser accepts English function names, comma arguments, quoted strings and
sheet names, A1 references with absolute/mixed flags, rectangular/whole-row/whole-column
ranges and fixed cell/rectangle names. Built-in sparse sources enumerate stored coordinates within resource limits; external dense sources retain bounded scanning.

## Explicit compatibility profile

- Unary signs bind before powers: `-2^2=4`; powers associate left: `2^3^2=64`.
- Numeric coercion accepts invariant signed decimal/exponent strings after ASCII
  whitespace trimming, not currency, grouped numbers, dates or percent strings.
- Referenced text/booleans are ignored by numeric aggregates; direct numeric text
  and booleans are converted. Blank, empty text and missing arguments differ.
- Text comparison is ordinal, ignoring case. Different types order as
  Number < Text < Boolean. Concatenation formats numbers with invariant G15.
- ROUND ties away from zero; ROUNDUP moves away from zero, ROUNDDOWN toward zero.
  Only rounding uses the input double's shortest invariant round-trip decimal
  representation (`ToString("R", InvariantCulture)`). Its integer coefficient and
  exponent are rounded with integer quotient/remainder arithmetic, then converted
  back to double. `num_digits` truncates toward zero. Thus `ROUNDDOWN(0.29,2)=0.29`,
  `ROUNDUP(0.07,2)=0.07`, and `ROUND(1.005,2)=1.01`. Adjacent doubles remain distinct;
  computed `0.30000000000000004` is not normalized to `0.3`. Nonfinite rounding
  results produce `#NUM!`. Existing signed-zero behavior is preserved, including
  positive zero from nonzero inputs for places below -308. This profile does not emulate all Excel precision corrections.
- IF and IFERROR evaluate only the selected value branch, but every reached AST is
  statically validated. `IF(TRUE,1,UNKNOWN())` fails with UnsupportedFunction.
  IFERROR never hides an unsupported feature, cycle, limit or cancellation.
- AND/OR do not short-circuit away later errors. COUNT ignores ordinary Excel errors;
  COUNTA counts them. Neither suppresses engine failures.

Unsupported features include arrays/dynamic arrays/spills, external/3D/structured
references, implicit intersection, reference unions/intersections, R1C1, formula or
relative names, approximate lookups, INDIRECT/OFFSET, LET/LAMBDA,
volatile functions, UDF/VBA and iterative calculation. Unsupported reached cells
fail with diagnostics instead of falling back to saved caches. A range cannot be
returned as a scalar by silently choosing its top-left cell.

Default limits are 8,192 formula UTF-16 characters, 16,384 AST nodes, syntax depth
128, dependency depth 256, 100,000 formula cells, 1,000,000 cell visits and 2,000,000
evaluation steps. `FormulaEvaluationOptions` exposes explicit limits. Cancellation
uses `OperationCanceledException`. Cycles include their dependency path.

## Text resource limits

```csharp
var limits = new FormulaEvaluationOptions
{
    MaxStringLength = 32767,
    MaxCachedStringCharacters = 16777216L,
};
var bounded = new FormulaEngine().CreateSession(source, limits);
// WorkbookRecalculator.Recalculate(input, output, options: limits);
// MappingOptions.FormulaEvaluationOptions and Reader options accept these limits too.
```

Both defaults are library resource limits measured in **UTF-16 code units**, not
Unicode scalar values or a guarantee of Excel's Unicode length rules. A surrogate
pair uses two units. Negative limits throw `ArgumentOutOfRangeException` when a
session is created. Zero permits only empty Text for the one-value limit, or only
empty cached Text for the cache budget. Options are copied into the
session, so later changes do not affect it. `FormulaValue.FromText` remains a
storage factory without session limits.

`MaxStringLength` applies to evaluated literals, source cell Text, selected IF
results and temporary concatenations. Concatenation checks the combined lengths
using `long` **before** allocating the resulting string. Unselected IF branches
are not evaluated; existing static function validation still applies.
`MaxCachedStringCharacters` budgets the sum of Text lengths retained in the
session's cell-value cache. Each distinct cell counts, including two cells sharing
the same string instance. Repeated reads and the result dictionary do not count
a second time. Unretained direct `Evaluate(parsed)` results do not consume this
cumulative budget; one-value limits still apply.

Exceeding either budget produces `ResourceLimitExceeded`, with the formula cell,
source span and limit name in `Feature` and `Message`, without the full string.
It is an engine failure that IFERROR cannot catch. Failed cells do not retain a
partial successful result. OpenXml and Mapping calculation failures leave the
destination unchanged before the final output copy. The budget does not bound
caller-owned source data, OpenXml snapshots, total process memory or post-GC RSS.

## Distribution and validation

Formulas is a separate packable .NET Standard 2.1 library. OpenXml depends on it;
Mapping/main consume the adapter, and the CLI includes the transitive assembly.
This replaces the original standalone plan's nonpackable restriction because a
public OpenXml package cannot depend on an unpublished, omitted runtime DLL.
Creating local packages does not publish or release them. The calculation core
has no project or runtime package dependencies; inherited analyzers are build-only.

See the validation and performance records for executed checks and remaining gaps.
Excel desktop comparison has not been performed. Unicode collation, floating-point
rounding boundaries, extreme-number text conversion and broad coercion compatibility
remain unverified against Excel.

## Selection boundary

Rendering evaluates selected source cells and their reached dependencies. Explicit
page selection uses a geometry-only prepass and the same candidate-selection method
as page materialization, including repeated titles. Saved row/column geometry and
object bounds determine the pages; cached text is not treated as a calculated result.
The actual reader then calculates those candidates before formatting or text
measurement. An unrelated cycle or unsupported formula on an excluded page does not
stop rendering. Markdown and continuous output follow their existing region rules
rather than applying paginated print-area rules.

Mapping's `RecalculateMappedWorkbook` defaults to true, calculating all formulas for
saved XLSX output. Set it to false to rewrite references and invalidate all caches,
then let the subsequent Reader evaluate its selected source regions. Rendering CLI
commands with Mapping use this ordering. `MappingOptions.FormulaEvaluationOptions`
and the matching Reader property configure resource limits.

OOXML `cm`/`vm` calculation metadata is conservatively rejected when reached, so metadata-backed dynamic arrays or rich values are never assumed to be ordinary scalars.

The executable [invoice sample](../samples/ExcelRenderer.Formulas.Sample/README.md) creates XLSX and verifies 0/1/3/4-line totals and PDF, PNG, SVG and Markdown output.

## Expanded report functions

The calculator supports 40 functions, including conditional aggregates, exact
lookups, six date functions and eight text functions. See the updated function
reference and [oracle provenance and capture instructions](formulas-oracle.md).
Built-in sources use indexed sparse ranges; external data sources remain compatible.
DateSystem follows the workbook, and UTF-16 v1/text resource limits remain explicit.
Excel desktop validation is pending; specification/profile checks are reported
separately in [the expansion validation record](formulas-expansion-validation.md).
