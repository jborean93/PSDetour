using namespace System.Collections
using namespace System.IO
using namespace System.Text

#Requires -Version 7.2

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Manifest]
    $Manifest
)

#region Build

task Clean {
    if (Test-Path -LiteralPath $Manifest.ReleasePath) {
        Remove-Item -LiteralPath $Manifest.ReleasePath -Recurse -Force
    }
    New-Item -Path $Manifest.ReleasePath -ItemType Directory | Out-Null
}

task BuildManaged {
    $arguments = @(
        'publish'
        '--configuration', $Manifest.Configuration
        '--verbosity', 'quiet'
        '-nologo'
        "-p:Version=$($Manifest.Module.Version)"
    )

    $csproj = (Get-Item -Path "$($Manifest.DotnetPath)/*.csproj").FullName
    foreach ($framework in $Manifest.TargetFrameworks) {
        Write-Host "Compiling for $framework" -ForegroundColor Cyan
        $outputDir = [Path]::Combine($Manifest.ReleasePath, "bin", $framework)
        New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
        dotnet @arguments --framework $framework --output $outputDir $csproj

        if ($LASTEXITCODE) {
            throw "Failed to compiled code for $framework"
        }
    }
}

task BuildNative {
    Assert-Detours -RequiredCommit 4b8c659f549b0ab21cf649377c7a84eb708f5e68

    $detoursPath = [Path]::GetFullPath([Path]::Combine($Manifest.OutputPath, "lib", "Detours"))
    $detoursCodePath = [Path]::Combine($detoursPath, "src")
    $codePath = [Path]::Combine($Manifest.DotnetPath, "..", "$($Manifest.Module.Name)Native")
    $outPath = [Path]::Combine($codePath, "bin")
    if (Test-Path -LiteralPath $outPath) {
        Remove-Item -LiteralPath $outPath -Force -Recurse
    }
    New-Item -Path $outPath -ItemType Directory | Out-Null

    $sourceFiles = (Get-ChildItem -Path ([Path]::Combine($codePath, "*.cpp"))).FullName
    $incPaths = @(
        '/I', [Path]::Combine($codePath, 'include')
        '/I', [Path]::Combine($detoursPath, 'include')
    )
    $defPaths = @(Get-ChildItem -Path ([Path]::Combine($codePath, "*.def")) | ForEach-Object { "/DEF:`"$($_.FullName)`"" })
    $preprocessorDefines = @('/D', 'WINDOWS')
    $compilerArgs = @('/EHa', '/Od', '/GS', '/sdl', '/Zi', '/std:c++17', '/LD')

    foreach ($arch in @('x64', 'arm64')) {
        Invoke-WithDevToolEnvironment -Architecture $arch -ScriptBlock {
            Write-Host "Compiling Detours for $arch"
            Push-Location -LiteralPath $detoursCodePath
            try {
                $env:DETOURS_TARGET_PROCESSOR = $arch.ToUpperInvariant()
                nmake.exe
                if ($LASTEXITCODE) {
                    throw "Failed to compiled Detours for $arch"
                }
            }
            finally {
                $env:DETOURS_TARGET_PROCESSOR = ''
                Pop-Location
            }

            $outArchPath = [Path]::Combine($outPath, $arch)
            New-Item -Path $outArchPath -ItemType Directory | Out-Null

            $libPaths = @(
                "/LIBPATH:`"$([Path]::Combine($detoursPath, "lib.$($arch.ToUpperInvariant())"))`""
            )
            $outDllPath = [Path]::Combine($outArchPath, "$($Manifest.Module.Name)Native.dll")
            $arguments = @(
                $sourceFiles
                $incPaths
                $preprocessorDefines
                $compilerArgs
                '/link'
                $libPaths
                $defPaths
                'detours.lib'
                '/ignore:4099'
                "/out:`"$outDllPath`""
            )

            Write-Host "Compiling $($Manifest.Module.Name)Native for $arch`n$arguments"
            Push-Location -LiteralPath $outArchPath
            try {
                & {
                    $PSNativeCommandArgumentPassing = 'Legacy'
                    cl.exe @arguments
                }
                if ($LASTEXITCODE) {
                    throw "Failed to compile $($Manifest.Module.Name)Native for $arch"
                }
            }
            finally {
                Pop-Location
            }

            $nativeReleasePath = [Path]::Combine($Manifest.ReleasePath, 'bin', $arch)
            if (-not (Test-Path -LiteralPath $nativeReleasePath)) {
                New-Item -Path $nativeReleasePath -ItemType Directory | Out-Null
            }

            $destOutDllPath = [Path]::Combine($nativeReleasePath, "$($Manifest.Module.Name)Native.dll")
            Copy-Item -LiteralPath $outDllPath -Destination $destOutDllPath
        }
    }
}

task BuildModule {
    $copyParams = @{
        Path = [Path]::Combine($Manifest.PowerShellPath, '*')
        Destination = $Manifest.ReleasePath
        Recurse = $true
        Force = $true
    }
    Copy-Item @copyParams
}

task BuildDocs {
    Get-ChildItem -LiteralPath $Manifest.DocsPath -Directory | ForEach-Object {
        Write-Host "Building docs for $($_.Name)" -ForegroundColor Cyan
        $outputPath = [Path]::Combine($Manifest.ReleasePath, $_.Name)
        New-Item -Path $outputPath -ItemType Directory -Force | Out-Null

        $moduleDocs = [Path]::Combine($_.FullName, $Manifest.Module.Name)
        $commandFiles = Measure-PlatyPSMarkdown -Path ([Path]::Combine($moduleDocs, '*.md')) |
            Where-Object { $_.FileType -band 'CommandHelp' } |
            Select-Object -ExpandProperty FilePath
        $commandHelp = Import-MarkdownCommandHelp -Path $commandFiles

        # Export-MamlCommandHelp writes to a sub folder named after the
        # module, stage it and move the xml into the culture folder.
        $stagingPath = [Path]::Combine($Manifest.OutputPath, 'maml', $_.Name)
        if (Test-Path -LiteralPath $stagingPath) {
            Remove-Item -LiteralPath $stagingPath -Recurse -Force
        }
        Export-MamlCommandHelp -CommandHelp $commandHelp -OutputFolder $stagingPath -Force |
            Move-Item -Destination $outputPath -Force

        # PlatyPS no longer converts about topics, the help system reads the
        # markdown as plain text just fine.
        Get-ChildItem -LiteralPath $moduleDocs -Filter 'about_*.md' -File | ForEach-Object {
            $dest = [Path]::Combine($outputPath, "$($_.BaseName).help.txt")
            Copy-Item -LiteralPath $_.FullName -Destination $dest
        }
    }
}

task SignModule {
    $accountName = $env:AZURE_TS_NAME
    $profileName = $env:AZURE_TS_PROFILE
    $endpoint = $env:AZURE_TS_ENDPOINT
    if (-not $accountName -or -not $profileName -or -not $endpoint) {
        throw "AZURE_TS_NAME, AZURE_TS_PROFILE and AZURE_TS_ENDPOINT must be set to sign the module"
    }

    Write-Host "Authenticating with Azure TrustedSigning $accountName $profileName for signing" -ForegroundColor Cyan
    $keyParams = @{
        AccountName = $accountName
        ProfileName = $profileName
        Endpoint = $endpoint
    }
    $key = Get-OpenAuthenticodeAzTrustedSigner @keyParams
    $signParams = @{
        Key = $key
        TimeStampServer = 'http://timestamp.acs.microsoft.com'
    }

    $toSign = Get-ChildItem -LiteralPath $Manifest.ReleasePath -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Extension -in ".ps1", ".psm1", ".psd1", ".ps1xml" -or (
                $_.Extension -eq ".dll" -and $_.BaseName -like "$($Manifest.Module.Name)*"
            )
        } |
        ForEach-Object -Process {
            Write-Host "Signing '$($_.FullName)'"
            $_.FullName
        }

    Set-OpenAuthenticodeSignature -LiteralPath $toSign @signParams
}

task Package {
    $repoParams = @{
        Name = "$($Manifest.Module.Name)-Local"
        Uri = $Manifest.OutputPath
        Trusted = $true
        Force = $true
    }
    Register-PSResourceRepository @repoParams
    try {
        Publish-PSResource -Path $Manifest.ReleasePath -Repository $repoParams.Name -SkipModuleManifestValidate
    }
    finally {
        Unregister-PSResourceRepository -Name $repoParams.Name
    }
}

#endregion Build

#region Test

task TestSetup {
    $wildcardBase = ".*$([regex]::Escape([Path]::DirectorySeparatorChar))"
    $watchFolder = [Path]::Combine($Manifest.ReleasePath, 'bin', $Manifest.TestFramework)

    # This is used in unit tests to restrict the coverage collector to only the
    # module assemblies. Only the dlls with a pdb file are generated by us. We
    # cannot rely on the default in case external pdbs are found by dotnet.
    # The integration tests ignore this option as PesterTests instruments the
    # same assemblies explicitly.
    $includedAssemblies = @(
        Get-ChildItem -LiteralPath $watchFolder -Filter "*.pdb" |
            ForEach-Object {
                "$wildcardBase$([regex]::Escape($_.BaseName))\.dll$"
            }
    )

    $config = @{
        codeCoverage = @{
            Configuration = @{
                Format = 'cobertura'
                DeterministicReport = $env:GITHUB_ACTIONS -eq 'true'
                # Treats the code after a call to a [DoesNotReturn] method in
                # another assembly, like Cmdlet.ThrowTerminatingError, as
                # unreachable. The default only looks in the same assembly.
                DoesNotReturnAttribute = 'AllAssemblies'
                CodeCoverage = @{
                    ModulePaths = @{
                        Include = $includedAssemblies
                    }
                }
            }
        }
    }
    $configJson = $config | ConvertTo-Json -Depth 5
    Set-Content -Path $Manifest.TestSettingsPath -Value $configJson -Encoding UTF8
}

task UnitTests {
    $testsPath = [Path]::Combine($Manifest.TestPath, 'units')
    if (-not (Test-Path -LiteralPath $testsPath)) {
        Write-Host "No unit tests found, skipping" -ForegroundColor Yellow
        return
    }

    Get-ChildItem -LiteralPath $testsPath -Directory | ForEach-Object {
        Write-Host "Running unit tests for $($_.Name)" -ForegroundColor Cyan

        $coveragePath = [Path]::Combine($Manifest.TestResultsPath, "Unit.$($_.Name).Coverage.cobertura.xml")
        $arguments = @(
            'test'
            '--project', $_.FullName
            '--configuration', $Manifest.Configuration
            '--results-directory', $Manifest.TestResultsPath
            '--coverage'
            '--coverage-output', $coveragePath
            '--coverage-settings', $Manifest.TestSettingsPath
        )

        dotnet @arguments
        if ($LASTEXITCODE) {
            throw "Unit tests $($_.Name) failed"
        }
    }
}

task BuildTestNativeLib {
    Invoke-WithDevToolEnvironment -ScriptBlock {
        $examplesPath = [Path]::Combine($Manifest.TestPath, "NativeExamples")
        $outExamplesPath = [Path]::Combine($examplesPath, "bin")
        if (-not (Test-Path -LiteralPath $outExamplesPath)) {
            New-Item -Path $outExamplesPath -Force -ItemType Directory | Out-Null
        }

        $sourceFiles = (Get-ChildItem -Path ([Path]::Combine($examplesPath, "*.c"))).FullName
        $defPaths = @(Get-ChildItem -Path ([Path]::Combine($examplesPath, "*.def")) | ForEach-Object { "/DEF:`"$($_.FullName)`"" })
        $compilerArgs = @('/EHa', '/Od', '/GS', '/sdl', '/Zi', '/std:c++17', '/LD')
        $arguments = @(
            $sourceFiles
            $compilerArgs
            '/link'
            $defPaths
            "/out:`"$([Path]::Combine($outExamplesPath, "NativeExamples.dll"))`""
        )

        Write-Host "Compiling NativeExamples`n$arguments"
        Push-Location -LiteralPath $outExamplesPath
        try {
            & {
                $PSNativeCommandArgumentPassing = 'Legacy'
                cl.exe @arguments
            }
            if ($LASTEXITCODE) {
                throw "Failed to compile NativeExamples for $arch"
            }
        }
        finally {
            Pop-Location
        }
    }
}

task PesterTests {
    $testsPath = [Path]::Combine($Manifest.TestPath, '*.tests.ps1')
    if (-not (Test-Path -Path $testsPath)) {
        Write-Host "No Pester tests found, skipping" -ForegroundColor Yellow
        return
    }

    $dotnetTools = @(dotnet tool list --global) -join "`n"
    if (-not $dotnetTools.Contains('dotnet-coverage')) {
        Write-Host 'Installing dotnet tool dotnet-coverage' -ForegroundColor Yellow
        dotnet tool install --global dotnet-coverage
    }

    $pwsh = Assert-PowerShell -Version $Manifest.PowerShellVersion -Arch $Manifest.PowerShellArch
    $resultsFile = [Path]::Combine($Manifest.TestResultsPath, 'Pester.xml')
    if (Test-Path -LiteralPath $resultsFile) {
        Remove-Item $resultsFile -ErrorAction Stop -Force
    }
    $pesterScript = [Path]::Combine($PSScriptRoot, 'PesterTest.ps1')
    $pwshArguments = @(
        '-NoProfile'
        '-NonInteractive'
        if ($IsWindows) {
            '-ExecutionPolicy', 'Bypass'
        }
        '-File', $pesterScript
        '-TestPath', $Manifest.TestPath
        '-OutputFile', $resultsFile
        '-PesterVersion', $Manifest.PesterVersion
    )

    $watchFolder = [Path]::Combine($Manifest.ReleasePath, 'bin', $Manifest.TestFramework)
    $coveragePath = [Path]::Combine($Manifest.TestResultsPath, "Integration.Coverage.cobertura.xml")
    $pwshHome = Split-Path -Path $pwsh -Parent

    # Our assemblies are the ones with a pdb, dotnet-coverage collect
    # instruments them for the run with --include-files.
    $includeFiles = @(
        Get-ChildItem -LiteralPath $watchFolder -Filter "*.pdb" |
            ForEach-Object {
                [Path]::Combine($watchFolder, "$($_.BaseName).dll")
            }
    )

    # DoesNotReturnAttribute = AllAssemblies needs the instrumenter to resolve
    # S.M.A next to our assemblies to see any pwsh [DoesNotReturn] attribute,
    # for example Cmdlet.ThrowTerminatingError, and not include the return
    # path as a missed coverage branch. The S.M.A of the pwsh under test is
    # copied there for the run, the module still uses the copy pwsh has
    # already loaded.
    $smaPath = [Path]::Combine($watchFolder, 'System.Management.Automation.dll')
    $pwshSma = & $pwsh -NoProfile -NonInteractive -Command '[PSObject].Assembly.Location'
    if ($LASTEXITCODE -or -not $pwshSma) {
        throw "Failed to find System.Management.Automation.dll of '$pwsh'"
    }

    $arguments = @(
        'collect'
        $pwsh
        $pwshArguments
        '--output', $coveragePath
        '--settings', $Manifest.TestSettingsPath
        foreach ($file in $includeFiles) {
            '--include-files', $file
        }
    )

    $origEnv = $env:PSModulePath
    try {
        Copy-Item -LiteralPath $pwshSma -Destination $smaPath

        $env:PSModulePath = @(
            [Path]::Combine($pwshHome, "Modules")
            [Path]::Combine($Manifest.OutputPath, "Modules")
        ) -join ([Path]::PathSeparator)

        dotnet-coverage @arguments
    }
    finally {
        Remove-Item -LiteralPath $smaPath -Force -ErrorAction Ignore
        $env:PSModulePath = $origEnv
    }

    if ($LASTEXITCODE) {
        throw "Pester failed tests"
    }
}

task CoverageReport {
    $dotnetTools = @(dotnet tool list --global) -join "`n"
    if (-not $dotnetTools.Contains('dotnet-reportgenerator-globaltool')) {
        Write-Host 'Installing dotnet tool dotnet-reportgenerator-globaltool' -ForegroundColor Yellow
        dotnet tool install --global dotnet-reportgenerator-globaltool
    }

    $mergedCoveragePath = [Path]::Combine($Manifest.TestResultsPath, "Coverage.cobertura.xml")
    if (Test-Path -LiteralPath $mergedCoveragePath) {
        Remove-Item $mergedCoveragePath -Force
    }

    # ReportGenerator merges the unit test and Pester reports by file and
    # line, keeping the line and branch coverage of both.
    $coverageFiles = Get-ChildItem -Path $Manifest.TestResultsPath -Filter "*.Coverage.cobertura.xml"
    $reportPath = [Path]::Combine($Manifest.TestResultsPath, "CoverageReport")
    $reportArgs = @(
        "-reports:$($coverageFiles.FullName -join ';')"
        "-sourcedirs:$($Manifest.RepositoryPath)/src"
        "-targetdir:$reportPath"
        '-filefilters:-*.g.cs'  # Filter out source generated files
        '-reporttypes:Html_Dark;JsonSummary;Cobertura'
    )
    reportgenerator @reportArgs
    if ($LASTEXITCODE) {
        throw "reportgenerator failed with RC of $LASTEXITCODE"
    }

    $mergedReport = [Path]::Combine($reportPath, 'Cobertura.xml')
    Copy-Item -LiteralPath $mergedReport -Destination $mergedCoveragePath

    $coverageScript = [Path]::Combine($PSScriptRoot, 'CoverageReport.ps1')
    & $coverageScript -Path $mergedCoveragePath
}

#endregion Test

task Build -Jobs Clean, BuildManaged, BuildNative, BuildModule, BuildDocs, Package

task Test -Jobs TestSetup, UnitTests, BuildTestNativeLib, PesterTests, CoverageReport

# build.ps1 expands the -ModuleNupkg into the release path first.
task Sign -Jobs SignModule, Package
