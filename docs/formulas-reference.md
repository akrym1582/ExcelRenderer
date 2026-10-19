# Formula support reference

[日本語](formulas-reference.ja.md) · [Usage and API guide](formulas.md) · [README](../README.md)

This reference describes the built-in `ExcelRenderer.Formulas` calculator used by
explicit `Recalculate` mode. It supports the **40 functions listed below** and a
bounded subset of Excel syntax. It does not promise full Excel compatibility.
`CachedValue` remains the default: it displays saved results without evaluating
functions, including formulas outside this calculator's supported set.

For Reader, Core, CLI, Mapping and XLSX saving examples, see the
[usage guide](formulas.md). Namespace changes and required imports are listed in the
[API migration guide](namespace-migration.md). The [invoice sample](../samples/ExcelRenderer.Formulas.Sample/README.md)
combines row expansion, master-data VLOOKUP, SUMIFS/COUNTIFS, dates, TEXT and rendering.

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
| Whole rows/columns | `=SUM(1:10)`, `=SUM(A:C)`, `=SUM($A:$C)` | Built-in sources use sparse indexes; empty positions are processed in bulk. External dense sources remain subject to cell-visit limits. |
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

Range examples use the following cells on the `Examples` sheet. Numeric date results use the 1900 date system.

| Row | A | B | C |
| --- | --- | ---: | --- |
| 1 | Office | 10 | TRUE |
| 2 | Travel | 20 | TRUE |
| 3 | Office | 30 | FALSE |
| 4 | Office | 40 | TRUE |

| Function | Arguments | Behavior | Example and result |
| --- | --- | --- | --- |
| `SUM` | 0–255 | Sum numeric inputs. No numeric inputs returns 0. | `=SUM(2,3)` → 5 |
| `COUNT` | 0–255 | Count numbers; direct convertible arguments also count. Ordinary Excel errors are ignored. | `=COUNT(1,"2",TRUE)` → 3 |
| `COUNTA` | 0–255 | Count nonblank values, including empty Text, booleans and Excel errors. | `=COUNTA("","x")` → 2 |
| `MIN` | 1–255 | Minimum numeric input; no numeric inputs returns 0. | `=MIN(5,2)` → 2 |
| `MAX` | 1–255 | Maximum numeric input; no numeric inputs returns 0. | `=MAX(5,2)` → 5 |
| `AVERAGE` | 1–255 | Arithmetic mean; no numeric inputs produces `#DIV/0!`. | `=AVERAGE(2,4)` → 3 |
| `IF` | 2–3 | Evaluate the selected value branch. An absent third argument returns FALSE when the condition is false. | `=IF(TRUE,"ok","no")` → "ok" |
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
| `SUMIF` | 2–3 | One criterion; sum_range resizes from its top-left. | `=SUMIF(A1:A4,"Office",B1:B4)` → 80 |
| `SUMIFS` | 3–255, odd | Sum aligned ranges matching all criteria; 1–127 pairs. | `=SUMIFS(B1:B4,A1:A4,"Office",C1:C4,TRUE)` → 50 |
| `COUNTIF` | 2 | Count positions matching one criterion, including blank runs. | `=COUNTIF(A1:A4,"Office")` → 3 |
| `COUNTIFS` | 2–254, even | Count aligned positions matching 1–127 criterion pairs. | `=COUNTIFS(A1:A4,"Office",B1:B4,">=30")` → 2 |
| `VLOOKUP` | 3–4 | Exact mode FALSE/0 must be explicit; first matching row. | `=VLOOKUP("Travel",A1:B4,2,FALSE)` → 20 |
| `MATCH` | 2–3 | Exact mode 0 must be explicit; one-based vector position. | `=MATCH("Travel",A1:A4,0)` → 2 |
| `INDEX` | 2–3 | Select one cell by one-based row/column, preserving its reference. | `=INDEX(B1:B4,3)` → 30 |
| `DATE` | 3 | Normalize year/month/day; return a numeric date serial. | `=DATE(2026,1,31)` → 46053 |
| `YEAR` | 1 | Year component in the selected workbook date system. | `=YEAR(DATE(2026,1,31))` → 2026 |
| `MONTH` | 1 | Month component, from 1 to 12. | `=MONTH(DATE(2026,2,28))` → 2 |
| `DAY` | 1 | Day component; the 1900 profile retains day zero and serial 60. | `=DAY(DATE(2026,2,28))` → 28 |
| `EDATE` | 2 | Shift months, clamping the day to the target month end. | `=EDATE(DATE(2026,1,31),1)` → 46081 |
| `EOMONTH` | 2 | Return the last day of the shifted month. | `=EOMONTH(DATE(2026,1,15),1)` → 46081 |
| `LEFT` | 1–2 | UTF-16 prefix; default length 1. | `=LEFT("Invoice",3)` → "Inv" |
| `RIGHT` | 1–2 | UTF-16 suffix; default length 1. | `=RIGHT("Invoice",4)` → "oice" |
| `MID` | 3 | One-based UTF-16 substring. | `=MID("Invoice",3,3)` → "voi" |
| `LEN` | 1 | Count UTF-16 code units, rather than grapheme clusters. | `=LEN("😀")` → 2 |
| `TRIM` | 1 | Trim/collapse ASCII U+0020 spaces only. | `=TRIM("  A   B  ")` → "A B" |
| `SUBSTITUTE` | 3–4 | Case-sensitive nonoverlapping replacement; optional occurrence. | `=SUBSTITUTE("a-b-a","a","X",2)` → "a-b-X" |
| `CONCATENATE` | 1–255 | Concatenate scalar values without range expansion. | `=CONCATENATE("INV-",2026)` → "INV-2026" |
| `TEXT` | 2 | Format one number/date with the supported invariant patterns. | `=TEXT(1234.5,"#,##0.00")` → "1,234.50" |


### Conditional aggregates

```text
SUMIF(range, criteria, [sum_range])
SUMIFS(sum_range, criteria_range1, criteria1, [...])
COUNTIF(range, criteria)
COUNTIFS(criteria_range1, criteria1, [...])
```

`SUMIFS` and `COUNTIFS` accept 1–127 range/criterion pairs. Every range must have
identical row and column counts; mismatched shapes return `#VALUE!`. Ranges may
start at different cells or lie on different sheets: matching uses relative
positions. `SUMIF` instead sizes `sum_range` from its top-left to match `range`.
For the example data, `=SUMIF(A1:A4,"Office",B1)` still returns 80. A resized range
extending beyond XFD/1048576 returns `#REF!`. Without `sum_range`, SUMIF sums `range`.
Only numeric sum cells contribute; a matched sum-cell Excel error propagates.

A criterion is one scalar value or text prefixed by `=`, `<>`, `<`, `<=`, `>` or
`>=`, such as `">="&B1`. Text comparison is OrdinalIgnoreCase. In this explicit
profile, numeric criteria also match convertible numeric text, and blank cells
act as zero for numeric criteria. Boolean criteria match typed booleans.
`""` matches missing cells and empty Text; these remain distinct stored values.
For example, `=COUNTIF(A:A,"")` counts every empty position across all 1048576 rows,
not just a worksheet's used range. Formula results equal to zero, empty Text,
FALSE or an Excel error are included in the data source, rather than dropped.

For text equality/inequality, `*` matches any sequence, `?` one UTF-16 unit, and
`~` escapes a wildcard. `"~*"` matches a literal asterisk. Wildcard equality
matches Text, including empty Text, but does not match a missing cell. An error
passed directly as the criterion propagates; an error spelling supplied as Text,
such as `"#N/A"`, can select stored error values. These coercions are the
[specified Oracle profile](formulas-oracle.md), not an Excel desktop certification.

### Exact lookup and single-cell INDEX

```text
VLOOKUP(lookup_value, table_array, col_index_num, FALSE)
MATCH(lookup_value, lookup_array, 0)
INDEX(reference, row_num, [column_num])
```

The argument-count table permits a missing mode, matching the familiar syntax,
but this engine requires **explicit FALSE/0 for VLOOKUP and 0 for MATCH**.
Omitting the mode or requesting approximation produces the engine diagnostic
`UnsupportedFeature`, which IFERROR cannot catch. `=VLOOKUP("Travel",A1:B4,2)`
is therefore unsupported; adding `FALSE` yields 20.

VLOOKUP searches the first column and returns a value from the same row. MATCH
accepts one row or one column and returns a one-based position. Both select the
first duplicate, ignore text case, and support the text wildcards above. They
preserve typed equality: numeric Text `"10"` does not equal Number 10. A missing
lookup key becomes zero, and a numeric zero key can match a blank position.
No match returns `#N/A`. Search errors reached before a match propagate; later
rows and unused return columns are not evaluated merely to find that match.
VLOOKUP truncates a fractional column index toward zero; an index below 1 returns
`#VALUE!`, and an index wider than the table returns `#REF!`.

INDEX selects **one reference**, so `=SUM(INDEX(B1:B4,3))` also returns 30.
A two-dimensional rectangle requires both row and column. A one-column vector
accepts `INDEX(B1:B4,3)`; a one-row vector accepts `INDEX(B1:D1,2)`.
`INDEX(B1:D1,,2)` and `INDEX(B1:B4,3,)` support the corresponding omitted index.
Fractional indices truncate toward zero. Negative indices return `#VALUE!` and
out-of-range indices return `#REF!`. Zero indices, arrays, multi-area references
and a two-dimensional reference without its column are unsupported array
operations (`UnsupportedArrayEvaluation`). INDEX has no `area_num` overload.

### Dates and workbook epochs

```text
DATE(year, month, day)
YEAR(serial_number) / MONTH(serial_number) / DAY(serial_number)
EDATE(start_date, months) / EOMONTH(start_date, months)
```

Dates are numeric serials, not culture-parsed strings. The standalone session uses
`FormulaEvaluationOptions.DateSystem` (default `Excel1900`); OpenXml uses the
workbook's `Date1904` flag and copies options without modifying the caller's object.
A 1904 workbook's serial zero is 1904-01-01. The 1900 profile preserves serial zero
as 1900/1/0 and serial 60 as the fictitious 1900-02-29: `=DAY(60)` is 29.
Negative or out-of-range dates return `#NUM!`; the maximum date is 9999-12-31.

DATE truncates numeric arguments toward zero, adds 1900 to input years 0–1899,
and normalizes overflowing months/days. For example,
`=DATE(2026,2,0)` equals `=DATE(2026,1,31)` (46053 in the 1900 system).
YEAR/MONTH/DAY discard the serial's time fraction. EDATE/EOMONTH truncate month
offsets, discard time fractions and clamp to the target month where needed.
`=TEXT(EDATE(DATE(2026,1,31),1),"yyyy-mm-dd")` returns `"2026-02-28"`.

XLSX ISO date cells accept timezone-free ISO dates/times and preserve their time
fractions during serial conversion. Locale date strings and timezone-bearing ISO
values are unsupported; the date functions do not call a locale date parser.

### Text functions and bounded TEXT formats

```text
LEFT(text, [num_chars]) / RIGHT(text, [num_chars])
MID(text, start_num, num_chars) / LEN(text) / TRIM(text)
SUBSTITUTE(text, old_text, new_text, [instance_num])
CONCATENATE(text1, [...]) / TEXT(value, format_text)
```

LEFT/RIGHT default to one unit. Lengths are nonnegative, positions are one-based,
and fractional counts/positions truncate toward zero. A negative length or a MID
start below 1 returns `#VALUE!`; a start beyond the text returns empty Text.
All four indexing/length functions use UTF-16 code units: `=LEN("😀")` is 2 and
slicing can split a surrogate pair. They do not implement grapheme-aware slicing.
TRIM only trims/collapses ASCII U+0020, preserving tabs, U+00A0 and ideographic spaces.

SUBSTITUTE compares case-sensitively and replaces nonoverlapping occurrences.
An omitted instance replaces all; a positive one-based instance selects one;
a nonpositive truncated instance returns `#VALUE!`. Empty `old_text` leaves the
input unchanged. CONCATENATE takes 1–255 scalar inputs, converts numbers with
invariant G15 and booleans to TRUE/FALSE, and does not expand multi-cell ranges.

TEXT supports these invariant patterns; every result is Text:

| Pattern | Example | Result |
| --- | --- | --- |
| Zero padding | `=TEXT(42,"00000")` | `"00042"` |
| Required decimals | `=TEXT(12.5,"0.00")` | `"12.50"` |
| Grouping | `=TEXT(1234.5,"#,##0.00")` | `"1,234.50"` |
| Percent | `=TEXT(0.125,"0.0%")` | `"12.5%"` |
| Year/month/day | `=TEXT(DATE(2026,1,31),"yyyy-mm-dd")` | `"2026-01-31"` |
| Year/month | `=TEXT(DATE(2026,1,31),"yyyy年mm月")` | `"2026年01月"` |

Numeric cores contain one or more `0` placeholders, or `#,##0`, with optional
required decimal zeros and trailing `%`. Quoted prefix/suffix literals are
supported. Date tokens must be in year/month[/day] order: `yyyy`, `m`/`mm`, and
`d`/`dd`, with separators such as `-`, `/`, `年`, `月`, `日`. English token case is
ignored. Decimal and group separators stay `.` and `,`, regardless of locale.
Numeric formatting uses the decimal rounding profile below.

Time tokens, weekdays, month names, locale/era codes, colors, conditional or
multiple sections, scientific, fraction and accounting formats yield
`UnsupportedFeature`. TEXT is a bounded subset, not the entire Excel format
language. TEXT checks formatted output length before allocating. Evaluated and retained
Text obey both session budgets described under resource limits.

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
| `UnsupportedFeature` | Approximate lookup mode or an unsupported TEXT format. |
| `UnsupportedFunction` | A function outside the 40-name table. |
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
| Other functions | XLOOKUP, CONCAT, TODAY/NOW, financial functions and every name outside the table. |
| Array calculation | Array constants, legacy array formulas, dynamic arrays/spills and array-returning evaluation. Reached XLSX data-table formulas and calculation metadata are also rejected. |
| Reference grammar | External workbooks, 3D and structured/table references, R1C1, implicit intersection (`@`), spill (`#`), reference unions/intersections. |
| Names and execution | Formula/relative/multi-area names, INDIRECT/OFFSET, LET/LAMBDA, volatile execution, UDF/VBA and iterative/circular calculation. |
| Locale/date semantics | Localized formula syntax, locale-aware text collation, locale date strings or timezone-bearing ISO dates; date functions interpret numeric serials using the session/workbook date system. |

Literal HYPERLINK metadata extraction in cached-value rendering is a separate
feature; HYPERLINK is not a supported calculator function. The main renderer evaluates
[Phase 1 conditional-formatting conditions](conditional-formatting.md) through the same
engine; Core retains ordinary saved styles. Data-validation formulas remain metadata. Supported formulas can still fail when a reached dependency
uses an unsupported feature or exceeds a limit.

The function and syntax tables describe implemented behavior, not an Excel desktop
compatibility certification. See [validation](formulas-validation.md),
[review-fix validation](formulas-review-fixes-validation.md) and
[performance](formulas-performance.md) for the tested SHAs and remaining gaps.

## Sparse ranges and lookup performance

Built-in sources implement `ISparseFormulaDataSource`; external `IFormulaDataSource`
implementations keep dense bounded fallback. The sparse contract requires all
stored coordinates in unique row-major order, including formula zeros, empty Text,
errors and calculation metadata. Missing cells are not retained in result caches.
Conditional ranges join by relative position with a k-way cursor merge; no
full-column arrays are allocated.

`FormulaSession.GetStatistics()` reports source reads, cursor candidates, bulk
blank positions, cell visits, steps and retained values. Direct traversal for a
dense rectangle requires a coordinate-index proof that every position is stored;
a metadata-only UsedRange is insufficient. Sparse/custom cursors still validate
ordered, unique coordinates.

Single-column queries use a column-to-sorted-rows coordinate index: an absent
column returns immediately; a populated column takes O(log Ncol + K) work for
Ncol stored cells and K returned coordinates, independent of other columns.
Multi-column queries retain row-major indexing. Added retained coordinate memory
is O(stored cells); values and evaluation results are not duplicated.

`IndexWork` additionally counts built-in index dictionary probes, binary-search
comparisons, inspected rows and coordinates, including density proofs. Each unit
also consumes one evaluation step and checks cancellation, even inside a
`MoveNext` that returns no candidate. There is no batching delay. `SourceReads`
and `RangeCandidates` retain their previous meanings. This internal-work guarantee
applies to built-in `FormulaRangeIndex.Query` scans; arbitrary external iterator
code cannot be interrupted while it remains inside its own `MoveNext`.

Exact lookups retain only the evaluated prefix of a session-local vector index.
Repeated keys avoid rescanning that prefix while preserving first duplicates,
typed equality, error order and lazy return evaluation. Limits are 16 vectors,
65536 typed values and 131072 indexed Text units per session. Reaching a limit
uses bounded streaming fallback; no cursor is retained between calls. Wildcard
searches of the prefix consume the same work budget before extending it.

[Expansion validation](formulas-expansion-validation.md) and
[Oracle provenance](formulas-oracle.md) distinguish specification comparisons
from the still-unobserved Excel desktop comparisons.
