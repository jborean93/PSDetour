using namespace System.IO
using namespace System.Net

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$RequiredVersion
)
end {
    $downloadUrl = "https://github.com/PowerShell/PowerShell/releases/download/v$RequiredVersion/PowerShell-$RequiredVersion-win-x64.zip"
    $targetFolder = $PSCmdlet.GetUnresolvedProviderPathFromPSPath(
        "$PSScriptRoot/../output/pwsh-$RequiredVersion")
    $fileName = "pwsh-$RequiredVersion.zip"

    if (Test-Path $targetFolder\pwsh.exe) {
        return
    }

    if (-not (Test-Path $targetFolder)) {
        $null = New-Item $targetFolder -ItemType Directory -Force
    }

    $oldSecurityProtocol = [ServicePointManager]::SecurityProtocol
    try {
        & {
            $ProgressPreference = 'SilentlyContinue'
            [ServicePointManager]::SecurityProtocol = 'Tls12'
            Invoke-WebRequest -UseBasicParsing -Uri $downloadUrl -OutFile $targetFolder/$fileName
        }
    }
    finally {
        [ServicePointManager]::SecurityProtocol = $oldSecurityProtocol
    }

    $oldPreference = $global:ProgressPreference
    try {
        $global:ProgressPreference = 'SilentlyContinue'
        Expand-Archive -LiteralPath $targetFolder/$fileName -DestinationPath $targetFolder
    }
    finally {
        $global:ProgressPreference = $oldPreference
    }

    Remove-Item -LiteralPath $targetFolder/$fileName
}
