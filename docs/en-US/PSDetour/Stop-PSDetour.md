---
document type: cmdlet
external help file: PSDetour.dll-Help.xml
HelpUri: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/PSDetour/Stop-PSDetour.md
Module Name: PSDetour
ms.date: ''
PlatyPS schema version: 2024-05-01
---

# Stop-PSDetour

## SYNOPSIS

Stops a PSDetour session and removes all active hooks.

## SYNTAX

### Default (Default)

```
Stop-PSDetour [<CommonParameters>]
```

## ALIASES

## DESCRIPTION

Stops the active PSDetour session that was started by `Start-PSDetour`.

## EXAMPLES

### Example 1

```powershell
PS C:\> Stop-PSDetour
```

Stops the detour session.

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### System.Object

None

## NOTES

## RELATED LINKS

- [about_PSDetour](./about_PSDetour.md)

