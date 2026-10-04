<#
.SYNOPSIS
    Builds the plugin for every supported Jellyfin line and writes installable zips plus their manifest entries.

.DESCRIPTION
    For each target framework (net9.0 = Jellyfin 10.11, net10.0 = Jellyfin 12) this publishes the plugin, stages only
    the assemblies listed in build/plugin.json, adds meta.json, zips it and records a plugin repository "version"
    entry (targetAbi, sourceUrl, MD5 checksum). The entries are written to manifest-versions.json, which the release
    workflow uploads next to the zips; build/New-PluginManifest.ps1 merges them into the repository manifest.

    Works on PowerShell 7 (CI) and Windows PowerShell 5.1.

.EXAMPLE
    ./build/package.ps1 -Version 0.1.0

.EXAMPLE
    ./build/package.ps1 -Version 0.1.0 -SourceUrlBase https://github.com/25LioN52/JellyfinHyperionGrabber/releases/download/v0.1.0 -ChangelogFile notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [string] $SourceUrlBase = '',

    [string] $ChangelogFile = '',

    [string] $OutputDirectory = '',

    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$OutputDirectory = if ($OutputDirectory) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
} else {
    Join-Path $repositoryRoot 'artifacts'
}

$project = Join-Path $repositoryRoot (Join-Path 'src' (Join-Path 'Jellyfin.Plugin.HyperionGrabber' 'Jellyfin.Plugin.HyperionGrabber.csproj'))
$metadata = Get-Content (Join-Path $PSScriptRoot 'plugin.json') -Raw | ConvertFrom-Json
$changelog = if ($ChangelogFile) { (Get-Content $ChangelogFile -Raw).Trim() } else { '' }
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$utf8 = New-Object System.Text.UTF8Encoding $false

function Get-MSBuildProperty([string] $Framework, [string] $Name) {
    $value = (& dotnet msbuild $project -nologo "-getProperty:$Name" "-p:TargetFramework=$Framework" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $value) {
        throw "Could not read MSBuild property $Name for $Framework."
    }

    return $value
}

function Write-Json([object] $Value, [string] $Path) {
    [System.IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 8) + "`n", $utf8)
}

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$frameworks = @((& dotnet msbuild $project -nologo '-getProperty:TargetFrameworks' | Out-String).Trim().Split(';'))
$entries = @()

foreach ($framework in $frameworks) {
    $targetAbi = Get-MSBuildProperty $framework 'JellyfinTargetAbi'
    $flavor = Get-MSBuildProperty $framework 'JellyfinAbiFlavor'
    $jellyfinLine = Get-MSBuildProperty $framework 'JellyfinLine'
    $pluginVersion = "$Version.$flavor"
    Write-Host "Packaging $pluginVersion for Jellyfin $jellyfinLine ($framework, targetAbi $targetAbi)"

    $publishDirectory = Join-Path $OutputDirectory (Join-Path 'publish' $framework)
    & dotnet publish $project -nologo -c $Configuration -f $framework -o $publishDirectory "-p:VersionPrefix=$Version"
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $framework."
    }

    $stage = Join-Path $OutputDirectory (Join-Path 'stage' $framework)
    if (Test-Path $stage) {
        Remove-Item $stage -Recurse -Force
    }

    New-Item -ItemType Directory -Force $stage | Out-Null
    foreach ($artifact in $metadata.artifacts) {
        Copy-Item (Join-Path $publishDirectory $artifact) $stage
    }

    Write-Json ([ordered]@{
            category    = $metadata.category
            changelog   = $changelog
            description = $metadata.description
            guid        = $metadata.guid
            name        = $metadata.name
            overview    = $metadata.overview
            owner       = $metadata.owner
            targetAbi   = $targetAbi
            timestamp   = $timestamp
            version     = $pluginVersion
        }) (Join-Path $stage 'meta.json')

    $zipName = "hyperion-grabber_$($pluginVersion)_jellyfin-$jellyfinLine.zip"
    $zipPath = Join-Path $OutputDirectory $zipName
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zipPath
    $checksum = (Get-FileHash $zipPath -Algorithm MD5).Hash.ToLowerInvariant()

    $sourceUrl = if ($SourceUrlBase) { "$($SourceUrlBase.TrimEnd('/'))/$zipName" } else { $zipName }
    $entries += [ordered]@{
        version   = $pluginVersion
        changelog = $changelog
        targetAbi = $targetAbi
        sourceUrl = $sourceUrl
        checksum  = $checksum
        timestamp = $timestamp
    }
}

$entriesPath = Join-Path $OutputDirectory 'manifest-versions.json'
Write-Json @($entries) $entriesPath
Write-Host "Wrote $($entries.Count) manifest entries to $entriesPath"
