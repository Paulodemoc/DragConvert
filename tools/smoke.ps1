<#
.SYNOPSIS
    Runs DragConvert.exe --smoke and fails if it doesn't exit cleanly within the timeout.
.DESCRIPTION
    --smoke starts the app fully, converts a tiny generated image and exits. It exits with 2 if another
    DragConvert is already running (it would otherwise hand off and test nothing).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Exe,
    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
$p = Start-Process -FilePath $Exe -ArgumentList '--smoke' -PassThru
if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
    $p.Kill()
    throw "DragConvert did not exit within $TimeoutSeconds s"
}

$log = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'DragConvert\logs') -Filter *.log -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime | Select-Object -Last 1
if ($log) { Get-Content $log.FullName -Tail 15 }

switch ($p.ExitCode) {
    0 { Write-Host "Smoke run passed: $Exe" }
    2 { throw 'Another DragConvert is running; close it and retry' }
    default { throw "DragConvert exited with code $($p.ExitCode)" }
}
