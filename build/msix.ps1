<#
.SYNOPSIS
  Builds the Microsoft Store package (MSIX) of Mo Converter.

.DESCRIPTION
  1. publishes the program self-contained (with FFmpeg and the license files),
  2. makes the tile and logo images from src\Mo.App\Assets\app-source.png,
  3. fills build\msix\AppxManifest.template.xml,
  4. packs artifacts\msix\MoConverter_<version>_x64.msix with makeappx.

  The package is NOT signed by default: for the Store, upload it as it is; Microsoft signs it.
  -SignForTesting additionally signs a copy with a throw-away certificate made on the spot, so the package can be
  looked at with signtool. Installing that copy needs the .cer to be trusted on the PC (not done here).

  IdentityName, Publisher and PublisherDisplayName MUST be the values Partner Center shows under
  "Product management > Product identity" after the name "Mo Converter" is reserved. The defaults only let the
  package build.

  makeappx and signtool come from the NuGet package Microsoft.Windows.SDK.BuildTools (Windows SDK license:
  https://aka.ms/WinSDKLicenseURL). They are used at build time only and are not shipped.

.EXAMPLE
  ./build/msix.ps1 -Version 1.0.0 -IdentityName 12345Mohamad.MoConverter -Publisher 'CN=ABCD1234-...' -PublisherDisplayName 'Mohamad Mourad'
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$IdentityName = 'MohamadMourad.MoConverter',
    [string]$Publisher = 'CN=Mohamad Mourad',
    [string]$PublisherDisplayName = 'Mohamad Mourad',
    [switch]$SignForTesting
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dotnet = if ($env:DOTNET_EXE) { $env:DOTNET_EXE } else { 'dotnet' }
$work = Join-Path $root 'artifacts\msix'
$layout = Join-Path $work 'layout'
$msix = Join-Path $work "MoConverter_${Version}_x64.msix"

function Invoke-Step([string]$name, [scriptblock]$step) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $step
    if ($LASTEXITCODE) { throw "$name failed (exit code $LASTEXITCODE)" }
}

Push-Location $root
try {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force $layout | Out-Null

    if (-not (Test-Path 'tools\ffmpeg\bin\ffmpeg.exe')) { Invoke-Step 'Fetch FFmpeg' { & (Join-Path $PSScriptRoot 'get-ffmpeg.ps1') } }

    Invoke-Step 'Restore packaging tools' { & $dotnet restore build\msix\Msix.Tools.csproj -nologo -v:q }
    $packages = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    $makeappx = (Get-ChildItem $packages -Recurse -Filter makeappx.exe | Where-Object FullName -match '\\x64\\' | Select-Object -First 1).FullName
    $signtool = (Get-ChildItem $packages -Recurse -Filter signtool.exe | Where-Object FullName -match '\\x64\\' | Select-Object -First 1).FullName
    if (-not $makeappx) { throw 'makeappx.exe not found after restore.' }

    Invoke-Step 'Publish (self-contained, win-x64)' {
        & $dotnet publish src\Mo.App\Mo.App.csproj -c Release -r win-x64 --self-contained -p:Version=$Version -o $layout -nologo -v:q
    }

    # ---- tile and logo images, drawn from the app icon ----
    Write-Host '== Images' -ForegroundColor Cyan
    Add-Type -AssemblyName System.Drawing
    $assets = New-Item -ItemType Directory -Force (Join-Path $layout 'Assets')
    $source = [System.Drawing.Image]::FromFile((Join-Path $root 'src\Mo.App\Assets\app-source.png'))
    function New-Tile([string]$name, [int]$width, [int]$height) {
        $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bitmap)
        $g.InterpolationMode = 'HighQualityBicubic'; $g.SmoothingMode = 'AntiAlias'
        $g.Clear([System.Drawing.Color]::Transparent)
        $side = [int]([Math]::Min($width, $height) * 0.9)   # the icon, centred, with a little air around it
        $g.DrawImage($source, [int](($width - $side) / 2), [int](($height - $side) / 2), $side, $side)
        $g.Dispose()
        $bitmap.Save((Join-Path $assets.FullName $name), [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }
    New-Tile 'StoreLogo.png' 50 50
    New-Tile 'Square44x44Logo.png' 44 44
    New-Tile 'Square150x150Logo.png' 150 150
    New-Tile 'Wide310x150Logo.png' 310 150
    New-Tile 'SmallTile.png' 71 71
    New-Tile 'LargeTile.png' 310 310
    $source.Dispose()

    # ---- manifest ----
    Write-Host '== Manifest' -ForegroundColor Cyan
    $text = Get-Content (Join-Path $PSScriptRoot 'msix\AppxManifest.template.xml') -Raw -Encoding UTF8
    $text = $text.Replace('@IDENTITY_NAME@', $IdentityName)
    $text = $text.Replace('@PUBLISHER@', [Security.SecurityElement]::Escape($Publisher))
    $text = $text.Replace('@PUBLISHER_DISPLAY_NAME@', [Security.SecurityElement]::Escape($PublisherDisplayName))
    $text = $text.Replace('@VERSION@', "$Version.0")
    if ($text -match '@[A-Z_]+@') { throw "Unfilled placeholder in the manifest: $($Matches[0])" }
    [IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $text, (New-Object Text.UTF8Encoding($false)))

    Invoke-Step 'Pack' { & $makeappx pack /d $layout /p $msix /o /nv }
    # (/nv skips the Windows App Certification checks that need the full SDK; Partner Center runs them on upload.)

    if ($SignForTesting) {
        Write-Host '== Sign a copy with a throw-away certificate' -ForegroundColor Cyan
        $rsa = [System.Security.Cryptography.RSA]::Create(2048)
        $request = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest($Publisher, $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $true)))
        $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension('DigitalSignature', $true)))
        $usages = New-Object System.Security.Cryptography.OidCollection
        [void]$usages.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
        $request.CertificateExtensions.Add((New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($usages, $true)))
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(1))
        $password = [Guid]::NewGuid().ToString('N')
        $pfx = Join-Path $work 'test-signing.pfx'
        [IO.File]::WriteAllBytes($pfx, $certificate.Export('Pfx', $password))
        [IO.File]::WriteAllBytes((Join-Path $work 'test-signing.cer'), $certificate.Export('Cert'))
        $signed = Join-Path $work "MoConverter_${Version}_x64.signed-test.msix"
        Copy-Item $msix $signed
        Invoke-Step 'signtool sign' { & $signtool sign /fd SHA256 /f $pfx /p $password $signed }
        Remove-Item $pfx -Force   # the private key is not kept
    }

    Write-Host "Done: $msix  ($([math]::Round((Get-Item $msix).Length / 1MB, 1)) MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
