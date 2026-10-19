<#
.SYNOPSIS
    Downloads an LGPL FFmpeg build (ffmpeg.exe, ffprobe.exe) into tools/bin for development and tests.
.DESCRIPTION
    Uses the BtbN FFmpeg-Builds "lgpl" variant: no GPL components (so no x264/x265; MP4 uses the h264_mf
    encoder backed by Windows). The archive's SHA-256 is checked against the release's checksums.sha256.
    Development tooling only; the app itself never downloads anything.
#>
[CmdletBinding()]
param(
    [string] $Version = '8.1',
    [string] $Destination = (Join-Path $PSScriptRoot 'bin'),
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ffmpegExe = Join-Path $Destination 'ffmpeg.exe'
if ((Test-Path $ffmpegExe) -and -not $Force) {
    Write-Host "FFmpeg already present: $ffmpegExe (use -Force to re-download)"
    & $ffmpegExe -hide_banner -version | Select-Object -First 1
    return
}

$release = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest'
$assetName = "ffmpeg-n$Version-latest-win64-lgpl-$Version.zip"
$work = Join-Path ([System.IO.Path]::GetTempPath()) "dragconvert-ffmpeg-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null

try {
    $zip = Join-Path $work $assetName
    $checksums = Join-Path $work 'checksums.sha256'
    Write-Host "Downloading $assetName ..."
    Invoke-WebRequest "$release/$assetName" -OutFile $zip
    Invoke-WebRequest "$release/checksums.sha256" -OutFile $checksums

    $expected = (Get-Content $checksums | Where-Object { $_ -match [regex]::Escape($assetName) } | Select-Object -First 1) -split '\s+' | Select-Object -First 1
    if (-not $expected) { throw "No checksum listed for $assetName" }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
    if ($actual -ne $expected.ToUpperInvariant()) { throw "Checksum mismatch for $assetName (expected $expected, got $actual)" }
    Write-Host "Checksum OK ($actual)"

    Expand-Archive $zip -DestinationPath $work
    $root = Get-ChildItem $work -Directory | Where-Object Name -like 'ffmpeg-*' | Select-Object -First 1
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item (Join-Path $root.FullName 'bin\ffmpeg.exe'), (Join-Path $root.FullName 'bin\ffprobe.exe') $Destination -Force
    Copy-Item (Join-Path $root.FullName 'LICENSE.txt') (Join-Path $Destination 'FFMPEG-LICENSE.txt') -Force -ErrorAction SilentlyContinue

    & $ffmpegExe -hide_banner -version | Select-Object -First 1
    Write-Host "Installed to $Destination"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
