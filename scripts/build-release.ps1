<#
.SYNOPSIS
  Runs the tests and builds the Playnite add-on package into .\dist:
    VerticalCoverFixer_<version>.pext
  Used both locally and by GitHub Actions.

.EXAMPLE
  ./scripts/build-release.ps1                  # version 0.0.0
  ./scripts/build-release.ps1 -Version 1.2.0
#>
param(
    [string]$Version = $(if ($env:GITHUB_REF_NAME -match '^v(\d+\.\d+\.\d+)') { $Matches[1] } else { '0.0.0' })
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$work = Join-Path $root 'obj\release'

Remove-Item $dist, $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item $dist, $work -ItemType Directory | Out-Null

Write-Host "==> Running tests" -ForegroundColor Cyan
dotnet test "$root\tests\VerticalCoverFixer.Tests" -c Release
if ($LASTEXITCODE) { throw 'Tests failed' }

Write-Host "==> Building Playnite add-on $Version" -ForegroundColor Cyan
dotnet build "$root\src\VerticalCoverFixer" -c Release -p:Version=$Version -o "$work\addon"
if ($LASTEXITCODE) { throw 'Add-on build failed' }

# Stamp the version into the manifest so Playnite shows the right number.
$manifest = "$work\addon\extension.yaml"
(Get-Content $manifest) -replace '^Version:.*$', "Version: $Version" | Set-Content $manifest -Encoding utf8

# A .pext is a zip of the add-on folder. Only ship what Playnite needs.
$pextDir = "$work\pext"
New-Item $pextDir -ItemType Directory | Out-Null
Copy-Item "$work\addon\VerticalCoverFixer.dll", $manifest, "$work\addon\icon.png" $pextDir
Copy-Item "$root\LICENSE" "$pextDir\LICENSE.txt"   # MIT: the license must travel with every copy
Compress-Archive -Path "$pextDir\*" -DestinationPath "$work\VerticalCoverFixer.zip"
Move-Item "$work\VerticalCoverFixer.zip" "$dist\VerticalCoverFixer_$Version.pext"

Write-Host "==> Done. Files in ${dist}:" -ForegroundColor Green
Get-ChildItem $dist | Format-Table Name, Length
