<#
.SYNOPSIS
  Builds a Mo Converter release with Velopack (installer, portable zip, full and delta update packages).

.EXAMPLE
  ./build/release.ps1 -Version 0.2.0
  Builds into artifacts\releases. Nothing is uploaded.

.EXAMPLE
  $env:GITHUB_TOKEN = '<token with contents:write on the repository>'
  ./build/release.ps1 -Version 0.2.0 -Upload
  Also uploads a DRAFT release to GitHub. Installed copies see the update only after the draft is published
  on GitHub (or when -Publish is passed). From then on every older copy must update.
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$Upload,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repo = 'https://github.com/mohamadmourad91/Mo-Converter'
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$publishDir = Join-Path $root 'artifacts\publish'
$releaseDir = Join-Path $root 'artifacts\releases'

function Invoke-Step([string]$name, [scriptblock]$step) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE) { throw "$name failed (exit code $LASTEXITCODE)" }
}

Push-Location $root
try {
    Invoke-Step 'Restore vpk' { & $dotnet tool restore }

    # Video and audio need FFmpeg next to the program; fetch the pinned LGPL build if it is not there yet.
    if (-not (Test-Path (Join-Path $root 'tools\ffmpeg\bin\ffmpeg.exe'))) {
        Invoke-Step 'Fetch FFmpeg' { & (Join-Path $PSScriptRoot 'get-ffmpeg.ps1') }
    }

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Step 'Publish (self-contained, win-x64)' {
        & $dotnet publish src\Mo.App\Mo.App.csproj -c Release -r win-x64 --self-contained -p:Version=$Version -o $publishDir
    }

    # The previous release is needed to build a small delta package; the first release has none.
    Invoke-Step 'Fetch previous release' {
        & $dotnet vpk download github --repoUrl $repo -o $releaseDir
    }

    Invoke-Step 'Pack' {
        & $dotnet vpk pack --packId MoConverter --packVersion $Version --packDir $publishDir `
            --mainExe 'Mo Converter.exe' --packTitle 'Mo Converter' --icon (Join-Path $root 'src\Mo.App\Assets\app.ico') --packAuthors 'Mohamad Mourad' -o $releaseDir
    }

    if ($Upload) {
        if (-not $env:GITHUB_TOKEN) { throw 'Set $env:GITHUB_TOKEN before using -Upload.' }
        $uploadArgs = @('vpk', 'upload', 'github', '--repoUrl', $repo, '--token', $env:GITHUB_TOKEN, '-o', $releaseDir,
            '--releaseName', "Mo Converter $Version", '--tag', "v$Version")
        if ($Publish) { $uploadArgs += '--publish' }
        Invoke-Step 'Upload to GitHub' { & $dotnet @uploadArgs }
    }

    Write-Host "Done: $releaseDir" -ForegroundColor Green
}
finally {
    Pop-Location
}
