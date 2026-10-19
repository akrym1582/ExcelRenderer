# Optional Windows/desktop-Excel oracle. Never modifies the supplied workbook.
param(
    [Parameter(Mandatory = $true)][string]$InputWorkbook,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [int]$MaxCells = 10000
)
$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $InputWorkbook).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$copy = Join-Path $output ('excel-input-' + [Guid]::NewGuid().ToString('N') + '.xlsx')
Copy-Item -LiteralPath $source -Destination $copy
$excel = $null
$workbook = $null
try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AutomationSecurity = 3
    $workbook = $excel.Workbooks.Open($copy, 0, $false)
    $excel.CalculateFullRebuild()
    $rows = [Collections.Generic.List[object]]::new()
    foreach ($sheet in $workbook.Worksheets) {
        try {
            $range = $sheet.UsedRange
            try {
                if (($rows.Count + $range.Cells.CountLarge) -gt $MaxCells) {
                    throw "DisplayFormat capture exceeds MaxCells=$MaxCells; use a small representative workbook."
                }
                foreach ($cell in $range.Cells) {
                    try {
                        $display = $cell.DisplayFormat
                        try {
                            $rows.Add([pscustomobject]@{
                                Sheet = $sheet.Name; Address = $cell.Address($false, $false)
                                Value = $cell.Value2; Text = $cell.Text
                                FillColorOle = $display.Interior.Color
                                FontColorOle = $display.Font.Color
                                Bold = $display.Font.Bold; Italic = $display.Font.Italic
                            })
                        } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($display) }
                    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($cell) }
                }
            } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($range) }
        } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($sheet) }
    }
    $rows | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $output 'display-format.json')
    $workbook.ExportAsFixedFormat(0, (Join-Path $output 'excel.pdf'))
    $workbook.SaveCopyAs((Join-Path $output 'excel-recalculated.xlsx'))
    [ordered]@{
        InputSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash
        CapturedUtc = [DateTime]::UtcNow.ToString('o')
        ExcelVersion = $excel.Version; ExcelBuild = $excel.Build
        OperatingSystem = $excel.OperatingSystem; Cells = $rows.Count
    } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $output 'capture.json')
} finally {
    try {
        if ($null -ne $workbook) {
            try { $workbook.Close($false) } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($workbook) }
        }
    } finally {
        try {
            if ($null -ne $excel) {
                try { $excel.Quit() } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel) }
            }
        } finally {
            Remove-Item -LiteralPath $copy -ErrorAction SilentlyContinue
            [GC]::Collect()
            [GC]::WaitForPendingFinalizers()
        }
    }
}
