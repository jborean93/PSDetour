[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]
    $Configuration = 'Debug'
)

$modulePath = [IO.Path]::Combine($PSScriptRoot, 'module')
$manifestItem = Get-Item ([IO.Path]::Combine($modulePath, '*.psd1'))

$ModuleName = $manifestItem.BaseName
$Manifest = Test-ModuleManifest -Path $manifestItem.FullName -ErrorAction Ignore -WarningAction Ignore
$Version = $Manifest.Version
$BuildPath = [IO.Path]::Combine($PSScriptRoot, 'output')
$PowerShellPath = [IO.Path]::Combine($PSScriptRoot, 'module')
$CSharpPath = [IO.Path]::Combine($PSScriptRoot, 'src')
$ReleasePath = [IO.Path]::Combine($BuildPath, $ModuleName, $Version)
$IsUnix = $PSEdition -eq 'Core' -and -not $IsWindows

task Clean {
    if (Test-Path $ReleasePath) {
        Remove-Item $ReleasePath -Recurse -Force
    }

    New-Item -ItemType Directory $ReleasePath | Out-Null
}

task AssertSMA {
    $AssertSMA = "$PSScriptRoot/tools/AssertSMA.ps1"
    & $AssertSMA -RequiredVersion 7.2.0
}

task AssertDetours {
    $AssertDetours = "$PSScriptRoot/tools/AssertDetours.ps1"
    & $AssertDetours -RequiredVersion 4.0.1
}

task BuildDocs {
    $helpParams = @{
        Path = [IO.Path]::Combine($PSScriptRoot, 'docs', 'en-US')
        OutputPath = [IO.Path]::Combine($ReleasePath, 'en-US')
    }
    New-ExternalHelp @helpParams | Out-Null
}

task BuildManaged {
    Get-ChildItem -LiteralPath $CSharpPath -Directory | ForEach-Object -Process {
        $_ | Push-Location
        $arguments = @(
            'publish'
            '--configuration', $Configuration
            '--verbosity', 'q'
            '-nologo'
            "-p:Version=$Version"
        )
        try {
            [xml]$csharpProjectInfo = Get-Content ([IO.Path]::Combine($_.FullName, '*.csproj'))
            $targetFrameworks = @($csharpProjectInfo.Project.PropertyGroup[0].TargetFrameworks.Split(
                    ';', [StringSplitOptions]::RemoveEmptyEntries))

            foreach ($framework in $targetFrameworks) {
                Write-Host "Compiling $($_.Name) for $framework"
                dotnet @arguments --framework $framework

                if ($LASTEXITCODE) {
                    throw "Failed to compiled code for $framework"
                }
            }
        }
        finally {
            Pop-Location
        }
    }
}

task CopyToRelease {
    $copyParams = @{
        Path = [IO.Path]::Combine($PowerShellPath, '*')
        Destination = $ReleasePath
        Recurse = $true
        Force = $true
    }
    Copy-Item @copyParams

    $nativeBuildFolder = [IO.Path]::Combine($CSharpPath, "$($ModuleName)Native", 'bin', $Configuration, 'final')
    $nativeBinFolder = [IO.Path]::Combine($ReleasePath, 'bin', 'x64')
    if (-not (Test-Path -LiteralPath $nativeBinFolder)) {
        New-Item -Path $nativeBinFolder -ItemType Directory | Out-Null
    }
    Copy-Item ([IO.Path]::Combine($nativeBuildFolder, '*.dll')) -Destination $nativeBinFolder

    [xml]$csharpProjectInfo = Get-Content ([IO.Path]::Combine($CSharpPath, $ModuleName, '*.csproj'))
    $targetFrameworks = @($csharpProjectInfo.Project.PropertyGroup[0].TargetFrameworks.Split(
            ';', [StringSplitOptions]::RemoveEmptyEntries))

    foreach ($framework in $targetFrameworks) {
        $buildFolder = [IO.Path]::Combine($CSharpPath, $ModuleName, 'bin', $Configuration, $framework, 'publish')
        $binFolder = [IO.Path]::Combine($ReleasePath, 'bin', $framework)
        if (-not (Test-Path -LiteralPath $binFolder)) {
            New-Item -Path $binFolder -ItemType Directory | Out-Null
        }
        Copy-Item ([IO.Path]::Combine($buildFolder, "*")) -Destination $binFolder -Exclude "System.Management.Automation.*"
    }
}

task Sign {
    $vaultName = $env:AZURE_KEYVAULT_NAME
    $vaultCert = $env:AZURE_KEYVAULT_CERT
    if (-not $vaultName -or -not $vaultCert) {
        return
    }

    $key = Get-OpenAuthenticodeAzKey -Vault $vaultName -Certificate $vaultCert
    $signParams = @{
        Key = $key
        TimeStampServer = 'http://timestamp.digicert.com'
    }

    Get-ChildItem -LiteralPath $ReleasePath -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in ".ps1", ".psm1", ".psd1", ".ps1xml" -or (
                $_.Extension -eq ".dll" -and $_.BaseName -like "$ModuleName*"
            )
        } |
        ForEach-Object -Process {
            Set-OpenAuthenticodeSignature -LiteralPath $_.FullName @signParams
        }
}

task Package {
    $nupkgPath = [IO.Path]::Combine($BuildPath, "$ModuleName.$Version*.nupkg")
    if (Test-Path $nupkgPath) {
        Remove-Item $nupkgPath -Force
    }

    $repoParams = @{
        Name = 'LocalRepo'
        SourceLocation = $BuildPath
        PublishLocation = $BuildPath
        InstallationPolicy = 'Trusted'
    }
    if (Get-PSRepository -Name $repoParams.Name -ErrorAction SilentlyContinue) {
        Unregister-PSRepository -Name $repoParams.Name
    }

    Register-PSRepository @repoParams
    try {
        Publish-Module -Path $ReleasePath -Repository $repoParams.Name
    }
    finally {
        Unregister-PSRepository -Name $repoParams.Name
    }
}

task Analyze {
    $pssaSplat = @{
        Path = $ReleasePath
        Settings = [IO.Path]::Combine($PSScriptRoot, 'ScriptAnalyzerSettings.psd1')
        Recurse = $true
        ErrorAction = 'SilentlyContinue'
    }
    $results = Invoke-ScriptAnalyzer @pssaSplat
    if ($null -ne $results) {
        $results | Out-String
        throw "Failed PsScriptAnalyzer tests, build failed"
    }
}

task DoUnitTest {
    $testsPath = [IO.Path]::Combine($PSScriptRoot, 'tests', 'units')
    if (-not (Test-Path -LiteralPath $testsPath)) {
        Write-Host "No unit tests found, skipping"
        return
    }

    $resultsPath = [IO.Path]::Combine($BuildPath, 'TestResults')
    if (-not (Test-Path -LiteralPath $resultsPath)) {
        New-Item $resultsPath -ItemType Directory -ErrorAction Stop | Out-Null
    }

    # dotnet test places the results in a subfolder of the results-directory. This subfolder is based on a random guid
    # so a temp folder is used to ensure we only get the current runs results
    $tempResultsPath = [IO.Path]::Combine($resultsPath, "TempUnit")
    if (Test-Path -LiteralPath $tempResultsPath) {
        Remove-Item -LiteralPath $tempResultsPath -Force -Recurse
    }
    New-Item -Path $tempResultsPath -ItemType Directory | Out-Null

    try {
        $runSettingsPrefix = 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration'
        $arguments = @(
            'test'
            '"{0}"' -f $testsPath
            '--results-directory', $tempResultsPath
            '--collect:"XPlat Code Coverage"'
            '--'
            "$runSettingsPrefix.Format=json"
            "$runSettingsPrefix.IncludeDirectory=`"$CSharpPath`""
        )

        Write-Host "Running unit tests"
        dotnet @arguments

        if ($LASTEXITCODE) {
            throw "Unit tests failed"
        }

        Move-Item -Path $tempResultsPath/*/*.json -Destination $resultsPath/UnitCoverage.json -Force
    }
    finally {
        Remove-Item -LiteralPath $tempResultsPath -Force -Recurse
    }
}

task DoTest {
    $resultsPath = [IO.Path]::Combine($BuildPath, 'TestResults')
    if (-not (Test-Path $resultsPath)) {
        New-Item $resultsPath -ItemType Directory -ErrorAction Stop | Out-Null
    }

    $resultsFile = [IO.Path]::Combine($resultsPath, 'Pester.xml')
    if (Test-Path $resultsFile) {
        Remove-Item $resultsFile -ErrorAction Stop -Force
    }

    $pesterScript = [IO.Path]::Combine($PSScriptRoot, 'tools', 'PesterTest.ps1')
    $pwsh = [Environment]::GetCommandLineArgs()[0] -replace '\.dll$', ''
    $arguments = @(
        '-NoProfile'
        '-NonInteractive'
        if (-not $IsUnix) {
            '-ExecutionPolicy', 'Bypass'
        }
        '-File', $pesterScript
        '-TestPath', ([IO.Path]::Combine($PSScriptRoot, 'tests'))
        '-OutputFile', $resultsFile
    )

    # We use coverlet to collect code coverage of our binary
    $unitCoveragePath = [IO.Path]::Combine($resultsPath, 'UnitCoverage.json')
    $targetArgs = '"' + ($arguments -join '" "') + '"'

    if ($PSVersionTable.PSVersion.Major -eq 7 -and $PSVersionTable.PSVersion.Minor -eq 2) {
        $pwshFramework = 'net6.0-windows'
        $targetArgs = '"' + ($targetArgs -replace '"', '\"') + '"'
        $watchFolder = '"{0}"' -f ([IO.Path]::Combine($ReleasePath, 'bin', $pwshFramework))
    }
    else {
        $pwshFramework = 'net7.0-windows'
        $watchFolder = [IO.Path]::Combine($ReleasePath, 'bin', $pwshFramework)
    }

    $arguments = @(
        $watchFolder
        '--target', $pwsh
        '--targetargs', $targetArgs
        '--output', ([IO.Path]::Combine($resultsPath, 'Coverage.xml'))
        '--format', 'cobertura'
        if (Test-Path -LiteralPath $unitCoveragePath) {
            '--merge-with', $unitCoveragePath
        }
    )

    & coverlet $arguments
    if ($LASTEXITCODE) {
        throw "Pester failed tests"
    }
}

task DoInstall {
    $installBase = $Home
    if ($profile) {
        $installBase = $profile | Split-Path
    }

    $installPath = [IO.Path]::Combine($installBase, 'Modules', $ModuleName, $Version)
    if (-not (Test-Path $installPath)) {
        New-Item $installPath -ItemType Directory | Out-Null
    }

    Copy-Item -Path ([IO.Path]::Combine($ReleasePath, '*')) -Destination $installPath -Force -Recurse
}

task Build -Jobs Clean, AssertDetours, AssertSMA, BuildManaged, CopyToRelease, BuildDocs, Sign, Package

# FIXME: Work out why we need the obj and bin folder for coverage to work
task Test -Jobs AssertDetours, AssertSMA, BuildManaged, Analyze, DoUnitTest, DoTest
task Install -Jobs DoInstall

task . Build
