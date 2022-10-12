---
external help file: PSDetour.dll-Help.xml
Module Name: PSDetour
online version: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/Start-PSDetour.md
schema: 2.0.0
---

# Start-PSDetour

## SYNOPSIS

Starts a detour session.

## SYNTAX

```
Start-PSDetour [-Hook] <ScriptBlockHook[]> [<CommonParameters>]
```

## DESCRIPTION

Starts a detour session with all the hooks defined in the current session.
Use `New-PSDetourHook` to define the hooks to setup for the session.

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
Type: ScriptBlockHook[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSDetour.Commands.ScriptBlockHook[]
The hook as input by value or by name.

## OUTPUTS

### System.Object
None

## NOTES

## RELATED LINKS
