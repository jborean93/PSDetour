---
external help file: PSDetour.dll-Help.xml
Module Name: PSDetour
online version: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/New-PSDetourHook.md
schema: 2.0.0
---

# Trace-PSDetourProcess

## SYNOPSIS
Starts a PSDetour hook in either the current process or specified process and provides hook helpers for getting data back.

## SYNTAX

```
Trace-PSDetourProcess [-Hook] <DetourHook[]> [-ProcessId <ProcessIntString>] [-FunctionsToDefine <IDictionary>]
 [<CommonParameters>]
```

## DESCRIPTION
Can be used to easily start a hook session in the same or other process.
The trace will stay alive until the target process has ended or a pipeline stop has been signaled through ctrl+c.
The hook actions will have access to a special helper state that can be used to stream data back to the `Trace-PSDetourProcess` caller's cmdlet like `WriteObject`, `WriteVerbose`, etc.
Keep in mind these objects will be serialized so should only be used for simple objects that are sent back to the caller.
It also provides some PSHost integrations like `WriteLine` to write to the cmdlet's host.

See [PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks) for a collection of hooks that are designed to be used with this cmdlet.

## EXAMPLES

### Example 1 - Hook API in current process
```powershell
PS C:\> $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId {
...     [OutputType([int])]
...     param()
...     $this.Invoke()
... }
PS C:\> $hook | Trace-PSDetourProcess
```

This hooks `GetCurrentProcessId` in the current process and will wait until `ctrl+c` is pressed.

### Example 2 - Hook API in another process
```powershell
PS C:\> $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId {
...     [OutputType([int])]
...     param()
...     $this.Invoke()
... }
PS C:\> $hook | Trace-PSDetourProcess -ProcessId lsass
```

This hooks `GetCurrentProcessId` in the target process called `lsass` and will wait until `ctrl+c` is pressed.

### Example 3 - Writing output back to the caller
```powershell
PS C:\> $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId {
...     [OutputType([int])]
...     param()
...
...     $this.State.WriteObject(@{foo = 'bar'})
...     $this.Invoke()
... }
PS C:\> $hook | Trace-PSDetourProcess
```

Will write the hashtable to the output pipeline of `Trace-PSDetourProcess` whenever the hooked method is called.
Other functions like `WriteVerbose`, `WriteLine`, `ReadLine` can be used on the `State` object, see [about_PSDetour](./about_PSDetour.md) for more information on what these methods are and what they can do.

### Example 4 - Stopping trace in hook

```powershell
PS C:\> $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId {
...     [OutputType([int])]
...     param()
...
...     $this.Invoke()
...     $this.State.StopTrace()
... }
PS C:\> $hook | Trace-PSDetourProcess
```

Hooks the first `GetCurrentProcessId` call and stops the trace once called.

### Example 5 - Define common functions

```powershell
PS C:\> Function My-Function {
...     "output"
... }
PS C:\> $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId {
...     [OutputType([int])]
...     param()
...
...     Set-Item -Path Function:My-Function -Value $this.State.GetFunction("My-Function")
...     $this.State.WriteObject((My-Function))
...
...     $this.Invoke()
... }
PS C:\> $hook | Trace-PSDetourProcess -FunctionsToDefine @{"My-Function" = ${function:My-Function}}
```

Registers the function `My-Function` which can be redefined in the hook scope through `$this.State.GetFunction`.
The function can then be called just like any other PowerShell function.

## PARAMETERS

### -FunctionsToDefine
Common functions to define that the `$this.State` will have access to in the hook.
These function scriptblocks can be access through `$this.State.GetFunction("Function-Name").

```yaml
Type: IDictionary
Parameter Sets: (All)
Aliases: Functions

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Hook
The hooks to load in the target process.

```yaml
Type: DetourHook[]
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
Default value: None
Accept pipeline input: True (ByPropertyName, ByValue)
Accept wildcard characters: False
```

### -ProcessId
The target process to trace as either the process id, process name, or process object.
If a process name is used and multiple processes are found with that name, the cmdlet will output an error.
If not specified, the current process will be traced.

```yaml
Type: ProcessIntString
Parameter Sets: (All)
Aliases: Process

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSDetour.DetourHook[]
The hook as input by value or by name.

## OUTPUTS

### System.Object
Any objects output by the hook's `State` object will be output here. These objects will be serialized so will not be "live" and will be a snapshot of the object that was sent from the hook.

## NOTES
See [PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks) for a collection of various APIs that have been written to be used with this cmdlet.

## RELATED LINKS

[PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks)
