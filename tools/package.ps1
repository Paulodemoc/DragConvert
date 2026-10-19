<#
.SYNOPSIS
    Builds the portable release: a self-contained, single-file DragConvert.exe plus the LGPL FFmpeg, zipped.
.DESCRIPTION
    Output (in artifacts/release):
      DragConvert-<version>-win-x64.zip
        DragConvert.exe              self-contained .NET 10 app (no runtime install needed)
        engines/ffmpeg.exe           LGPL FFmpeg build, run as a separate process
        LICENSE.txt, THIRD-PARTY-NOTICES.md
        licenses/FFMPEG-LICENSE.txt, licenses/FFMPEG-SOURCE.txt
      DragConvert-<version>-win-x64.zip.sha256
    Run tools/fetch-ffmpeg.ps1 first.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'artifacts/release'
$name = "DragConvert-$Version-$Runtime"
$stage = Join-Path $out $name
$ffmpeg = Join-Path $root 'tools/bin/ffmpeg.exe'
if (-not (Test-Path $ffmpeg)) { throw 'FFmpeg missing: run tools/fetch-ffmpeg.ps1 first' }

Remove-Item $stage, "$stage.zip", "$stage.zip.sha256" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path (Join-Path $stage 'engines'), (Join-Path $stage 'licenses') -Force | Out-Null

dotnet publish (Join-Path $root 'src/DragConvert.App/DragConvert.App.csproj') `
    --configuration Release --runtime $Runtime --self-contained `
    -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none --output $stage
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Copy-Item $ffmpeg (Join-Path $stage 'engines')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $stage
Copy-Item (Join-Path $root 'tools/bin/FFMPEG-LICENSE.txt') (Join-Path $stage 'licenses')

# LGPL: say exactly which FFmpeg this is and where its source and build scripts are.
$ffmpegVersion = (& $ffmpeg -hide_banner -version | Select-Object -First 3) -join [Environment]::NewLine
@"
DragConvert runs FFmpeg (engines/ffmpeg.exe) as a separate program. It is licensed under the
GNU Lesser General Public License v2.1 or later; see FFMPEG-LICENSE.txt.

This build:
$ffmpegVersion

It is the "lgpl" variant from https://github.com/BtbN/FFmpeg-Builds (no GPL components).
Source code: https://github.com/FFmpeg/FFmpeg (the version string above names the exact commit)
Build scripts and the sources of the bundled libraries: https://github.com/BtbN/FFmpeg-Builds

You may replace engines/ffmpeg.exe with any other FFmpeg build.
"@ | Set-Content (Join-Path $stage 'licenses/FFMPEG-SOURCE.txt') -Encoding utf8

Compress-Archive -Path (Join-Path $stage '*') -DestinationPath "$stage.zip" -CompressionLevel Optimal
$hash = (Get-FileHash "$stage.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content "$stage.zip.sha256" -Encoding ascii
Write-Host "Packaged $stage.zip ($([math]::Round((Get-Item "$stage.zip").Length / 1MB)) MB), SHA-256 $hash"
