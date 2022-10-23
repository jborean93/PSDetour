# Copyright: (c) 2022, Jordan Borean (@jborean93) <jborean93@gmail.com>
# MIT License (see LICENSE or https://opensource.org/licenses/MIT)

$importModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core
if (-not ('PSDetour.Commands.NewPSDetourSession' -as [type])) {
    $framework = if ($PSVersionTable.PSVersion.Major -eq 7 -and $PSVersionTable.PSVersion.Minor -eq 2) {
        'net6.0-windows'
    }
    else {
        'net7.0-windows'
    }

    &$importModule ([IO.Path]::Combine($PSScriptRoot, 'bin', $framework, 'PSDetour.dll')) -ErrorAction Stop
}
else {
    &$importModule -Force -Assembly ([PSDetour.Commands.NewPSDetourSession].Assembly)
}
