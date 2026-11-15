<#
.SYNOPSIS
  Downloads the pinned LGPL build of FFmpeg into tools\ffmpeg (bin + LICENSE.txt) and checks its SHA-256.

  Why this build:
  - LGPL-3.0 ("lgpl" variant): no GPL parts (x264, x265 are disabled), so Mo Converter keeps its own license.
  - "shared": FFmpeg's libraries are separate DLLs that users may replace, as the LGPL asks.
  - Pinned to a dated autobuild so every build of Mo Converter ships the same FFmpeg.
  Source code of this exact FFmpeg version: https://github.com/FFmpeg/FFmpeg/tree/d838f9f9ae (n9.0.2-57-gd838f9f9ae).
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$tag = 'autobuild-2026-11-14-12-37'
$name = 'ffmpeg-n9.0.2-57-gd838f9f9ae-win64-lgpl-shared-9.0.zip'
$sha256 = 'cdb9dd9f94bc3958dd0d7ba39ec61b8e9d0a68351bf2f8be22178bd8e0a5aa91'
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/$tag/$name"

$target = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\ffmpeg'
$work = Join-Path ([IO.Path]::GetTempPath()) ('mo-ffmpeg-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $zip = Join-Path $work $name
    Write-Host "Downloading $name"
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing -Headers @{ 'User-Agent' = 'mo-converter-build' }
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { throw "Checksum mismatch: expected $sha256, got $actual" }

    Expand-Archive $zip -DestinationPath $work
    $root = Get-ChildItem $work -Directory | Select-Object -First 1
    New-Item -ItemType Directory -Force (Join-Path $target 'bin') | Out-Null
    # ffplay is a media player we do not need.
    Get-ChildItem (Join-Path $root.FullName 'bin') | Where-Object Name -ne 'ffplay.exe' |
        Copy-Item -Destination (Join-Path $target 'bin') -Force
    Copy-Item (Join-Path $root.FullName 'LICENSE.txt') (Join-Path $target 'LICENSE.txt') -Force
    & (Join-Path $target 'bin\ffmpeg.exe') -hide_banner -version | Select-Object -First 1
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
