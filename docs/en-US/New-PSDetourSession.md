---
external help file: PSDetour.dll-Help.xml
Module Name: PSDetour
online version: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/New-PSDetourHook.md
schema: 2.0.0
---

# New-PSDetourSession

## SYNOPSIS

Creates a PSRemoting session to another process.

## SYNTAX

```
New-PSDetourSession [-ProcessId] <ProcessIntString[]> [-OpenTimeoutMS <Int32>]
 [-ApplicationArguments <PSPrimitiveDictionary>] [-Name <String>] [<CommonParameters>]
```

## DESCRIPTION

Creates a PSRemoting session to another process by injecting a running version of PowerShell into a new thread.
This can be used to run commands remotely in any process and not just PowerShell.

Due to the nature of how PSDetour hooks into the remote process, this is something that cannot be undone.
Once a process has been tainted it will continue to run the PowerShell listener until the target process ends.
See [about_PSDetourSessions](./about_PSDetourSessions.md) for more information.

## EXAMPLES

### Example 1 - Create a remote session to notepad

```powershell
PS C:\> $session = New-PSDetourSsession -ProcessId notepad
PS C:\> Enter-PSSession -Session $session
```

Creates a PSSession to the process called `notepad` and create an interactive prompt for that session.
_Note: This will only work if there is only one process called `notepad`._

### Example 2 - Create a remote session to a process by id

```powershell
PS C:\> $session = New-PSDetourSsession -ProcessId 1234
PS C:\> Invoke-Command -Session $session -ScriptBlock { $pid }
PS C:\> $session | Remove-PSSession
```

Creates a PSSession to the process with the id `1234` and runs the `$pid` command in the session.

## PARAMETERS

### -ApplicationArguments
Any custom arguments to set in the remote session under `$PSSenderInfo.ApplicationArguments`.
The keys must be a string and the values can only be primitive types like integers, strings, etc.

```yaml
Type: PSPrimitiveDictionary
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Name
Custom name to call the remote runspace.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -OpenTimeoutMS
The timeout in milliseconds to wait until the session has been opened.

```yaml
Type: Int32
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -ProcessId

The process to create the remote session to.
This can either be the process id, name, or `Process` object.
If specifying by string/name, only one process of that name must exist.
If there is none or multiple then the cmdlet will output an error.

```yaml
Type: ProcessIntString[]
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

### PSDetour.ProcessIntString[]
The process can be passed in by input.

## OUTPUTS

### System.Management.Automation.Runspaces.PSSession
The PSSession object that can be used with `Invoke-Command`, `Enter-PSSession`, or other PSRemoting cmdlets. Make sure to use `Remove-PSSession` to shut down the session when it's no longer needed.

## NOTES

Currently only x64 process targets are supported. ARM or x86 based processes will most likely fail and crash the target process.
See [Host Error Codes](https://github.com/dotnet/runtime/blob/main/docs/design/features/host-error-codes.md) for a list of return codes that may be used to debug hosting errors.

## RELATED LINKS
