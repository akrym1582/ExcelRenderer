# Formula support reference

[日本語](formulas-reference.ja.md) · [Usage and API guide](formulas.md) · [README](../README.md)

This reference describes the built-in `ExcelRenderer.Formulas` calculator used by
explicit `Recalculate` mode. It supports the **19 functions listed below** and a
bounded subset of Excel syntax. It does not promise full Excel compatibility.
`CachedValue` remains the default: it displays saved results without evaluating
functions, including formulas outside this calculator's supported set.

For Reader, Core, CLI, Mapping and XLSX saving examples, see the
[usage guide](formulas.md). The [invoice sample](../samples/ExcelRenderer.Formulas.Sample/README.md)
combines row expansion, cross-sheet tax, ROUND, SUM and rendering.

## Supported syntax and references

Examples use a leading `=`; `FormulaParser.Parse` also accepts formulas without it.
English function names are case-insensitive. Arguments use commas and decimal
numbers use an invariant `.` separator; localized function aliases and semicolon
argument separators are unsupported.

| Feature | Examples | Scope |
| --- | --- | --- |
| Numbers | `=12`, `=-1.25`, `=1.25E-3` | Finite double values; scientific notation is supported. |
| Text | `="hello"`, `="a""b"`, `=","` | Double quotes delimit text; doubled quotes escape a quote. Punctuation inside text is never syntax. |
| Booleans | `=TRUE`, `=FALSE`, `=TRUE()` | Boolean literals and zero-argument functions. |
| Excel error literals | `=#DIV/0!`, `=#N/A` | The seven error values listed below. |
| Grouping and operators | `=(A1+2)*3`, `=50%`, `="("&A1&")"` | Unary signs, arithmetic, percent, concatenation and comparisons. |
| Cell references | `=A1`, `=$A$1`, `=$A1`, `=A$1` | A1 notation, rows 1–1048576 and columns A–XFD. `$` controls copy displacement. |
| Other sheets | `=Rates!B1`, `='Tax Rates'!B1`, `='O''Brien'!A1` | Single quotes quote sheet names; doubled apostrophes escape an apostrophe. Empty quoted sheet names are invalid. |
| Rectangular ranges | `=SUM(A1:C10)`, `=SUM(Rates!A1:B3)` | Both endpoints must describe the same coordinate type on one sheet. A quoted string cannot be an endpoint. |
| Whole rows/columns | `=SUM(1:10)`, `=SUM(A:C)`, `=SUM($A:$C)` | Iterated under resource limits. A full column can exceed the default cell-visit budget; prefer bounded ranges. |
| Fixed defined names | `=SUM(Amounts)`, `=Rates!TaxRate` | Fixed cell/rectangular ranges; see the name rules below. |
| Omitted arguments | `=SUM(,1,)`, `=IF(FALSE,1,)` | Actual empty argument positions evaluate as numeric zero. An empty text argument remains Text. |
| Shared formulas | Saved shared-formula master/follower cells | The OpenXml adapter restores supported references before evaluation or Mapping row rewriting. Text literals are preserved. |

The XLSX adapter accepts defined names containing one sheet-qualified, fully
absolute cell or rectangle, for example `Rates!$B$1` or `Invoice!$D$2:$D$10`.
Sheet-local names take precedence over workbook-global names for unqualified
lookups. An explicitly sheet-qualified name resolves in that sheet's local scope.
Formula names, relative names and multi-area names are unsupported. The standalone
workbook builder binds names directly to fixed `FormulaRange` values.

Ranges are references, not arrays. Aggregates accept ranges, and IF can select a
range for an enclosing aggregate, such as `=SUM(IF(TRUE,A1:A3,B1:B3))`.
Returning a multi-cell range as a scalar, including `=A1:A3`, fails with
`UnsupportedArrayEvaluation`; no top-left implicit intersection is performed.

## Operators and precedence

The table runs from highest to lowest precedence. This is the engine's explicit
profile; two differences worth checking when migrating formulas are
`=-2^2` → **4** and `=2^3^2` → **64**.

| Precedence | Operators | Behavior |
| --- | --- | --- |
| 1 | Unary `+`, `-` | Bind before powers. |
| 2 | Postfix `%` | Divide by 100. |
| 3 | `^` | Power, left-associative. |
| 4 | `*`, `/` | Multiply and divide. |
| 5 | Binary `+`, `-` | Add and subtract. |
| 6 | `&` | Concatenate; numbers use invariant G15, booleans use TRUE/FALSE. |
| 7 | `=`, `<>`, `<`, `<=`, `>`, `>=` | Compare; text uses OrdinalIgnoreCase, and differing nonblank types order Number < Text < Boolean. |

Parentheses override grouping. Nonfinite arithmetic results produce `#NUM!`;
`=0^0` produces `#NUM!` and `=0^-1` produces `#DIV/0!` in this profile.

## Supported functions

The argument counts below count syntactic arguments, not cells within a range.
An aggregate with a range may visit many cells, subject to session limits.
Only the following names are supported; parsing a function call does not establish
that the evaluator supports it.

| Function | Arguments | Behavior | Example and result |
| --- | --- | --- | --- |
| `SUM` | 0–255 | Sum numeric inputs. No numeric inputs returns 0. | `=SUM(2,3)` → 5 |
| `COUNT` | 0–255 | Count numbers; direct convertible arguments also count. Ordinary Excel errors are ignored. | `=COUNT(1,"2",TRUE)` → 3 |
| `COUNTA` | 0–255 | Count nonblank values, including empty Text, booleans and Excel errors. | `=COUNTA("","x")` → 2 |
| `MIN` | 1–255 | Minimum numeric input; no numeric inputs returns 0. | `=MIN(5,2)` → 2 |
| `MAX` | 1–255 | Maximum numeric input; no numeric inputs returns 0. | `=MAX(5,2)` → 5 |
| `AVERAGE` | 1–255 | Arithmetic mean; no numeric inputs produces `#DIV/0!`. | `=AVERAGE(2,4)` → 3 |
| `IF` | 2–3 | Evaluate the selected value branch. An absent third argument returns FALSE when the condition is false. | `=IF(TRUE,"ok","no")` → Text("ok") |
| `IFERROR` | 2 | Evaluate the fallback only for an ordinary Excel error value. The first argument must be scalar. | `=IFERROR(1/0,0)` → 0 |
| `AND` | 1–255 | Logical conjunction; later errors are still evaluated. No usable logical inputs produces `#VALUE!`. | `=AND(TRUE,FALSE)` → FALSE |
| `OR` | 1–255 | Logical disjunction; later errors are still evaluated. No usable logical inputs produces `#VALUE!`. | `=OR(TRUE,FALSE)` → TRUE |
| `NOT` | 1 | Negate a coerced logical value. | `=NOT(TRUE)` → FALSE |
| `ROUND` | 2 | Round to `num_digits` decimal places; ties go away from zero. | `=ROUND(1.005,2)` → 1.01 |
| `ROUNDUP` | 2 | Round away from zero if discarded decimal digits are nonzero. | `=ROUNDUP(-1.21,1)` → -1.3 |
| `ROUNDDOWN` | 2 | Round toward zero. | `=ROUNDDOWN(0.29,2)` → 0.29 |
| `ABS` | 1 | Absolute value. | `=ABS(-3)` → 3 |
| `INT` | 1 | Floor toward negative infinity. | `=INT(-1.2)` → -2 |
| `MOD` | 2 | `number - divisor * floor(number / divisor)`; zero divisor produces `#DIV/0!`. | `=MOD(-3,2)` → 1 |
| `TRUE` | 0 | Boolean true. | `=TRUE()` → TRUE |
| `FALSE` | 0 | Boolean false. | `=FALSE()` → FALSE |

### Coercion and evaluation

- Numeric operators/functions accept invariant signed decimal/exponent Text after
  trimming ASCII spaces, tabs and line breaks. Currency, grouped numbers, percent
  strings and date strings are not numeric coercions. Booleans convert to 1/0;
  blank cells convert to zero in numeric contexts.
- SUM/MIN/MAX/AVERAGE ignore referenced Text/booleans and blank cells. Direct
  numeric Text and booleans convert to numbers; invalid direct numeric Text yields
  `#VALUE!`. Excel error inputs propagate. COUNT ignores ordinary Excel errors;
  COUNTA counts them. Empty Text and blank cells remain distinct.
- Logical coercion accepts booleans, numbers (zero is false), blank (false) and
  TRUE/FALSE Text ignoring case. AND/OR ignore referenced Text and blank cells;
  they do not short-circuit later errors.
- IF/IFERROR evaluate only the selected value branch, but function names and
  argument counts are statically checked across the reached formula's whole AST.
  `=IF(TRUE,1,UNKNOWN())` fails with `UnsupportedFunction`. A cell reference in an
  unselected branch is not read merely to evaluate that branch.

### Decimal rounding profile

Numbers remain double. ROUND/ROUNDUP/ROUNDDOWN alone round the input double's
shortest invariant round-trip decimal representation, using integer coefficient,
exponent and quotient/remainder arithmetic, then convert back to double.
`num_digits` truncates toward zero; positive places address the fraction and
negative places address tens, hundreds and so on. For example, `=ROUND(125,-1)`
returns 130 and `=ROUND(-2.5,0)` returns -3.

This fixes binary scaling cases such as `ROUND(1.005,2)` and preserves adjacent
double distinctions. It does not normalize computed 0.30000000000000004 to 0.3
or reproduce every Excel internal precision correction. Nonfinite results yield
`#NUM!`. Zero retains the existing sign convention, including positive zero from
nonzero inputs for places below -308. See the
[compatibility profile](formulas.md#explicit-compatibility-profile) and
[recorded correctness/performance checks](formulas-review-fixes-validation.md).

## Errors and diagnostics

Ordinary Excel errors are typed values: `#NULL!`, `#DIV/0!`, `#VALUE!`, `#REF!`,
`#NAME?`, `#NUM!` and `#N/A`. A standalone evaluation can succeed while returning
an Error value; XLSX saving persists it with the error cache type. IFERROR can
catch these values. For example, an unresolved name returns `#NAME?`, whereas an
unsupported function is an engine failure.

| Engine failure | Typical trigger |
| --- | --- |
| `SyntaxError`, `UnsupportedSyntax`, `InvalidReference` | Malformed syntax, unsupported reference grammar or an out-of-bounds address. |
| `UnsupportedFunction` | A function outside the 19-name table. |
| `InvalidArgumentCount` | A supported function receives the wrong number of arguments. |
| `UnsupportedArrayEvaluation` | A multi-cell range is used where a scalar is required. |
| `CircularReference` | A reached dependency cycle; diagnostics include the dependency path. |
| `ResourceLimitExceeded` | A parsing, evaluation, one-value Text or cached-Text budget is exceeded. |
| `DataSourceFailure` | Reading or resolving data fails. |

Standalone APIs return engine failures through `Success=false` and `Diagnostics`.
OpenXml integration surfaces them as `FormulaCalculationException`; public
rendering may wrap that exception. IFERROR does not hide engine failures.
Cancellation throws `OperationCanceledException`. Unsupported reached formulas
never fall back to saved caches. Recalculation failures before the final output
copy leave the destination unchanged; a failing destination write can be partial.

## Resource limits

`FormulaEvaluationOptions` sets these defaults. Sessions copy options on creation;
create a new session after changing inputs or limits. Shared session memoization
is local to one immutable data source, never a global result cache.

| Option | Default | Unit/scope |
| --- | ---: | --- |
| `MaxFormulaLength` | 8192 | UTF-16 units per formula |
| `MaxAstNodes` | 16384 | Syntax nodes per formula |
| `MaxSyntaxDepth` | 128 | Parser/AST depth |
| `MaxDependencyDepth` | 256 | Active cell dependency depth |
| `MaxFormulaCells` | 100000 | Formula cells evaluated per session |
| `MaxCellVisits` | 1000000 | Cell reads/range visits per session |
| `MaxEvaluationSteps` | 2000000 | Validation/evaluation steps per session |
| `MaxStringLength` | 32767 | UTF-16 units per evaluated Text, including temporary concatenations |
| `MaxCachedStringCharacters` | 16777216 | Sum of Text UTF-16 lengths retained by distinct cached cells |

The two Text limits accept zero (only empty evaluated/cached Text respectively)
and reject negative values at session creation. Surrogate pairs occupy two UTF-16
units. Repeated reads and registration of the same result in the result dictionary
do not count twice; different cells sharing one string each count. Unretained
`Evaluate(parsed)` results do not consume the cumulative cache budget. Concatenation
checks combined lengths before allocating. Limit diagnostics identify the formula
cell, source span and exceeded Text limit without including the full string.
These budgets do not bound caller-owned data, OpenXml snapshots or total process
memory, and do not guarantee all Excel Unicode length rules. Configuration examples
and the full retention contract are in [the guide](formulas.md#text-resource-limits).

## Unsupported features

| Area | Examples |
| --- | --- |
| Other functions | SUMIF/SUMIFS, COUNTIF/COUNTIFS, XLOOKUP/VLOOKUP, INDEX/MATCH, TEXT/LEFT/CONCAT, DATE/TODAY/NOW, financial functions and every name outside the table. |
| Array calculation | Array constants, legacy array formulas, dynamic arrays/spills and array-returning evaluation. Reached XLSX data-table formulas and calculation metadata are also rejected. |
| Reference grammar | External workbooks, 3D and structured/table references, R1C1, implicit intersection (`@`), spill (`#`), reference unions/intersections. |
| Names and execution | Formula/relative/multi-area names, INDIRECT/OFFSET, LET/LAMBDA, volatile execution, UDF/VBA and iterative/circular calculation. |
| Locale/date semantics | Localized formula syntax, locale-aware text collation, date-string or XLSX ISO-date cell coercion; numeric serial dates are ordinary numbers to the calculator. |

Literal HYPERLINK metadata extraction in cached-value rendering is a separate
feature; HYPERLINK is not a supported calculator function. Conditional-formatting
and validation formulas remain workbook metadata and are not evaluated to render
conditional styles. Supported formulas can still fail when a reached dependency
uses an unsupported feature or exceeds a limit.

The function and syntax tables describe implemented behavior, not an Excel desktop
compatibility certification. See [validation](formulas-validation.md),
[review-fix validation](formulas-review-fixes-validation.md) and
[performance](formulas-performance.md) for the tested SHAs and remaining gaps.
