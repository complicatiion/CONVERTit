<#
.SYNOPSIS
    Restore, build, test and package CONVERTit in one go (used by CI and before a release).
#>
[CmdletBinding()]
param([switch] $SkipInstaller)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'CONVERTit.slnx'

dotnet restore $solution
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
dotnet build $solution -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet test $solution -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

& (Join-Path $PSScriptRoot 'publish.ps1') -SkipInstaller:$SkipInstaller
