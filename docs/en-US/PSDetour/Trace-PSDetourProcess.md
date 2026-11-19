---
document type: cmdlet
external help file: PSDetour.dll-Help.xml
HelpUri: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/PSDetour/Trace-PSDetourProcess.md
Module Name: PSDetour
ms.date: ''
PlatyPS schema version: 2024-05-01
---

# Trace-PSDetourProcess

## SYNOPSIS

Starts a PSDetour hook in either the current process or specified process and provides hook helpers for getting data back.

## SYNTAX

### Default (Default)

```
Trace-PSDetourProcess [-Hook] <DetourHook[]> [-ProcessId <ProcessIntString>]
 [-FunctionsToDefine <IDictionary>] [-CSharpToLoad <String[]>] [<CommonParameters>]
```

## ALIASES

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
...     $this.State.WriteObject((My-Function))
...
...     $this.Invoke()
... }
PS C:\> $hook | Trace-PSDetourProcess -FunctionsToDefine @{"My-Function" = ${function:My-Function}}
```

Registers the function `My-Function` which will be redefined in the hook scope when run so can be run like normal.

## PARAMETERS

### -CSharpToLoad

C# code that is loaded into the process before the hooks are run.
Use this to pre-load any C# code that needs to be compiled so that the hooks can access the classes in the definitions.
As a C# assembly cannot be unloaded, the only way to update the C# code being used if changed is to restart the target process.

```yaml
Type: String[]
DefaultValue: None
SupportsWildcards: false
Aliases:
- CSharp
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

### -FunctionsToDefine

Common functions to define in the running hook.
These functions will automatically be redefined in the traced session and can be invoked like normal.

```yaml
Type: IDictionary
DefaultValue: None
SupportsWildcards: false
Aliases:
- Functions
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

### -Hook

The hooks to load in the target process.

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

### -ProcessId

The target process to trace as either the process id, process name, or process object.
If a process name is used and multiple processes are found with that name, the cmdlet will output an error.
If not specified, the current process will be traced.

```yaml
Type: ProcessIntString
DefaultValue: None
SupportsWildcards: false
Aliases:
- Process
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

### PSDetour.DetourHook[]

The hook as input by value or by name.

## OUTPUTS

### System.Object

Any objects output by the hook's `State` object will be output here. These objects will be serialized so will not be "live" and will be a snapshot of the object that was sent from the hook.

## NOTES

See [PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks) for a collection of various APIs that have been written to be used with this cmdlet.

## RELATED LINKS

- [PSDetour-Hooks](https://github.com/jborean93/PSDetour-Hooks)
