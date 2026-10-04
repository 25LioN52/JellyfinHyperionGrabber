<#
.SYNOPSIS
    Builds the Jellyfin plugin repository manifest (manifest.json) from per-release version entries.

.DESCRIPTION
    Every GitHub release carries a manifest-versions.json asset written by build/package.ps1. The docs workflow
    downloads them all into one folder and calls this script, so the published manifest is always derived from the
    releases themselves and never edited by hand.

    Works on PowerShell 7 (CI) and Windows PowerShell 5.1.

.EXAMPLE
    ./build/New-PluginManifest.ps1 -VersionsDirectory release-manifests -OutputPath site/manifest.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VersionsDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$metadata = Get-Content (Join-Path $PSScriptRoot 'plugin.json') -Raw | ConvertFrom-Json
$versions = @()
if (Test-Path $VersionsDirectory) {
    foreach ($file in Get-ChildItem $VersionsDirectory -Filter '*.json' -Recurse) {
        # foreach (not @()) so Windows PowerShell 5.1, which emits a JSON array as one object, also flattens it.
        foreach ($entry in (Get-Content $file.FullName -Raw | ConvertFrom-Json)) {
            $versions += $entry
        }
    }
}

$versions = @($versions | Sort-Object -Property @{ Expression = { [version] $_.version } } -Descending)
$manifest = @()
if ($versions.Count -gt 0) {
    $manifest = @([ordered]@{
            guid        = $metadata.guid
            name        = $metadata.name
            description = $metadata.description
            overview    = $metadata.overview
            owner       = $metadata.owner
            category    = $metadata.category
            versions    = $versions
        })
}

$outputFile = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
New-Item -ItemType Directory -Force (Split-Path -Parent $outputFile) | Out-Null
$json = if ($manifest.Count -eq 0) { '[]' } else { ConvertTo-Json -InputObject $manifest -Depth 8 }
[System.IO.File]::WriteAllText($outputFile, $json + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "Wrote manifest with $($versions.Count) versions to $OutputPath"
