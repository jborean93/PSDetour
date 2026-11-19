using namespace System.IO

<#
.SYNOPSIS
Run Pester test

.PARAMETER TestPath
The path to the tests to run

.PARAMETER OutputFile
The path to write the Pester test results to.

.PARAMETER PesterVersion
The exact Pester version to run the tests with, other versions on the module
path are ignored.
#>
[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [String]
    $TestPath,

    [Parameter(Mandatory)]
    [String]
    $OutputFile,

    [Parameter(Mandatory)]
    [Version]
    $PesterVersion
)

$ErrorActionPreference = 'Stop'

Import-Module -Name Pester -RequiredVersion $PesterVersion

[PSCustomObject]$PSVersionTable |
    Select-Object -Property *, @{N = 'Architecture'; E = {
            switch ([IntPtr]::Size) {
                4 { 'x86' }
                8 { 'x64' }
                default { 'Unknown' }
            }
        }
    } |
    Format-List |
    Out-Host

$configuration = [PesterConfiguration]::Default
$configuration.Output.Verbosity = 'Detailed'
$configuration.Run.Path = $TestPath
$configuration.Run.Throw = $true
$configuration.TestResult.Enabled = $true
$configuration.TestResult.OutputPath = $OutputFile
$configuration.TestResult.OutputFormat = 'NUnitXml'

Invoke-Pester -Configuration $configuration -WarningAction Ignore
