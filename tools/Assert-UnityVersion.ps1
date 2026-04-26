<#
.SYNOPSIS
Verifies that an expected Unity version matches ProjectVersion.txt.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Space Game'),
    [string]$ExpectedUnityVersion = $env:UNITY_VERSION
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
$versionFile = Join-Path $resolvedProjectPath 'ProjectSettings\ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
    throw "Unity ProjectVersion.txt was not found at '$versionFile'."
}

$actualUnityVersion = $null
foreach ($line in Get-Content -LiteralPath $versionFile) {
    if ($line -match '^m_EditorVersion:\s*(.+?)\s*$') {
        $actualUnityVersion = $Matches[1]
        break
    }
}

if ([string]::IsNullOrWhiteSpace($actualUnityVersion)) {
    throw "Could not read m_EditorVersion from '$versionFile'."
}

if ([string]::IsNullOrWhiteSpace($ExpectedUnityVersion)) {
    throw 'Expected Unity version was not supplied. Pass -ExpectedUnityVersion or set UNITY_VERSION.'
}

if ($ExpectedUnityVersion -ne $actualUnityVersion) {
    throw "Unity version mismatch. Expected '$ExpectedUnityVersion', but ProjectVersion.txt requires '$actualUnityVersion'."
}

Write-Host "Unity version verified: $actualUnityVersion"
