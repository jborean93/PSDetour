---
document type: cmdlet
external help file: PSDetour.dll-Help.xml
HelpUri: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/PSDetour/Start-PSDetour.md
Module Name: PSDetour
ms.date: ''
PlatyPS schema version: 2024-05-01
---

# Start-PSDetour

## SYNOPSIS

Starts a detour session.

## SYNTAX

### Default (Default)

```
Start-PSDetour [-Hook] <DetourHook[]> [-State <Object>] [<CommonParameters>]
```

## ALIASES

## DESCRIPTION

Starts a detour session with all the hooks defined in the current session.
Use `New-PSDetourHook` to define the hooks to setup for the session.
Once started, the detours will be in place until `Stop-PSDetour` is called.

## EXAMPLES

### Example 1

```powershell
PS C:\> $hook | Start-PSDetour
PS C:\> ... do stuff
PS C:\> Stop-PSDetour
```

Starts the detour session with the hooks specified and does work that should be hooked before stopping the session

## PARAMETERS

### -Hook

The hooks from `New-PSDetourHook` to setup in the detour session.

```yaml
Type: DetourHook[]
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -State

A custom object to inject into the running hooks accessible under `$this.State`.
This can be any object that will be accessible in the hook context regardless of whether it is running in the same Runspace or a custom one.
Alternatively, the `$using:var` syntax can be used which will force the hook to run in a new Runspace with the injected variable present.

```yaml
Type: Object
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSDetour.Commands.ScriptBlockHook[]

The hook as input by value or by name.

## OUTPUTS

### System.Object

None

## NOTES

## RELATED LINKS

- [about_PSDetour](./about_PSDetour.md)

