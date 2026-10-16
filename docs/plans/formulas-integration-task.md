# ExcelRenderer formulas integration — task record

Date: 2026-10-16. Source thread: 01a12841-b5e2-77fa-9a42-fe95888ff027.
Start: origin/main d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4, fetched 2026-10-16.
Branch: feature/formulas-integration-main-20261016. SDK: 10.0.401.

## Current instructions override standalone-only restrictions

Implement the independent BCL-only formula engine and connect it to current main, Core, OpenXml, Mapping and existing CLI. Preserve existing CachedValue default behavior and expose explicit recalculation. Do not reintroduce ClosedXML or another calculator. Add AST-based reference rewriting and typed cache persistence. No release, merge or ready-for-review transition. Push a new branch and create a **draft** PR, monitor current-head CI and correct task-caused failures.

Render only selected/drawn formula cells and reached dependencies; unrelated cycles must not stop rendering. Pipeline: Mapping input update/row expansion → reference rewrite → calculation → formatting → rendering. Document and test invalidation of unevaluated caches when saving XLSX.

## Completion checklist

- [x] Fetch origin/main; inspect current AGENTS; record base and SDK; create fresh branch without overwriting existing work.
- [x] Independent immutable AST, parser, spans, reference flags, typed values/data source, evaluation session and 19 functions.
- [x] Strict unsupported features, circularity, bounded resource use, cancellation and independent session cache lifetime.
- [x] Main/Core/OpenXml/Mapping/CLI explicit recalculation, retained CachedValue default.
- [x] Mapping 0/1/multiple/more-than-template lines, formula copy versus insertion semantics, cross-sheet and named ranges.
- [x] Restore shared formulas via safe syntax processing; persist typed caches; invalidate uncomputed caches; reload and render.
- [x] Reject unsupported arrays/dynamic arrays/external/structured references with feature and cell diagnostic; never silently reuse stale caches.
- [x] All existing output formats and Core regression tests; transactional failure behavior.
- [x] Locked restore, Release Rebuild zero warnings/errors, all tests, package contents/consumer test.
- [x] Existing mutation checks and image comparisons retain original thresholds.
- [ ] Windows/Linux standalone CI, dependency direction check.
- [x] Reproducible executable samples; English/Japanese docs; precise limitations and unverified Excel compatibility.
- [x] Benchmark baseline CachedValue versus changed CachedValue, recalculation off/on, 1k/10k/practical larger rows, independent/shared dependencies/aggregate/Mapping growth; at least 10000 formulas and 100000 SUM cells; warmup/repeats/allocations/environment/counts/correctness.
- [ ] Push branch, create draft PR, wait for latest-head CI and fix task-caused failures.
- [ ] Final report PR URL, base/head SHA, capabilities/limitations, numerical checks, performance, latest CI, unverified work.

## Environment observations

`gh auth status`: GH_TOKEN invalid. `gh api repos/akrym1582/ExcelRenderer`: Forbidden. Read-only git fetch and ls-remote worked. The authorized GitHub connector successfully read latest main, and git push dry-run succeeded. PR/CI operations can use the connector; no token changes were made.

## Attached standalone plan contract (retained implementation specification)

### 1. Purpose

Create ExcelRenderer.Formulas with a custom lexer/parser/calculator; BCL runtime only, no ClosedXML.Parser, ClosedXML, NPOI, ANTLR, DataTable.Compute, compiled C# or eval. No XLSX/ZIP/OpenXML/render/font/PDF dependency in the calculation core. Standalone-only integration, rewrite and packaging exclusions in the original plan are superseded by the current instructions above. Preserve responsibility separation and document packaging choice needed by existing package consumers.

### 2. Baseline and workspace

The reference SHA was d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4; always fetch current main, never reset to a historical snapshot. Preserve uncommitted work and existing branches with a new worktree when needed. Inspect AGENTS at every relevant directory. Integrate preceding Formulas implementations rather than duplicating. Follow current framework/test package versions and lockfiles.

### 3. Fixed initial language and functions

Optional leading =; integer/decimal/scientific numbers using dot and E/e; quoted text with doubled quote escapes including empty/Japanese text; case-insensitive TRUE/FALSE; #NULL!, #DIV/0!, #VALUE!, #REF!, #NAME?, #NUM!, #N/A. Operators binary + - * / ^, unary + -, postfix %, concatenation &, comparisons = <> < <= > >=. Parentheses, nested generic function calls, comma arguments and explicit missing-argument nodes. A1/$A$1/A$1/$A1 through XFD1048576; rectangular ranges, whole columns A:A/$A:$C and whole rows 1:10/$1:$10; sheet qualifiers including '売上 明細' and 'O''Brien'; Japanese names, local/book names and Sheet!Name.

English OOXML function names and commas only; do not normalize localized function names, semicolons or full-width operators. A cell-looking token before '(' is a function identifier. Unsupported functions still parse as FunctionCall and are diagnosed separately. Whole rows/columns must stream through configured limits, never pre-expand huge arrays.

Exactly 19 initial functions: SUM COUNT COUNTA MIN MAX AVERAGE IF IFERROR AND OR NOT ROUND ROUNDUP ROUNDDOWN ABS INT MOD TRUE FALSE. No lookup/conditional aggregates/date/text functions yet; '&' remains supported.

Unsupported: R1C1, 3D, external workbooks, structured references, array constants, dynamic arrays, implicit intersection/@, spill#, intersection/union, formula/relative/multi-area names, INDIRECT/OFFSET LET/LAMBDA iterative calculation UDF VBA volatile functions, date coercion and complete Excel precision emulation. Do not partially accept input or join `A1 B1` into `A1B1`.

### 4. Structure

netstandard2.1, nullable, latest C#, lockfiles. Follow existing StyleCop and XML documentation. Separate Syntax, Values, References, Evaluation, Functions and Data responsibilities. Dedicated net10 tests matching OpenXml.Tests dependencies. Add solution/test exclusion entries without suppressing diagnostics or broad rewrites. Dedicated tests must not depend on XLSX or ExcelNumberFormat. Docs: formulas.md, formulas.ja.md, formulas-validation.md; dependency script scripts/check-formulas-dependencies.py.

### 5. API/data contract

FormulaParser.Parse(string) -> FormulaParseResult containing immutable ParsedFormula/AST or diagnostics. API null misuse may throw ArgumentException; malformed formulas must not throw uncontrolled exceptions. Preserve original string and UTF-16 SourceSpan for every node.

FormulaEngine.Evaluate(parsed, dataSource, origin, options, cancellationToken) and EvaluateCell(target, dataSource, options, cancellationToken). Session API for multi-cell evaluation prevents N² repeated evaluation. origin provides current sheet/cell; A1 is absolute coordinate A1 for direct evaluation regardless of dollar flags. Return scalar Number/Text/Boolean/Error or engine diagnostics; directly referenced Blank becomes Number(0); retain Blank internally. Multiple-cell final results -> UnsupportedArrayEvaluation, not implicit top-left.

FormulaValue storage includes Blank, distinct from empty text and MissingArgument AST. Numbers are finite double; reject NaN/Infinity construction, arithmetic nonfinite -> #NUM!.

IFormulaDataSource resolves case-insensitive sheet display name to unique ID, retrieves Blank/Constant/FormulaText (without saved cache), resolves unqualified local then book names and explicitly qualified sheet-local names only. Fixed absolute cell/rectangle names only. Missing sheet -> #REF!; missing name -> #NAME?. Builder snapshots immutable sparse cells, rejects duplicate registrations, retains display names, rejects invalid unquoted names. One session requires immutable source. New evaluation must not reuse stale calculation results; distinguish reusable AST from cached values.

### 6. Lexer/parser

Character lexer with Pratt/equivalent recursive descent. Nodes Number/Text/Boolean/ErrorLiteral, Unary, Binary, Percent, FunctionCall, MissingArgument, CellReference, RangeReference, NameReference, Parenthesized. References preserve sheet, coordinates, dollar flags and spans. Reversed rectangle bounds normalize for evaluation without changing source AST. Reject differently qualified endpoints.

Precedence high to low: range colon, unary sign, postfix percent, power, multiply/divide, add/subtract, concatenate, comparisons. Binary operators left-associative including power. Fixed tests: -2^2=4, 2^3^2=64, 2^-2=.25. Limit recursion before allocation.

UnsupportedSyntax examples: {1,2}, [Book.xlsx]Sheet1!A1, Table1[Amount], Sheet1:Sheet3!A1, @A1, A1#, A1 B1, (A1,B1). Unterminated literals and missing parentheses -> SyntaxError. Full input consumption required. Whitespace inside quoted text retained; outside whitespace still separates tokens.

### 7. Evaluation semantics

Internal Scalar versus Cell/Range Reference distinction must survive lazy IF so SUM(IF(TRUE,A1:A3,B1:B3)) works. SUM(TRUE) differs from SUM(A1) when A1 is boolean. Only supported aggregate/logical contexts enumerate multireferences.

Arithmetic: Blank=0, Boolean=1/0; Text trims ASCII whitespace and only invariant signed decimal/exponent syntax converts. No date/currency/grouped/percent strings. Conversion failure #VALUE!. Comparison: same-type number/numeric; text OrdinalIgnoreCase; Boolean FALSE<TRUE; mixed Number<Text<Boolean without numeric text coercion. Blank coerces according to opposing type. Concatenation Blank empty, bool uppercase TRUE/FALSE, number invariant G15. Left-to-right first Excel error propagates. Divide zero #DIV/0!. Nonfinite #NUM!, 0^0 #NUM!, zero negative power #DIV/0!, negative fractional power #NUM!.

SUM/MIN/MAX/AVERAGE ignore referenced Blank/Text/Boolean, aggregate Number, propagate Error; direct booleans/numeric strings convert, other text #VALUE!. No numbers SUM/MIN/MAX=0, AVERAGE=#DIV/0!.
COUNT referenced Number only; direct Number/Boolean/numeric string count; ordinary error/non-numeric text do not count; engine failures never ignored. COUNTA counts all except Blank including empty string/booleans/errors, never ignores engine failures.
IF evaluates condition and chosen branch only, 2–3 args; omitted false arg Boolean(false), explicit missing slot Number(0). IFERROR catches Excel errors only, never syntax/unsupported/cycle/limit/cancellation.
AND/OR ignore reference Text/Blank, number nonzero=true, direct Boolean/Number/TRUE-FALSE text accepted, other direct text #VALUE!, no valid values #VALUE!, no short circuit hiding errors. NOT scalar logical conversion with Blank=false.
ROUND ties away from zero, negative digits supported, digits truncate toward zero; ROUNDUP away from zero, ROUNDDOWN toward zero. INT=floor. MOD=n-d*floor(n/d), sign divisor, divisor zero #DIV/0!. TRUE/FALSE no args.
SUM/COUNT/COUNTA 0–255 args; MIN/MAX/AVERAGE/AND/OR 1–255; IF 2–3; IFERROR/MOD/ROUND* 2; NOT/ABS/INT 1; TRUE/FALSE 0. Static InvalidArgumentCount before execution. Missing args explicitly numeric zero for aggregates/numeric functions, false for logical, IF dedicated rules. Do not use decimal globally, default ToEven, arbitrary epsilon, or expose Math.Round 0–15 digits constraint. Handle extreme digits before overflow; document binary boundary differences.

### 8. Sessions, dependencies and failure

Per-cell Unvisited/Visiting/Done with session-local memoization. CircularReference includes cell path. Static full AST validation of every reached formula before evaluation: IF(TRUE,1,UNKNOWN()) -> UnsupportedFunction; IF(TRUE,1,A1) does not evaluate unselected A1 cycle. Distinguish invalid coordinate diagnostics from missing cell Blank. Errors: SyntaxError UnsupportedSyntax UnsupportedFunction InvalidArgumentCount InvalidReference UnsupportedArrayEvaluation CircularReference ResourceLimitExceeded DataSourceFailure. Include formula cell, span, feature, readable message and dependency path. Cancellation is OperationCanceledException, not caught by IFERROR. Never broadly catch bugs as Excel errors.

Default limits: formula length 8192 UTF-16, AST nodes 16384, syntax depth 128, dependency depth 256, formula cells 100000, combined cell reads/range visits 1000000, evaluation steps 2000000. Options permit explicit changes. Check before allocation/recursion and cancellation at loops/boundaries. Stream ranges row-major. No unbounded static caches.

### 9. Fixtures/tests

Expected values from specification/hand calculation/v1-profile, Excel-observed only after real measurement; record expectedSource and rationale. Do not generate expected results from implementation.
Acceptance: 1+2*3=7; (1+2)*3=9; unary/power cases above; 50%*200=100; Japanese concatenation; lazy IF avoids ordinary divide errors; IFERROR(1/0,99)=99; IFERROR(UNKNOWN(),99) and IF(TRUE,1,UNKNOWN()) fail unsupported. ROUND(2.5,0)=3, ROUND(-2.5,0)=-3, ROUND(125,-1)=130; ROUNDUP(-1.21,1)=-1.3; ROUNDDOWN(-1.29,1)=-1.2; INT(-1.2)=-2; MOD(-3,2)=1; MOD(3,-2)=-1.
A1=text 2,A2=true,A3=3: SUM(A1:A3)=3 versus SUM("2",TRUE,3)=6. Blank/empty-text/zero: COUNTA=2, AVERAGE=0; empty average #DIV/0!. COUNT(#N/A reference)=0, COUNTA=1. AND(FALSE,1/0), OR(TRUE,1/0) -> #DIV/0!. IF(FALSE,1)=false versus IF(FALSE,1,)=0. Quoted sheet reference 120*2=240, local-name precedence, cycle path, untaken self-cycle, SUM(A:A) limit=100 -> ResourceLimitExceeded, final A1:A2 -> UnsupportedArrayEvaluation.
Test lexer escaping, Japanese, scientific, flags, ranges, ambiguity, spans/depth/missing; malformed/huge/fuzz fixed seed full-consumption no hangs; conversion matrices, each function boundaries/errors/count/missing; diamond evaluate once, cross-sheet, unrelated cycle, cancellation/new session, cross-workbook cache separation; deterministic depth/range limits. Exact comparisons for small integer/round/error/type and case-specific tolerance for accumulated doubles/power. Executable invoice sample 3 lines qty*price*tax ROUND SUM with names and cross-sheet.
No Excel/LibreOffice/ClosedXML requirement in tests. Explicitly unverified: binary rounding boundaries, Unicode collation, extreme-number G15 concatenation and deep coercions. Never claim complete Excel compatibility.

### 10. Implementation order

Fetch/instructions/branch → projects/lockfiles → values/references/diagnostics/immutable data → parser/AST → sessions/operators → 19 functions → example → integration/reference rewriting/cache serialization/rendering → documentation/dependency/CI/performance → full rebuild/tests/pack → draft PR/current-head CI.

### 11. Validation

Restore then locked restore; python3 scripts/check-formulas-dependencies.py; Release Rebuild engine and whole slnx; dotnet test slnx -c Release --no-build. Windows/Linux isolated formula CI with no fonts/drawing packages. Preserve existing lock versions except required references. Pack impacted projects and inspect dependency/DLL completeness, no publish. Benchmark 10000 constant calculations, 10000 independent formula cells, SUM 100000 cells minimum; warmup, time, allocated bytes, SDK/OS, no hard timing thresholds. Current task additionally requires baseline and integrated benchmarks described above.

### 12. Reporting

Do not label skeletons complete. Report implementation, unsupported features, actual tests/counts, recorded SHA, environment/commands, skipped/unverified checks, package choice, actual performance and draft PR/latest-head CI. No main merge or release.

### 13. Sources

Reference repository/AGENTS at d7c08f464a2be2b1d8f20d71607b6e0f04c1cfc4. Microsoft Excel operator precedence: https://support.microsoft.com/ja-jp/excel/calculation-operators-and-precedence-in-excel ; SUM direct/reference behavior: https://learn.microsoft.com/en-us/office/vba/api/excel.worksheetfunction.sum ; COUNT/COUNTA/AVERAGE/ROUND Microsoft support function pages; floating point: https://learn.microsoft.com/en-us/troubleshoot/microsoft-365-apps/excel/floating-point-arithmetic-inaccurate-result . VBA documentation informs SUM coercion only, never operator precedence. The v1 compatibility profile and limits are independently chosen product specifications.
