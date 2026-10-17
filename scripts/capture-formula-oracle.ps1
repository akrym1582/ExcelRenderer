[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$InputDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [int]$TimeoutSeconds = 1800,
    [switch]$Worker
)
$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'Excel desktop capture requires Windows; no installation is attempted.' }
$InputDirectory = [IO.Path]::GetFullPath($InputDirectory)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if ($InputDirectory -eq $OutputDirectory) { throw 'Input and output directories must differ.' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$statePath = Join-Path $OutputDirectory 'capture-process.json'
if (-not $Worker) {
    if (Test-Path (Join-Path $OutputDirectory 'manifest.json')) { throw 'Choose a new output directory to preserve prior observations.' }
    Remove-Item $statePath -ErrorAction SilentlyContinue
    $arguments = '-NoProfile -STA -File "{0}" -InputDirectory "{1}" -OutputDirectory "{2}" -TimeoutSeconds {3} -Worker' -f $PSCommandPath,$InputDirectory,$OutputDirectory,$TimeoutSeconds
    $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -ArgumentList $arguments -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill()
        # Stop only the newly created, recorded Excel process with the same start time.
        if (Test-Path $statePath) {
            $state = Get-Content $statePath -Raw | ConvertFrom-Json
            $owned = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
            if ($owned -and $owned.ProcessName -eq 'EXCEL' -and $owned.StartTime.ToUniversalTime().Ticks.ToString() -eq $state.startTicks) { $owned.Kill() }
        }
        throw 'Excel capture timed out. Only this capture worker and its owned Excel process were stopped.'
    }
    if ($process.ExitCode -ne 0) { throw "Excel capture failed with exit code $($process.ExitCode)." }
    Write-Host "Capture completed: $OutputDirectory"
    exit 0
}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FormulaOracleWindow {
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
'@
Add-Type -AssemblyName System.IO.Compression.FileSystem
$inputs = Get-Content (Join-Path $InputDirectory 'inputs.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$existing = @(Get-Process -Name EXCEL -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$excel = $null; $workbooks = $null; $book = $null; $ownedExcel = $false
$observations = @()
try {
    $excel = New-Object -ComObject Excel.Application
    [uint32]$excelProcessId = 0
    [void][FormulaOracleWindow]::GetWindowThreadProcessId([IntPtr]$excel.Hwnd, [ref]$excelProcessId)
    if ($excelProcessId -eq 0 -or $existing -contains $excelProcessId) { throw 'Excel COM did not create an identifiable dedicated process; capture aborted.' }
    $ownedExcel = $true
    $owned = Get-Process -Id $excelProcessId
    @{ pid=$excelProcessId; startTicks=$owned.StartTime.ToUniversalTime().Ticks.ToString() } | ConvertTo-Json | Set-Content $statePath -Encoding UTF8
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AskToUpdateLinks = $false
    $excel.EnableEvents = $false
    $excel.AutomationSecurity = 3
    $workbooks = $excel.Workbooks
    foreach ($file in $inputs.files) {
        if ($file.id -notmatch '^[a-z0-9-]+$' -or $file.file -ne ($file.id + '.xlsx')) { throw 'Invalid generated file name.' }
        $sourcePath = Join-Path $InputDirectory $file.file
        if ((Get-FileHash $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.inputSha256) { throw "Input hash mismatch: $($file.id)" }
        $zip = [IO.Compression.ZipFile]::OpenRead($sourcePath)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName -match '(?i)vba|externalLinks|connections|embeddings|activeX') { throw 'Only generated macro-free workbooks without external content are accepted.' }
            }
        } finally { $zip.Dispose() }
        $savedPath = Join-Path $OutputDirectory $file.file
        if (Test-Path $savedPath) { throw "Output already exists: $savedPath" }
        Copy-Item $sourcePath $savedPath
        try {
            $book = $workbooks.Open($savedPath, 0, $false)
            $compatibility = 'unavailable'
            try { $book.CompatibilityVersion = 1; $compatibility = $book.CompatibilityVersion.ToString() } catch { }
            $excel.CalculateFullRebuild()
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            while ($excel.CalculationState -ne 0) {
                if ([DateTime]::UtcNow -gt $deadline) { throw 'Excel calculation did not finish.' }
                Start-Sleep -Milliseconds 100
            }
            if ([bool]$book.Date1904 -ne [bool]$file.date1904) { throw 'Excel changed the workbook date system.' }
            $book.Save()
            $observations += @{
                id=$file.id; excelVersion=$excel.Version.ToString(); excelBuild=$excel.Build.ToString()
                os=[Environment]::OSVersion.VersionString; capturedAt=[DateTime]::UtcNow.ToString('o')
                culture=[Globalization.CultureInfo]::CurrentCulture.Name; date1904=[bool]$book.Date1904
                compatibilityVersion=$compatibility; inputSha256=$file.inputSha256
                savedSha256=(Get-FileHash $savedPath -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        } finally {
            if ($book) { $book.Close($false); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book); $book=$null }
        }
    }
    @{ source='excel-observed'; caseSha256=$inputs.caseSha256; observations=@($observations) } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputDirectory 'manifest.json') -Encoding UTF8
} finally {
    if ($book) { try { $book.Close($false) } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($book) } }
    if ($workbooks) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($workbooks) }
    if ($excel) {
        if ($ownedExcel) { $excel.Quit() }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel)
    }
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
}
