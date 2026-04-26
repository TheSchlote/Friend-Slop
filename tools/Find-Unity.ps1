<#
.SYNOPSIS
Finds the Unity editor executable that matches this project's Unity version.

.DESCRIPTION
The script avoids relying on global PATH. It resolves Unity in this order:
UNITY_EXE, common Unity Hub install roots, Unity Hub secondary install roots,
then PATH as a fallback. The resolved editor must match ProjectVersion.txt
unless -AllowVersionMismatch is supplied.
#>
[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Space Game'),
    [string]$UnityVersion,
    [switch]$AllowVersionMismatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Get-ProjectUnityVersion {
    param([Parameter(Mandatory = $true)][string]$ResolvedProjectPath)

    $versionFile = Join-Path $ResolvedProjectPath 'ProjectSettings\ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $versionFile -PathType Leaf)) {
        throw "Unity ProjectVersion.txt was not found at '$versionFile'."
    }

    foreach ($line in Get-Content -LiteralPath $versionFile) {
        if ($line -match '^m_EditorVersion:\s*(.+?)\s*$') {
            return $Matches[1]
        }
    }

    throw "Could not read m_EditorVersion from '$versionFile'."
}

function Add-Candidate {
    param(
        [AllowEmptyCollection()][System.Collections.Generic.List[string]]$Candidates,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    try {
        $resolved = Resolve-FullPath -Path $Path
    }
    catch {
        $resolved = $Path
    }

    if (-not $Candidates.Contains($resolved)) {
        [void]$Candidates.Add($resolved)
    }
}

function Join-IfBase {
    param(
        [string]$Base,
        [string]$Child
    )

    if ([string]::IsNullOrWhiteSpace($Base)) {
        return $null
    }

    return Join-Path $Base $Child
}

function Add-HubEditorRoot {
    param(
        [AllowEmptyCollection()][System.Collections.Generic.List[string]]$Candidates,
        [string]$Root,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ([string]::IsNullOrWhiteSpace($Root)) {
        return
    }

    Add-Candidate -Candidates $Candidates -Path (Join-Path $Root (Join-Path $Version 'Editor\Unity.exe'))

    $leaf = Split-Path $Root -Leaf
    if ($leaf -eq $Version) {
        Add-Candidate -Candidates $Candidates -Path (Join-Path $Root 'Editor\Unity.exe')
    }
}

function Add-HubRootsFromValue {
    param(
        [AllowEmptyCollection()][System.Collections.Generic.List[string]]$Candidates,
        [AllowNull()][object]$Value,
        [Parameter(Mandatory = $true)][string]$Version
    )

    if ($null -eq $Value) {
        return
    }

    if ($Value -is [string]) {
        Add-HubEditorRoot -Candidates $Candidates -Root $Value -Version $Version
        return
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        foreach ($item in $Value) {
            Add-HubRootsFromValue -Candidates $Candidates -Value $item -Version $Version
        }
        return
    }

    foreach ($propertyName in @('path', 'paths', 'installPath', 'installPaths', 'secondaryInstallPath', 'secondaryInstallPaths')) {
        $property = $Value.PSObject.Properties[$propertyName]
        if ($null -ne $property) {
            Add-HubRootsFromValue -Candidates $Candidates -Value $property.Value -Version $Version
        }
    }
}

function Add-HubConfigCandidates {
    param(
        [AllowEmptyCollection()][System.Collections.Generic.List[string]]$Candidates,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $configFiles = @(
        (Join-IfBase -Base $env:APPDATA -Child 'UnityHub\secondaryInstallPath.json'),
        (Join-IfBase -Base $env:LOCALAPPDATA -Child 'UnityHub\secondaryInstallPath.json')
    )

    foreach ($configFile in $configFiles) {
        if ([string]::IsNullOrWhiteSpace($configFile) -or -not (Test-Path -LiteralPath $configFile -PathType Leaf)) {
            continue
        }

        try {
            $json = Get-Content -LiteralPath $configFile -Raw | ConvertFrom-Json
            Add-HubRootsFromValue -Candidates $Candidates -Value $json -Version $Version
        }
        catch {
            Write-Warning "Could not parse Unity Hub install path config '$configFile': $($_.Exception.Message)"
        }
    }
}

function Get-UnityExecutableVersion {
    param([Parameter(Mandatory = $true)][string]$UnityExe)

    try {
        $output = & $UnityExe -version 2>&1 | ForEach-Object { $_.ToString() }
        foreach ($line in $output) {
            if ($line -match '(\d+\.\d+\.\d+[a-z]\d+)') {
                return $Matches[1]
            }
        }
    }
    catch {
        return $null
    }

    return $null
}

$resolvedProjectPath = Resolve-FullPath -Path $ProjectPath
if ([string]::IsNullOrWhiteSpace($UnityVersion)) {
    $UnityVersion = Get-ProjectUnityVersion -ResolvedProjectPath $resolvedProjectPath
}

$candidates = [System.Collections.Generic.List[string]]::new()
Add-Candidate -Candidates $candidates -Path $env:UNITY_EXE

$hubRoots = @(
    (Join-IfBase -Base $env:ProgramFiles -Child 'Unity\Hub\Editor'),
    (Join-IfBase -Base ${env:ProgramFiles(x86)} -Child 'Unity\Hub\Editor'),
    (Join-IfBase -Base $env:LOCALAPPDATA -Child 'Programs\Unity\Hub\Editor')
)

foreach ($hubRoot in $hubRoots) {
    Add-HubEditorRoot -Candidates $candidates -Root $hubRoot -Version $UnityVersion
}

Add-HubConfigCandidates -Candidates $candidates -Version $UnityVersion

foreach ($commandName in @('Unity', 'Unity.exe')) {
    foreach ($command in Get-Command -Name $commandName -ErrorAction SilentlyContinue) {
        Add-Candidate -Candidates $candidates -Path $command.Source
    }
}

$existingMismatches = [System.Collections.Generic.List[string]]::new()
foreach ($candidate in $candidates) {
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        continue
    }

    $actualVersion = Get-UnityExecutableVersion -UnityExe $candidate
    if ($AllowVersionMismatch -or $actualVersion -eq $UnityVersion) {
        Write-Output ((Resolve-Path -LiteralPath $candidate).ProviderPath)
        exit 0
    }

    if ([string]::IsNullOrWhiteSpace($actualVersion)) {
        [void]$existingMismatches.Add("$candidate (version could not be read)")
    }
    else {
        [void]$existingMismatches.Add("$candidate (reported $actualVersion)")
    }
}

$checked = if ($candidates.Count -gt 0) { $candidates -join [Environment]::NewLine } else { '<no candidates>' }
$message = "Could not find Unity $UnityVersion for project '$resolvedProjectPath'. Checked:$([Environment]::NewLine)$checked"
if ($existingMismatches.Count -gt 0) {
    $message += "$([Environment]::NewLine)Found non-matching Unity executables:$([Environment]::NewLine)$($existingMismatches -join [Environment]::NewLine)"
}
$message += "$([Environment]::NewLine)Install Unity $UnityVersion with Unity Hub or set UNITY_EXE to the full Unity.exe path."
throw $message
