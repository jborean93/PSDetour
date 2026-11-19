using namespace System.IO
using namespace System.Runtime.InteropServices

#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]
    $Configuration = 'Debug',

    [Parameter()]
    [ValidateSet('Build', 'Test', 'Sign')]
    [string]
    $Task = 'Build',

    [Parameter()]
    [Version]
    $PowerShellVersion = $PSVersionTable.PSVersion,

    [Parameter()]
    [Architecture]
    $PowerShellArch = [RuntimeInformation]::ProcessArchitecture,

    [Parameter()]
    [string]
    $ModuleNupkg
)

$ErrorActionPreference = 'Stop'

if ($Task -eq 'Sign' -and -not $ModuleNupkg) {
    throw "The Sign task requires -ModuleNupkg with the nupkg to sign"
}

. ([Path]::Combine($PSScriptRoot, "tools", "common.ps1"))

$manifestPath = ([Path]::Combine($PSScriptRoot, 'manifest.psd1'))
$Manifest = [Manifest]::new($Configuration, $PowerShellVersion, $PowerShellArch, $manifestPath)

if ($ModuleNupkg) {
    # Only the nupkg contents should be present, not a previous build.
    Remove-Item -LiteralPath $Manifest.ReleasePath -Recurse -Force
    Write-Host "Expanding module nupkg to '$($Manifest.ReleasePath)'" -ForegroundColor Cyan
    Expand-Nupkg -Path $ModuleNupkg -DestinationPath $Manifest.ReleasePath
}

Write-Host "Installing PowerShell dependencies" -ForegroundColor Cyan
$deps = $Task -eq 'Test' ? $Manifest.TestRequirements : $Manifest.BuildRequirements
$deps | Install-BuildDependencies

$buildScript = [Path]::Combine($PSScriptRoot, "tools", "InvokeBuild.ps1")
$invokeBuildSplat = @{
    Task = $Task
    File = $buildScript
    Manifest = $manifest
}
Invoke-Build @invokeBuildSplat
