[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkDirectory)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'This command requires Windows Excel desktop, .NET 10 and Python 3.' }
$WorkDirectory = [IO.Path]::GetFullPath($WorkDirectory)
if (Test-Path $WorkDirectory) { throw 'Choose a new work directory to preserve existing observations.' }
New-Item -ItemType Directory -Path $WorkDirectory | Out-Null
function Run {
    param([string]$Executable, [string[]]$CommandArguments)
    & $Executable @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "$Executable exited with code $LASTEXITCODE" }
}
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $cases = 'tests/ExcelRenderer.Formulas.Tests/Fixtures/Oracle/cases.json'
    $cli = 'tools/ExcelRenderer.Formulas.Oracle/oracle.py'
    $inputs = Join-Path $WorkDirectory 'inputs'
    $observed = Join-Path $WorkDirectory 'excel'
    $actual = Join-Path $WorkDirectory 'actual.json'
    Run 'dotnet' @('restore','tools/ExcelRenderer.Formulas.Oracle','--locked-mode')
    Run 'dotnet' @('build','tools/ExcelRenderer.Formulas.Oracle','-c','Release','--no-restore','-m:1')
    Run 'python' @($cli,'generate',$inputs)
    Run 'dotnet' @('run','--project','tools/ExcelRenderer.Formulas.Oracle','-c','Release','--no-build','--',$cases,$actual,$inputs,(Join-Path $WorkDirectory 'roundtrip'))
    & "$PSScriptRoot/capture-formula-oracle.ps1" -InputDirectory $inputs -OutputDirectory $observed
    Run 'python' @($cli,'extract',$observed,(Join-Path $WorkDirectory 'excel-observed.json'))
    Run 'dotnet' @('restore','samples/ExcelRenderer.Formulas.Sample','--locked-mode')
    Run 'dotnet' @('build','samples/ExcelRenderer.Formulas.Sample','-c','Release','--no-restore','-m:1')
    $samples = Join-Path $WorkDirectory 'samples'
    foreach ($count in @(0,1,3,70)) { Run 'dotnet' @('run','--project','samples/ExcelRenderer.Formulas.Sample','-c','Release','--no-build','--',(Join-Path $samples "invoice-$count"),$count.ToString()) }
    $mapping = Join-Path $WorkDirectory 'mapping-inputs'
    Run 'python' @($cli,'prepare-mapping',$samples,$mapping)
    $mappingCases = Join-Path $mapping 'cases.json'
    $mappingActual = Join-Path $WorkDirectory 'mapping-actual.json'
    Run 'dotnet' @('run','--project','tools/ExcelRenderer.Formulas.Oracle','-c','Release','--no-build','--','--mapping',$mappingCases,$mappingActual,$mapping,(Join-Path $WorkDirectory 'mapping-roundtrip'))
    $mappingExcel = Join-Path $WorkDirectory 'mapping-excel'
    & "$PSScriptRoot/capture-formula-oracle.ps1" -InputDirectory $mapping -OutputDirectory $mappingExcel
    $mappingObserved = Join-Path $WorkDirectory 'mapping-excel-observed.json'
    Run 'python' @($cli,'--cases',$mappingCases,'extract',$mappingExcel,$mappingObserved)
    & python $cli compare $actual --observed (Join-Path $WorkDirectory 'excel-observed.json') --strict --require-roundtrip --output (Join-Path $WorkDirectory 'report.json')
    $engineExit = $LASTEXITCODE
    & python $cli --cases $mappingCases compare $mappingActual --observed $mappingObserved --strict --require-roundtrip --output (Join-Path $WorkDirectory 'mapping-report.json')
    if ($engineExit -ne 0 -or $LASTEXITCODE -ne 0) { throw 'Oracle comparison is incomplete or has differences. Inspect both reports.' }
} finally { Pop-Location }
