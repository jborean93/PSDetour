using namespace System.IO
using namespace System.IO.Compression
using namespace System.Net

[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string] $RequiredVersion
)
begin {
    Add-Type -AssemblyName System.IO.Compression
    function SaveEntry {
        param(
            [ZipArchiveEntry] $Entry,
            [string] $Destination
        )
        end {
            if (-not (Test-Path $Destination -PathType Container)) {
                throw 'Destination path must be a directory'
            }

            $Destination = Join-Path (Resolve-Path $Destination) -ChildPath $Entry.Name
            $entryStream = $Entry.Open()
            try {
                $destinationStream = [FileStream]::new(
                    <# path:   #> $Destination,
                    <# mode:   #> [FileMode]::Create,
                    <# access: #> [FileAccess]::Write,
                    <# share:  #> [FileShare]::ReadWrite)
                try {
                    $entryStream.CopyTo($destinationStream)
                } finally {
                    $destinationStream.Dispose()
                }
            } finally {
                $entryStream.Dispose()
            }
        }
    }
}
end {
    $targetFolder = $PSCmdlet.GetUnresolvedProviderPathFromPSPath(
        "$PSScriptRoot/../output/lib/System.Management.Automation/$RequiredVersion")
    $version = $RequiredVersion
    $fileName = "PowerShell-$version-win-fxdependent.zip"

    if (Test-Path $targetFolder\*.dll) {
        return
    }

    if (-not (Test-Path $targetFolder)) {
        $null = New-Item $targetFolder -ItemType Directory -Force
    }

    $oldSecurityProtocol = [ServicePointManager]::SecurityProtocol
    try {
        &{
            $ProgressPreference = 'SilentlyContinue'
            [ServicePointManager]::SecurityProtocol = 'Tls12'
            $downloadUri = "https://github.com/PowerShell/PowerShell/releases/download/v$version/$fileName"
            Invoke-WebRequest -UseBasicParsing -Uri $downloadUri -OutFile $targetFolder/$fileName
        }
    } finally {
        [ServicePointManager]::SecurityProtocol = $oldSecurityProtocol
    }

    # Why do all this when Expand-Archive exists? Well, it doesn't always resolve for me for some
    # reason.  Still gotta figure that out, but this also lets us pick and choose what we want from
    # the archive anyway.
    $fileStream = [FileStream]::new(
        <# path:   #> (Join-Path $targetFolder -ChildPath $fileName),
        <# mode:   #> [FileMode]::Open,
        <# access: #> [FileAccess]::Read,
        <# share:  #> [FileShare]::ReadWrite)

    try {
        $archiveStream = [ZipArchive]::new(
            <# stream: #> $fileStream,
            <# mode:   #> [ZipArchiveMode]::Read)

        try {
            $null = New-Item $targetFolder -ItemType Directory -Force -ErrorAction Ignore
            $dllEntry = $archiveStream.GetEntry('System.Management.Automation.dll')
            SaveEntry -Entry $dllEntry -Destination $targetFolder

            $xmlEntry = $archiveStream.GetEntry('System.Management.Automation.xml')
            SaveEntry -Entry $xmlEntry -Destination $targetFolder
        } finally {
            $archiveStream.Dispose()
        }
    } finally {
        $fileStream.Dispose()
        Remove-Item $targetFolder\$fileName -Force
    }
}
