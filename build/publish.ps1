<#
.SYNOPSIS
    Builds the CONVERTit release: convertit_v<version>_portable.zip (+ .sha256) and, if Inno Setup is installed,
    convertit_v<version>_setup.exe. FFmpeg is not bundled (downloaded in the app).

.DESCRIPTION
    1. Publishes the app self-contained for win-x64 into artifacts\publish\CONVERTit
    2. Copies the third-party notice of Magick.NET from the NuGet package into licenses\third-party
    3. Checks that every bundled component has a license file
    4. Packs the portable ZIP (folder convertit_v<version>, with CONVERTit.portable marker) and writes the SHA-256
    5. Compiles build\installer\convertit.iss with ISCC (skip with -SkipInstaller)

.EXAMPLE
    pwsh -File build\publish.ps1
    pwsh -File build\publish.ps1 -SkipInstaller
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [switch] $SkipInstaller
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
[xml] $props = Get-Content -Raw -Path (Join-Path $root 'Directory.Build.props')
$versionNode = $props.SelectSingleNode('//Version')
if ($null -eq $versionNode) { throw 'Version not found in Directory.Build.props.' }
$version = $versionNode.InnerText.Trim()

$name = "convertit_v$version"
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts 'publish\CONVERTit'
$portableRoot = Join-Path $artifacts "$($name)_portable"
$zip = Join-Path $artifacts "$($name)_portable.zip"

Write-Host "CONVERTit $version · $Configuration · $Runtime" -ForegroundColor Cyan

foreach ($dir in (Split-Path $publishDir), $portableRoot) {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish (Join-Path $root 'src\CONVERTit.App\CONVERTit.App.csproj') -c $Configuration -r $Runtime --self-contained true `
    -p:PublishReadyToRun=false -p:DebugType=none -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publishing CONVERTit.App failed.' }

# Third-party notice of Magick.NET (all native libraries with their licenses) from the NuGet package.
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$magickPackage = Join-Path $nugetRoot 'magick.net-q8-x64'
$notice = Get-ChildItem -Path $magickPackage -Recurse -Include 'Notice.txt', 'NOTICE*' -ErrorAction SilentlyContinue | Select-Object -First 1
$noticeTarget = Join-Path $publishDir 'licenses\third-party\MAGICK-NET-NOTICE.txt'
if ($null -ne $notice) {
    Copy-Item $notice.FullName $noticeTarget -Force
    Write-Host "Magick.NET notice: $($notice.FullName)"
}
else {
    Write-Warning 'Magick.NET Notice.txt not found in the NuGet cache – add licenses\third-party\MAGICK-NET-NOTICE.txt manually.'
}

# Every bundled component needs its license file.
foreach ($file in @('LICENSE.md', 'third-party\MAGICK-NET.md', 'third-party\LIBVLC.md', 'third-party\AVALONIA.md', 'third-party\DOTNET.md',
                    'third-party\LICENSE-LGPL-2.1.md', 'third-party\LICENSE-APACHE-2.0.md')) {
    if (-not (Test-Path (Join-Path $publishDir "licenses\$file"))) { throw "License file missing in the package: $file" }
}

# Portable ZIP: same files plus the marker (data in .\data).
$staging = Join-Path $portableRoot $name
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Copy-Item -Path (Join-Path $publishDir '*') -Destination $staging -Recurse
Set-Content -Path (Join-Path $staging 'CONVERTit.portable') -Value 'Portable mode: data is stored in .\data' -Encoding ascii

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $staging -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path -Leaf $zip)" -Encoding ascii
Write-Host "Portable: $zip" -ForegroundColor Green
Write-Host "SHA-256:  $hash"

# Installer (Inno Setup 6).
if (-not $SkipInstaller) {
    $iscc = @(
        (Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

    if ($null -eq $iscc) {
        Write-Warning 'Inno Setup 6 (ISCC.exe) not found – installer skipped. https://jrsoftware.org/isinfo.php'
    }
    else {
        & $iscc "/DAppVersion=$version" "/DSourceDir=$publishDir" "/DOutputDir=$artifacts" (Join-Path $PSScriptRoot 'installer\convertit.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
        $setup = Join-Path $artifacts "$($name)_setup.exe"
        $setupHash = (Get-FileHash -Path $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -Path "$setup.sha256" -Value "$setupHash  $(Split-Path -Leaf $setup)" -Encoding ascii
        Write-Host "Setup:    $setup" -ForegroundColor Green
        Write-Host "SHA-256:  $setupHash"
    }
}
