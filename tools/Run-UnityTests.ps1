<#
.SYNOPSIS
Runs this project's Unity EditMode and/or PlayMode tests.

.DESCRIPTION
This project uses a Unity Test Framework version that should not be invoked
with -quit when running command-line tests. TestRunner exits Unity after the
test run finishes.
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'EditMode', 'PlayMode')]
    [string]$TestPlatform = 'All',

    [string]$ProjectPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Space Game'),
    [string]$UnityExe,
    [string]$ResultsDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'FriendSlopUnityTests')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Read-TestSummary {
    param([Parameter(Mandatory = $true)][string]$ResultsPath)

    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        return $null
    }

    [xml]$xml = Get-Content -LiteralPath $ResultsPath -Raw
    $run = $xml.'test-run'
    $failedCases = @()
    foreach ($case in $xml.SelectNodes('//test-case[@result="Failed" or @result="Error"]')) {
        $messageNode = $case.SelectSingleNode('failure/message')
        $message = if ($null -ne $messageNode) { $messageNode.InnerText.Trim() } else { '' }
        $failedCases += [PSCustomObject]@{
            Name = $case.GetAttribute('fullname')
            Message = $message
        }
    }

    return [PSCustomObject]@{
        Result = $run.GetAttribute('result')
        Total = $run.GetAttribute('total')
        Passed = $run.GetAttribute('passed')
        Failed = $run.GetAttribute('failed')
        Skipped = $run.GetAttribute('skipped')
        FailedCases = $failedCases
    }
}

function Quote-CommandLineArgument {
    param([Parameter(Mandatory = $true)][string]$Value)

    return '"' + ($Value -replace '"', '\"') + '"'
}

function Invoke-UnityTestPlatform {
    param(
        [Parameter(Mandatory = $true)][string]$Platform,
        [Parameter(Mandatory = $true)][string]$ResolvedProjectPath,
        [Parameter(Mandatory = $true)][string]$ResolvedUnityExe,
        [Parameter(Mandatory = $true)][string]$ResolvedResultsDirectory
    )

    $platformSlug = $Platform.ToLowerInvariant()
    $resultsPath = Join-Path $ResolvedResultsDirectory "friend-slop-$platformSlug-results.xml"
    $logPath = Join-Path $ResolvedResultsDirectory "friend-slop-$platformSlug.log"

    Remove-Item -LiteralPath $resultsPath, $logPath -Force -ErrorAction SilentlyContinue

    Write-Host "Running Unity $Platform tests..."
    Write-Host "Unity: $ResolvedUnityExe"
    Write-Host "Project: $ResolvedProjectPath"
    Write-Host "Results: $resultsPath"
    Write-Host "Log: $logPath"

    $argumentList = @(
        '-batchmode',
        '-projectPath', (Quote-CommandLineArgument -Value $ResolvedProjectPath),
        '-runTests',
        '-testPlatform', $Platform,
        '-testResults', (Quote-CommandLineArgument -Value $resultsPath),
        '-logFile', (Quote-CommandLineArgument -Value $logPath)
    ) -join ' '

    $startProcessArguments = @{
        FilePath = $ResolvedUnityExe
        ArgumentList = $argumentList
        Wait = $true
        PassThru = $true
    }

    if ([System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT) {
        $startProcessArguments.WindowStyle = 'Hidden'
    }

    $process = Start-Process @startProcessArguments
    $exitCode = $process.ExitCode
    $summary = Read-TestSummary -ResultsPath $resultsPath

    if ($null -eq $summary) {
        Write-Error "Unity did not write a test results file for $Platform. See log: $logPath"
        return $false
    }

    Write-Host "$Platform result=$($summary.Result) total=$($summary.Total) passed=$($summary.Passed) failed=$($summary.Failed) skipped=$($summary.Skipped)"
    foreach ($failedCase in $summary.FailedCases) {
        Write-Host "FAILED: $($failedCase.Name)"
        if (-not [string]::IsNullOrWhiteSpace($failedCase.Message)) {
            Write-Host $failedCase.Message
        }
    }

    return ($exitCode -eq 0 -and $summary.Result -like 'Passed*')
}

$resolvedProjectPath = Resolve-FullPath -Path $ProjectPath
$resolvedResultsDirectory = Resolve-FullPath -Path $ResultsDirectory
New-Item -ItemType Directory -Path $resolvedResultsDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($UnityExe)) {
    $UnityExe = & (Join-Path $PSScriptRoot 'Find-Unity.ps1') -ProjectPath $resolvedProjectPath
}
$resolvedUnityExe = Resolve-FullPath -Path $UnityExe

$platforms = if ($TestPlatform -eq 'All') { @('EditMode', 'PlayMode') } else { @($TestPlatform) }
$allPassed = $true
foreach ($platform in $platforms) {
    $passed = Invoke-UnityTestPlatform -Platform $platform -ResolvedProjectPath $resolvedProjectPath -ResolvedUnityExe $resolvedUnityExe -ResolvedResultsDirectory $resolvedResultsDirectory
    $allPassed = $allPassed -and $passed
}

if (-not $allPassed) {
    exit 1
}

exit 0
