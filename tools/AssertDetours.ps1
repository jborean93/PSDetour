using namespace System.IO
using namespace System.Net

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $RequiredVersion
)
end {
    $targetFolder = $PSCmdlet.GetUnresolvedProviderPathFromPSPath(
        "$PSScriptRoot/../output/lib")
    $fileName = "Detours-$RequiredVersion.zip"

    if (Test-Path $targetFolder\Detours) {
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
            $downloadUri = "https://github.com/microsoft/Detours/archive/refs/tags/v$RequiredVersion.zip"
            Invoke-WebRequest -UseBasicParsing -Uri $downloadUri -OutFile $targetFolder/$fileName
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

    Rename-Item -LiteralPath $targetFolder/Detours-$RequiredVersion -NewName Detours
    Remove-Item -LiteralPath $targetFolder/$fileName
}
