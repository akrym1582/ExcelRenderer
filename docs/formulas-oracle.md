# Formula oracle and compatibility profiles

The checked-in `Fixtures/Oracle/cases.json` contains 500 independent cases. Its
expectations identify `specification`, `hand-calculated` or provisional `profile`
provenance. **Excel desktop observations: 0.** Passing these cases is not an Excel
compatibility certification. Historical validation documents retain their own SHAs.

The engine, XLSX reader and save/reload pipelines run the same typed inputs. Numbers,
booleans, text (including `001`), errors, missing cells, explicit blanks and formula
empty strings remain distinct. The generator writes inline strings and no formula
cache. The extractor reads f/v/t, not displayed strings. Integer counts, serials
and explicit rounding cases compare exactly; all current tolerances are zero.
Unsupported diagnostics never match an ordinary Excel error.

| Coverage | Cases / evidence | Excel observed |
| --- | --- | --- |
| Existing 19 functions, coercion and parser/rounding regressions | `existing-*`, existing regression tests | 0 |
| Criteria types × comparisons | 19 value kinds/variants × 18 criteria = 342 cases | 0 |
| SUMIF/SUMIFS/COUNTIF/COUNTIFS | shifted origins, blank runs, sum-only candidates, shape errors, selected/unselected errors | 0 |
| VLOOKUP/MATCH/INDEX | first duplicate, wildcard, numeric/text distinction, horizontal vector, missing return, non-A1 origins, omitted vector index, error order | 0 |
| DATE/YEAR/MONTH/DAY/EDATE/EOMONTH | both epochs, 0/59/60/61, serial 60 month shifts, 1462 offset, leap/month ends, 9999 and out-of-range | 0 |
| LEFT/RIGHT/MID/LEN/TRIM/SUBSTITUTE/CONCATENATE/TEXT | UTF-16 v1, Japanese, spaces, fractions, errors, formats, 32767-unit input | 0 |
| Engine bounds | malformed sparse sources, cancellation, wildcard budget, output/cache text limits, dense fallback, self reference | Separate deterministic tests; not Excel proof |
| Mapping invoice | 0/1/3/70 rows, fixed rewritten formula checks; eight pipeline results | 0 |

The criteria matrix is an explicit provisional profile, including numeric text,
blank-vs-empty, booleans and errors. Blank criterion references become zero;
empty text criteria remain empty text. Every later criteria cell is evaluated even
after an earlier mismatch, so cycles, unsupported formulas and resource failures
cannot turn into a mismatch. Nonselected sum cells are not evaluated. Lookup error
order, blank keys/returns, fractional indices, serial zero/fictitious dates and
UTF-16 behavior are also provisional until measured in Excel.

The committed `compatibility-exceptions.json` is empty. An exception must name one
stable case ID, reason, exact observed value and adopted value; wildcard exclusions
are not supported. No expected fixture is rewritten during tests or comparison.
Compatibility-v1 observations require a manifest reporting version 1 for the emoji
length case. A missing/unknown compatibility property is recorded as unavailable
and leaves that case unverified; a v2 observation must not certify v1 behavior.

## Run without Excel

From the repository root with .NET 10 and Python 3:

```sh
dotnet restore ExcelRenderer.slnx --locked-mode
dotnet build tools/ExcelRenderer.Formulas.Oracle -c Release --no-restore -m:1
python3 tools/ExcelRenderer.Formulas.Oracle/test_oracle.py
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py generate /tmp/oracle-inputs
dotnet run --project tools/ExcelRenderer.Formulas.Oracle -c Release --no-build -- tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/cases.json /tmp/actual.json /tmp/oracle-inputs /tmp/oracle-roundtrip
python3 tools/ExcelRenderer.Formulas.Oracle/oracle.py compare /tmp/actual.json --require-roundtrip --output /tmp/report.json
```

The report separately counts matched, mismatch, unsupported, known-difference and
not-observed. Its profile checks do not increment Excel matches. Add `--strict` to
require observed cases and all mandatory categories; zero observations exits 1.
CI runs the non-Excel pipelines on Windows and Linux and reports their provenance.

## Capture with Windows Excel desktop

Use an existing Excel installation, Python 3 and .NET 10. From the repository root,
one command creates inputs, engine outputs, Excel observations and both reports:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/run-formula-oracle.ps1 -WorkDirectory C:\Temp\ExcelRenderer-oracle-new
```

The directory must be new. Capture creates its own Excel COM process, disables
macros/events/link updates, calls CalculateFullRebuild, waits, saves and releases
its COM objects in finally. A supervisor bounds the whole capture (1800 seconds by
default). On timeout it stops only the worker and the newly owned Excel PID whose
start time still matches; it never kills all EXCEL processes or closes a user's
existing instance. The script rejects changed input hashes and macro/external
content. Version/build, OS, UTC capture time, culture, Date1904, compatibility
version, case SHA256 and input/saved XLSX SHA256 are retained in the manifest.

Inspect every mismatch before adopting changes. The command does not install
Excel or request credentials. Copy reviewed observed JSON/manifests/workbooks into
versioned fixtures explicitly. LibreOffice, engine output, hand calculations and
old unverified caches must never be labelled `excel-observed`.

For capture alone, call `scripts/capture-formula-oracle.ps1 -InputDirectory ...
-OutputDirectory ...`. Then run `oracle.py extract` and compare using `--observed`.
The full wrapper also creates Mapping-only, cache-free `invoice-input.xlsx` files.
`oracle.py prepare-mapping SAMPLES OUTPUT` copies those exact files; Excel and the
engine receive identical rewritten formulas. Fixed formula assertions in the
sample separately test reference rewriting, which value agreement alone cannot
prove.

Primary contracts: [SUMIF](https://support.microsoft.com/en-us/excel/functions/sumif-function),
[COUNTIFS](https://support.microsoft.com/en-us/excel/functions/countifs-function),
[VLOOKUP](https://support.microsoft.com/en-us/excel/functions/vlookup-function),
[DATE](https://support.microsoft.com/ja-jp/excel/functions/date-function),
[TEXT](https://support.microsoft.com/en-us/excel/functions/text-function),
[compatibility versions](https://support.microsoft.com/en-gb/excel/compatibility-versions),
[CalculateFullRebuild](https://learn.microsoft.com/en-us/office/vba/api/excel.application.calculatefullrebuild).
These sources do not settle every matrix boundary; those remain explicitly unobserved.

The exact cache-free Mapping workbooks from the recorded Linux run are retained in
[Fixtures/Oracle/Mapping](../tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/Mapping/)
with cases and input SHA256 metadata. Use `--cases` with that collection and the
runner's `--mapping` mode; it is separate from the 500-case engine collection.

## Pipeline isolation and target validation

Each requested pipeline executes independently and records exactly one result.
An OpenXml evaluation failure does not substitute for a roundtrip measurement;
a roundtrip failure leaves the prior OpenXml result intact. Formula calculation
exceptions become diagnostics for that stage. Other failures terminate with the
case ID and pipeline, preserving the original exception. The comparator rejects
all duplicate `(id, pipeline)` entries, including identical values.

Every normal pipeline uses the case's `target` (unqualified uppercase A1 address,
within A1:XFD1048576); it is parsed once per case. Inputs overlapping that target
are rejected by both generation and execution. Missing workbook targets identify
the case and address. The C5 and AA10 fixtures are hand-calculated, with Z1=999
as an unrelated input; their expected Number 5 is not an Excel observation.
Mapping still evaluates the target on its original first worksheet.

See [the three-fix validation report](validation/formulas-review/README.md) for
the current case hash, test totals, profile comparisons and index measurements.
