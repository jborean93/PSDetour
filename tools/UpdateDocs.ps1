#!/usr/bin/env pwsh
#Requires -Version 7.4

using namespace Microsoft.PowerShell.PlatyPS.Model
using namespace System.IO

<#
.SYNOPSIS
Regenerates the markdown help from the built module.

.DESCRIPTION
Uses Microsoft.PowerShell.PlatyPS to sync the markdown help under
docs/<locale>/<module> with the built cmdlets. Existing pages keep their prose
while the syntax and parameter metadata is refreshed, new cmdlets get a page
with placeholders to fill in, and the module page is refreshed. Parameters
and input/output types the cmdlet no longer has are removed and ms.date is
left blank so pages only change when their content does.

Run build.ps1 first, it builds the module and downloads the pinned PlatyPS
into output/Modules. Run this in a fresh pwsh process as the built module
cannot be unloaded.

.PARAMETER DocsPath
The locale directory holding the markdown help, defaults to docs/en-US.

.EXAMPLE
./tools/UpdateDocs.ps1
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]
    $DocsPath
)

$ErrorActionPreference = 'Stop'

$projectRoot = [Path]::GetFullPath([Path]::Combine($PSScriptRoot, '..'))
$moduleName = (Get-Item ([Path]::Combine($projectRoot, 'module', '*.psd1'))).BaseName
$modulePath = [Path]::Combine($projectRoot, 'output', $moduleName)
$platyPSPath = [Path]::Combine($projectRoot, 'output', 'Modules', 'Microsoft.PowerShell.PlatyPS')

if (-not $DocsPath) {
    $DocsPath = [Path]::Combine($projectRoot, 'docs', 'en-US')
}

$module = Import-Module -Name $modulePath -PassThru
$platyPS = Import-Module -Name $platyPSPath -PassThru
$moduleDocs = [Path]::Combine($DocsPath, $module.Name)

# PlatyPS 1.0.3 omits [<CommonParameters>] from the syntax generated from a
# live command. Remove this and the HasCmdletBinding fix below once the pinned
# version includes the fix.
# https://github.com/PowerShell/platyPS/issues/865
if ($platyPS.Version -gt [version]'1.0.3') {
    throw "PlatyPS $($platyPS.Version) may include the fix for PowerShell/platyPS#865, verify and remove the HasCmdletBinding workaround in $PSCommandPath or change version check"
}

Function Format-TypeName {
    # Formats a type with PowerShell's generic syntax so the name is readable
    # and PlatyPS can read it back, e.g. System.Collections.Generic.List[System.String]
    # instead of the CLR name System.Collections.Generic.List`1[[System.String, ...]].
    param([Type]$Type)

    if ($Type.IsArray) {
        return "$(Format-TypeName $Type.GetElementType())[$(',' * ($Type.GetArrayRank() - 1))]"
    }
    if ($Type.IsGenericType) {
        $baseName = $Type.GetGenericTypeDefinition().FullName -replace '`\d+$'
        $typeArgs = foreach ($t in $Type.GetGenericArguments()) { Format-TypeName $t }
        return "$baseName[$($typeArgs -join ', ')]"
    }
    $Type.FullName
}

Function Get-HelpTypeInfo {
    # Outputs an input/output entry for each type given, keeping the
    # description of the matching existing entry. The existing names are
    # resolved with the PowerShell type parser so any spelling of a type
    # matches, names that aren't a type, like a PSTypeName, are matched as is
    # (case insensitive).
    param(
        [InputOutput[]]$Existing,
        [string[]]$TypeName
    )

    $placeholder = '{{ Fill in the Description }}'
    $descriptions = @{}
    foreach ($io in $Existing) {
        $type = $io.Typename -as [type]
        $name = $type ? (Format-TypeName $type) : $io.Typename
        if (-not $descriptions[$name] -or $descriptions[$name] -eq $placeholder) {
            $descriptions[$name] = $io.Description
        }
    }

    foreach ($name in $TypeName | Select-Object -Unique) {
        $description = $descriptions[$name] ? $descriptions[$name] : $placeholder
        [InputOutput]::new($name, $description)
    }
}

# The default syntax of a cmdlet with dynamic parameters doesn't show them,
# a note is added below it in the markdown after it is exported. A cmdlet
# whose dynamic parameters come from another command names it here.
$dynamicNote = @{}
$dynamicSource = @{}

$help = foreach ($cmd in Get-Command -Module $module.Name) {
    $mdPath = [Path]::Combine($moduleDocs, "$($cmd.Name).md")
    if (Test-Path -LiteralPath $mdPath) {
        $markdownHelp = Import-MarkdownCommandHelp -LiteralPath $mdPath
        $cmdHelp = Update-CommandHelp -LiteralPath $mdPath

        # Update-CommandHelp appends the Get-Help parameter description, which
        # is the built MAML, when it differs from the markdown. Editing a
        # description and running this before rebuilding would add the old
        # text on every run so always use the markdown description.
        foreach ($param in $cmdHelp.Parameters) {
            $existing = $markdownHelp.Parameters | Where-Object Name -EQ $param.Name
            if ($existing) {
                $param.Description = $existing.Description
            }
        }
    }
    else {
        $cmdHelp = New-CommandHelp -CommandInfo $cmd
    }

    # Remove parameters that no longer exist in the cmdlet. Common parameters
    # like -ProgressAction are covered by the CommonParameters section so
    # remove them too, WhatIf and Confirm are optional common parameters and
    # are documented like any other parameter. Dynamic parameters come from
    # another command and are described in the cmdlet's description with a
    # link to it rather than listed.
    $commonParams = [System.Management.Automation.PSCmdlet]::CommonParameters
    $dynamicParams = @($cmd.Parameters.Values | Where-Object IsDynamic | ForEach-Object Name)
    $null = $cmdHelp.Parameters.RemoveAll({
            $name = $args[0].Name
            -not $cmd.Parameters.ContainsKey($name) -or $name -in $commonParams -or $name -in $dynamicParams
        })

    # Update-CommandHelp keeps the syntax of parameter sets that no longer
    # exist in the cmdlet and doesn't add new ones, the syntax is generated
    # from the cmdlet so take it from New-CommandHelp. Without the dynamic
    # parameters, their parameter sets would all be the same so only the
    # default one is kept, named so the markdown heading says the parameters
    # it is used with aren't shown. The MAML has no parameter set names, and
    # PlatyPS builds its syntax from the parameters rather than this, so
    # Get-Help only shows the parameters that are left.
    $cmdHelp.Syntax.Clear()
    foreach ($syntax in (New-CommandHelp -CommandInfo $cmd).Syntax) {
        $removed = $syntax.SyntaxParameters.RemoveAll({ $args[0].ParameterName -in $dynamicParams })
        if (-not $removed) {
            $cmdHelp.Syntax.Add($syntax)
        }
        elseif ($syntax.IsDefaultParameterSet) {
            $source = $dynamicSource[$cmd.Name]
            if ($source) {
                $setName = "$($source.Name) Dynamic Parameters"
                $note = "The parameters of this parameter set are defined dynamically based on [$($source.Name)]($($source.Uri))."
            }
            else {
                $setName = 'Dynamic Parameters'
                $note = "The parameters of this parameter set are defined dynamically, see the description."
            }

            $renamed = [SyntaxItem]::new($syntax.CommandName, $setName, $true)
            $renamed.SyntaxParameters.AddRange($syntax.SyntaxParameters)
            $cmdHelp.Syntax.Add($renamed)
            $dynamicNote[$cmd.Name] = @{
                SetName = $setName
                Text = "$note`n``Get-Command $($cmd.Name) -Syntax`` shows them."
            }
        }
    }

    # PlatyPS uses the CLR name for the parameter type, e.g.
    # System.Nullable`1[System.Int32], use the same format as the input/output
    # types and drop a top level Nullable as it doesn't matter in PowerShell.
    foreach ($param in $cmdHelp.Parameters) {
        $type = $cmd.Parameters[$param.Name].ParameterType
        $param.Type = Format-TypeName ([Nullable]::GetUnderlyingType($type) ?? $type)
    }

    # Update-CommandHelp keeps every input/output type already in the markdown
    # and only adds new ones, so types the cmdlet no longer declares are never
    # dropped. Both it and New-CommandHelp also add the types from Get-Help,
    # which is the built MAML. Rebuild them from the cmdlet metadata instead.
    $inputTypes = foreach ($param in $cmd.Parameters.Values) {
        $fromPipeline = $param.ParameterSets.Values.Where({
                $_.ValueFromPipeline -or $_.ValueFromPipelineByPropertyName
            })
        if ($fromPipeline) {
            Format-TypeName ([Nullable]::GetUnderlyingType($param.ParameterType) ?? $param.ParameterType)
        }
    }
    $outputTypes = $cmd.OutputType | ForEach-Object {
        # A type may not be resolvable so we just try our best to normalize
        $type = $_.Type
        $type ? (Format-TypeName ([Nullable]::GetUnderlyingType($type) ?? $type)) : $_.Name
    }

    $inputs = Get-HelpTypeInfo -Existing $cmdHelp.Inputs -TypeName $inputTypes
    $cmdHelp.Inputs.Clear()
    $cmdHelp.Inputs.AddRange([InputOutput[]]@($inputs))

    $outputs = Get-HelpTypeInfo -Existing $cmdHelp.Outputs -TypeName $outputTypes
    $cmdHelp.Outputs.Clear()
    $cmdHelp.Outputs.AddRange([InputOutput[]]@($outputs))

    # ms.date is used by Microsoft Learn, PlatyPS sets it to today whenever it
    # generates or updates help. Blank it so pages only change when the
    # content does.
    $cmdHelp.Metadata['ms.date'] = ''

    # Workaround for PlatyPS#865, see the version check above.
    foreach ($syntax in $cmdHelp.Syntax) {
        $syntax.HasCmdletBinding = $cmdHelp.HasCmdletBinding
    }

    $cmdHelp
}

$help | Export-MarkdownCommandHelp -OutputFolder $DocsPath -Force | Out-Null

# PlatyPS has no text for a syntax entry, add the note after the code block
# of the default syntax. Update-CommandHelp drops it when it reads the page
# so it is added each time. Import-MarkdownCommandHelp ignores it when the
# MAML is built.
foreach ($kvp in $dynamicNote.GetEnumerator()) {
    $mdPath = [Path]::Combine($moduleDocs, "$($kvp.Key).md")
    $content = [File]::ReadAllText($mdPath)
    $pattern = "(?s)(### $([Regex]::Escape($kvp.Value.SetName)) \(Default\)\r?\n\r?\n``````\r?\n.*?\r?\n``````\r?\n)"
    if ($content -notmatch $pattern) {
        throw "Failed to find the default syntax of $($kvp.Key) to add the dynamic parameter note"
    }
    $content = $content -replace $pattern, "`$1`n$($kvp.Value.Text.Replace('$', '$$'))`n"
    [File]::WriteAllText($mdPath, $content)
}

$moduleFile = [Path]::Combine($moduleDocs, "$($module.Name).md")
$moduleFileParams = @{
    LiteralPath = $moduleFile
    CommandHelp = $help
    NoBackup = $true
    Force = $true
}
Update-MarkdownModuleFile @moduleFileParams | Out-Null

# Update-MarkdownModuleFile always sets ms.date to today and ends the file
# with a blank line, neither can be controlled so fix up the text.
$content = [File]::ReadAllText($moduleFile)
$content = $content -replace '(?m)^ms\.date: [^\r\n]*', "ms.date: ''"
[File]::WriteAllText($moduleFile, $content.TrimEnd() + [Environment]::NewLine)
