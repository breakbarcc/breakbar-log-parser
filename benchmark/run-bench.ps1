<#
.SYNOPSIS
  Elite Insights benchmark for Windows (PowerShell 5.1+).

.DESCRIPTION
  Runs the EI CLI once per log (one process per log, like a worker job) in three batches:
    A  concurrency 1, SingleThreaded=false  (one log uses all cores)
    B  concurrency 1, SingleThreaded=true   (baseline per log on one core)
    C  concurrency 2, SingleThreaded=true   (two logs in parallel)
  Measures per log: wall time, EI "elapsed", peak working set, output sizes.
  Measures per batch: total wall time, logs/hour, peak combined working set.
  Results go to results\<host>-<timestamp>\ as CSV files.

.EXAMPLE
  .\run-bench.ps1 -EiPath C:\Tools\GW2EICLI\GuildWars2EliteInsights-CLI.exe
.EXAMPLE
  .\run-bench.ps1 -EiPath C:\Tools\GW2EICLI\GuildWars2EliteInsights-CLI.dll -NoCombatReplay -Repeat 2
#>
param(
    [Parameter(Mandatory = $true)] [string]$EiPath,
    [string]$LogDir = (Join-Path $PSScriptRoot 'logs'),
    [string]$ResultRoot = (Join-Path $PSScriptRoot 'results'),
    [int]$Repeat = 1,
    [switch]$NoCombatReplay,
    [string[]]$Batches = @('A', 'B', 'C'),
    [string]$Dotnet = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ---------- setup ----------
$EiPath = (Resolve-Path $EiPath).Path
# dotnet host: -Dotnet, else a local install in benchmark\.dotnet, else the global one
if (-not $Dotnet) {
    $localDotnet = Join-Path $PSScriptRoot '.dotnet\dotnet.exe'
    if (Test-Path $localDotnet) { $Dotnet = $localDotnet } else { $Dotnet = 'dotnet' }
}
if ($EiPath.ToLower().EndsWith('.dll')) {
    $exe = $Dotnet
    $argPrefix = '"' + $EiPath + '" '
} else {
    $exe = $EiPath
    $argPrefix = ''
}

$templatePath = Join-Path $PSScriptRoot 'ei-bench.template.conf'
$template = Get-Content -Raw -Path $templatePath
$cr = 'true'
if ($NoCombatReplay) { $cr = 'false' }

$logs = @(Get-ChildItem -Path $LogDir -File | Where-Object {
    $n = $_.Name.ToLower()
    $n.EndsWith('.evtc') -or $n.EndsWith('.zevtc') -or $n.EndsWith('.evtc.zip')
} | Sort-Object Name)
if ($logs.Count -eq 0) { throw "No .evtc/.zevtc/.evtc.zip files found in $LogDir" }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runRoot = Join-Path $ResultRoot ("{0}-{1}" -f $env:COMPUTERNAME, $stamp)
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$perLogCsv = Join-Path $runRoot 'per-log.csv'
$batchCsv = Join-Path $runRoot 'batches.csv'

# CSV numbers always with "." as decimal separator, independent of the Windows locale
function Num($x) { return ([double]$x).ToString('0.##', [System.Globalization.CultureInfo]::InvariantCulture) }

function Get-EvtcBytes([System.IO.FileInfo]$f) {
    if ($f.Name.ToLower().EndsWith('.evtc')) { return $f.Length }
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($f.FullName)
        try { return ($zip.Entries | Measure-Object -Property Length -Sum).Sum }
        finally { $zip.Dispose() }
    } catch { return '' }
}

# ---------- system info ----------
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$os = Get-CimInstance Win32_OperatingSystem
$eiVersion = ''
try {
    if ($argPrefix) { $vOut = & $exe $EiPath -v 2>$null } else { $vOut = & $exe -v 2>$null }
    $eiVersion = ($vOut | Where-Object { $_ -match '^v\d' } | Select-Object -Last 1)
} catch {}
@(
    "host=$env:COMPUTERNAME"
    "os=$($os.Caption) $($os.Version)"
    "cpu=$($cpu.Name)"
    "logical_cores=$([Environment]::ProcessorCount)"
    "ram_mb=$([math]::Round($os.TotalVisibleMemorySize / 1024))"
    "ei_path=$EiPath"
    "ei_version=$eiVersion"
    "combat_replay=$cr"
    "repeat=$Repeat"
    "logs=$($logs.Count)"
) | Set-Content -Path (Join-Path $runRoot 'system.txt') -Encoding ASCII

'batch,repeat,file,size_bytes,evtc_bytes,single_threaded,concurrency,exit_code,parsed,ei_elapsed_ms,wall_s,peak_ws_mb,html_bytes,json_bytes,out_total_bytes' |
    Set-Content -Path $perLogCsv -Encoding ASCII
'batch,repeat,concurrency,single_threaded,logs,ok,total_wall_s,logs_per_hour,combined_peak_mb' |
    Set-Content -Path $batchCsv -Encoding ASCII

# ---------- run helpers ----------
function Start-Ei($log, [bool]$single, [string]$runDir) {
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $conf = $template.Replace('__OUT__', $runDir).Replace('__SINGLE__', $single.ToString().ToLower()).Replace('__CR__', $cr)
    $confPath = Join-Path $runDir 'bench.conf'
    Set-Content -Path $confPath -Value $conf -Encoding UTF8

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.Arguments = $argPrefix + '-c "' + $confPath + '" "' + $log.FullName + '"'
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = $runDir
    $psi.EnvironmentVariables['DOTNET_gcServer'] = '0'

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    return @{
        Log = $log; Single = $single; RunDir = $runDir; Proc = $p; Sw = $sw; Peak = [long]0
        OutTask = $p.StandardOutput.ReadToEndAsync(); ErrTask = $p.StandardError.ReadToEndAsync()
    }
}

function Complete-Ei($r, [string]$batch, [int]$rep, [int]$conc) {
    $r.Proc.WaitForExit()
    $r.Sw.Stop()
    $stdout = $r.OutTask.Result
    Set-Content -Path (Join-Path $r.RunDir 'stdout.txt') -Value $stdout -Encoding UTF8
    Set-Content -Path (Join-Path $r.RunDir 'stderr.txt') -Value $r.ErrTask.Result -Encoding UTF8

    $parsed = ''; $elapsed = ''
    $line = ($stdout -split "`r?`n" | Where-Object { $_.StartsWith('Processed - ') } | Select-Object -First 1)
    if ($line) {
        try {
            $obj = $line.Substring(12) | ConvertFrom-Json
            $parsed = $obj.parsed; $elapsed = $obj.elapsed
        } catch {}
    }
    $files = @(Get-ChildItem -Path $r.RunDir -File -Recurse)
    $html = ($files | Where-Object { $_.Extension -eq '.html' } | Measure-Object -Property Length -Sum).Sum
    $json = ($files | Where-Object { $_.Name -match '\.json(\.gz)?$' } | Measure-Object -Property Length -Sum).Sum
    $total = ($files | Where-Object { $_.Name -notin @('bench.conf', 'stdout.txt', 'stderr.txt') } | Measure-Object -Property Length -Sum).Sum

    $row = @(
        $batch, $rep, $r.Log.Name, $r.Log.Length, (Get-EvtcBytes $r.Log), $r.Single.ToString().ToLower(), $conc,
        $r.Proc.ExitCode, "$parsed".ToLower(), $elapsed, (Num $r.Sw.Elapsed.TotalSeconds),
        [math]::Round($r.Peak / 1MB), [long]$html, [long]$json, [long]$total
    ) -join ','
    Add-Content -Path $perLogCsv -Value $row -Encoding ASCII
    Write-Host ("  {0,-45} exit={1} parsed={2,-5} wall={3,7}s peak={4,6} MB" -f $r.Log.Name, $r.Proc.ExitCode, $parsed, [math]::Round($r.Sw.Elapsed.TotalSeconds, 1), [math]::Round($r.Peak / 1MB))
    return ($parsed -eq $true)
}

function Invoke-Batch([string]$batch, [int]$rep, [int]$conc, [bool]$single) {
    Write-Host ("Batch {0} (repeat {1}): concurrency={2} SingleThreaded={3}" -f $batch, $rep, $conc, $single)
    $queue = New-Object System.Collections.Queue
    foreach ($l in $logs) { $queue.Enqueue($l) }
    $running = New-Object System.Collections.Generic.List[object]
    $ok = 0; $combinedPeak = [long]0; $i = 0
    $batchSw = [System.Diagnostics.Stopwatch]::StartNew()

    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        while ($running.Count -lt $conc -and $queue.Count -gt 0) {
            $i++
            $runDir = Join-Path $runRoot ("out\{0}-r{1}\{2:D3}" -f $batch, $rep, $i)
            $running.Add((Start-Ei $queue.Dequeue() $single $runDir))
        }
        $combined = [long]0
        foreach ($r in $running) {
            try {
                $r.Proc.Refresh()
                if (-not $r.Proc.HasExited) {
                    $combined += $r.Proc.WorkingSet64
                    if ($r.Proc.PeakWorkingSet64 -gt $r.Peak) { $r.Peak = $r.Proc.PeakWorkingSet64 }
                }
            } catch {}
        }
        if ($combined -gt $combinedPeak) { $combinedPeak = $combined }
        foreach ($r in $running.ToArray()) {
            if ($r.Proc.HasExited) {
                if (Complete-Ei $r $batch $rep $conc) { $ok++ }
                [void]$running.Remove($r)
            }
        }
        Start-Sleep -Milliseconds 100
    }
    $batchSw.Stop()
    $secs = $batchSw.Elapsed.TotalSeconds
    $perHour = 0
    if ($secs -gt 0) { $perHour = [math]::Round($logs.Count * 3600 / $secs, 1) }
    $row = @($batch, $rep, $conc, $single.ToString().ToLower(), $logs.Count, $ok, (Num $secs), (Num $perHour),[math]::Round($combinedPeak / 1MB)) -join ','
    Add-Content -Path $batchCsv -Value $row -Encoding ASCII
    Write-Host ("  -> total {0}s, {1} logs/h, combined peak {2} MB" -f [math]::Round($secs, 1), $perHour, [math]::Round($combinedPeak / 1MB))
}

# ---------- main ----------
$batchList = @($Batches | ForEach-Object { $_.Split(',') } | ForEach-Object { $_.Trim().ToUpper() })
Write-Host "Logs: $($logs.Count) | Results: $runRoot"
for ($rep = 1; $rep -le $Repeat; $rep++) {
    if ($batchList -contains 'A') { Invoke-Batch 'A' $rep 1 $false }
    if ($batchList -contains 'B') { Invoke-Batch 'B' $rep 1 $true }
    if ($batchList -contains 'C') { Invoke-Batch 'C' $rep 2 $true }
}
Write-Host "Done. Send these files: $perLogCsv, $batchCsv, $(Join-Path $runRoot 'system.txt')"
