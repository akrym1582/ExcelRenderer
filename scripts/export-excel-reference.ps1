# Run interactively on Windows with licensed desktop Microsoft Excel installed.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Operator,
    [string[]]$CaseId = @()
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Windows desktop Microsoft Excel is required.' }
$root = Split-Path $PSScriptRoot -Parent
$catalog = Get-Content (Join-Path $root 'docs/validation/excel-reference/cases.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$selected = @($catalog.cases | Where-Object { $CaseId.Count -eq 0 -or $_.id -in $CaseId })
if ($selected.Count -eq 0 -or @($CaseId | Where-Object { $_ -notin $catalog.cases.id }).Count) { throw 'Unknown case ID.' }
if ([string]::IsNullOrWhiteSpace($Operator)) { throw 'An operator identifier is required.' }
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path $output) { throw 'Use a new output directory to avoid stale PDFs/evidence.' }
New-Item $output -ItemType Directory | Out-Null
function Get-Sha([string]$Path) { (Get-FileHash $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$fontRoots = @((Join-Path $env:WINDIR 'Fonts'), (Join-Path $env:LOCALAPPDATA 'Microsoft/Windows/Fonts'))
$fonts = @($fontRoots | Where-Object { Test-Path $_ } | ForEach-Object {
    Get-ChildItem $_ -File | Where-Object { $_.Extension -in '.ttf', '.ttc', '.otf' } | ForEach-Object {
        @{ name = $_.Name; sha256 = Get-Sha $_.FullName }
    }
})
if ($fonts.Count -eq 0) { throw 'Could not inventory installed font files.' }
$excel = $null
$bootstrap = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AskToUpdateLinks = $false
    $excel.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
    $excel.EnableEvents = $false
    # Excel requires an open workbook before changing application calculation mode.
    $bootstrap = $excel.Workbooks.Add()
    $excel.Calculation = -4135 # xlCalculationManual
    $excel.CalculateBeforeSave = $false
    foreach ($case in $selected) {
        $inputPath = Join-Path $root $case.input
        if ((Get-Sha $inputPath) -ne $case.input_sha256) { throw "Input hash mismatch: $($case.id)" }
        $book = $null
        try {
            $book = $excel.Workbooks.Open($inputPath, 0, $true, 5, '', '', $true)
            $visible = @($book.Worksheets | Where-Object { $_.Visible -eq -1 } | ForEach-Object { $_.Name })
            if (($visible | ConvertTo-Json -Compress) -cne (@($case.selected_sheets) | ConvertTo-Json -Compress)) {
                throw "Visible sheet selection mismatch: $($case.id)"
            }
            $pdf = Join-Path $output ($case.id + '.pdf')
            # Entire workbook, all visible worksheets, existing print areas/settings,
            # standard quality, no From/To limits, no source save or explicit recalc.
            $book.ExportAsFixedFormat(0, $pdf, 0, $true, $false)
            $evidence = [ordered]@{
                reference_kind = 'microsoft-excel-windows'
                case_id = $case.id
                input_sha256 = $case.input_sha256
                pdf_sha256 = Get-Sha $pdf
                selected_sheets = @($case.selected_sheets)
                export_method = 'ExportAsFixedFormat'
                quality = 'standard'
                ignore_print_areas = $false
                calculation = 'manual-no-recalculate'
                excel_version = [string]$excel.Version
                excel_build = [string]$excel.Build
                windows_version = [System.Environment]::OSVersion.VersionString
                exported_at_utc = [DateTime]::UtcNow.ToString('o')
                operator = $Operator
                active_printer = [string]$excel.ActivePrinter
                font_files = $fonts
            }
            $json = $evidence | ConvertTo-Json -Depth 10
            [IO.File]::WriteAllText((Join-Path $output ($case.id + '.json')), $json + "`n", [Text.UTF8Encoding]::new($false))
            Write-Host "Exported $($case.id)"
        }
        finally {
            if ($null -ne $book) {
                $book.Close($false)
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book)
            }
        }
        if ((Get-Sha $inputPath) -ne $case.input_sha256) { throw "Source changed during export: $($case.id)" }
    }
}
finally {
    if ($null -ne $bootstrap) {
        $bootstrap.Close($false)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($bootstrap)
    }
    if ($null -ne $excel) {
        $excel.Quit()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
