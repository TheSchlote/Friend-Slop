<#
.SYNOPSIS
Configures this checkout to use UnityYAMLMerge for Unity text assets.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Space Game'),
    [string]$UnityExe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $UnityExe = & (Join-Path $PSScriptRoot 'Find-Unity.ps1') -ProjectPath $ProjectPath
}

$resolvedUnityExe = Resolve-FullPath -Path $UnityExe
$unityExeDirectory = Split-Path -Parent $resolvedUnityExe
$unityRoot = Split-Path -Parent $unityExeDirectory

$toolCandidates = @(
    (Join-Path $unityExeDirectory 'Data\Tools\UnityYAMLMerge.exe'),
    (Join-Path $unityExeDirectory 'Data\Tools\UnityYAMLMerge'),
    (Join-Path $unityRoot 'Tools\UnityYAMLMerge')
)

$unityYamlMerge = $null
foreach ($candidate in $toolCandidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        $unityYamlMerge = (Resolve-Path -LiteralPath $candidate).ProviderPath
        break
    }
}

if ([string]::IsNullOrWhiteSpace($unityYamlMerge)) {
    throw "UnityYAMLMerge was not found next to '$resolvedUnityExe'. Checked: $($toolCandidates -join ', ')"
}

& git config --local merge.unityyamlmerge.name 'Unity SmartMerge'
& git config --local merge.unityyamlmerge.driver "`"$unityYamlMerge`" merge -p %O %B %A %A"
& git config --local merge.unityyamlmerge.recursive binary

Write-Host "Configured Unity SmartMerge for this checkout: $unityYamlMerge"
