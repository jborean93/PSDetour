---
document type: cmdlet
external help file: PSDetour.dll-Help.xml
HelpUri: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/PSDetour/New-PSDetourHook.md
Module Name: PSDetour
ms.date: ''
PlatyPS schema version: 2024-05-01
---

# New-PSDetourHook

## SYNOPSIS

Create a PSDetour hook from a scriptblock.

## SYNTAX

### Default (Default)

```
New-PSDetourHook [-DllName] <String> [-MethodName] <String> [-Action] <ScriptBlock>
 [-Address <IntPtr>] [-AddressIsOffset] [<CommonParameters>]
```

## ALIASES

## DESCRIPTION

Creates a hook object from a PowerShell scriptblock to be used as a hook for a native C function.
The scriptblock should contain an `[OutputType([...])]` attribute to denote what the return value is of the C function.
It should also contain a `param()` block with parameters for each C function argument and their types.
When the hook is run, the `$this` variable contains information about the hook itself and can be used to invoke the actual API through the `Invoke` method.

Take care when typing the return and parameter types as a misconfiguration can cause the whole process to crash.
A parameter can be marked with the `MarshalAs` attribute to denote the marshalling type behaviour to use.
Currently only the `UnmanagedType`, `In`, and `Out` attributes can be set.

See [about_PSDetourMarshalling](./about_PSDetourMarshalling.md) for more information around how to define a hook and its input/output types.

## EXAMPLES

### Example 1 - Hook CreateDirectoryW with string marshalling

```powershell
PS C:\> New-PSDetourHook -DllName Kernel32 -MethodName CreateDirectoryW -Action {
...     [OutputType([bool])]
...     param (
...         [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
...         [string]$PathName,
...         [IntPtr]$SecurityAttributes
...     )
...
...     # Calling $this.Invoke will invoke the actual function and return the value
...     $this.Invoke($PathName, $SecurityAttributes)
... }
```

This example will hook the [CreateDirectoryW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createdirectoryw).
The `OutputType` is denoted as `bool` to reflect the `BOOL` return value of the function.
The `$PathName` parameter is documented as a `string` and is also marked as an `UnmanagedType.LPWStr` to help dotnet know it's a Unicode string.
Without this attribute the string will be an `ANSI` string giving the wrong value.
The `$this.Invoke` method is called to invoke the real `CreateDirectoryW` and the output is implicitly returned back to the caller.
Only the last output from the scriptblock action is going to be returned, in this case there is only the output from `$this.Invoke`.

### Example 2 - Hook CreateDirectoryW with raw IntPtr paths

```powershell
PS C:\> New-PSDetourHook -DllName Kernel32 -MethodName CreateDirectoryW -Action {
...     [OutputType([bool])]
...     param (
...         [IntPtr]$PathName,
...         [IntPtr]$SecurityAttributes
...     )
...
...     # Calling $this.Invoke will invoke the actual function and return the value
...     $res = $this.Invoke($PathName, $SecurityAttributes)
...
...     if (-not $res) {
...        $path = "\\?\" + [System.Runtime.InteropServices.Marshal]::PtrToStringUni($PathName)
...        $newPath = [System.Runtime.InteropServices.Marshal]::StringToHGlobalUni($path)
...        $res = $this.Invoke($newPath, $SecurityAttributes)
...        [System.Runtime.InteropServices.Marshal]::FreeHGlobal($newPath)
...     }
...
...     $res
... }
```

This is like the first example but the `$Pathname` is coming in as a raw `IntPtr`.
This `IntPtr` can be passed along directly but it can also be converted back to a string using normal dotnet methods.
In this example it will try to invoke the C function and have a fallback to prefix the path with `\\?\` if that fails.

## PARAMETERS

### -Action

The scriptblock to run when the C function is called.
The `[OutputType([type])]` should be defined as the return value type of the C function.
Parameters should also be defined in the `param()` block for each argument of the C function and their respective types.
Any variables that reference variables outside of the `-Action` should be prefixed with `$using:VariableName`.

```yaml
Type: ScriptBlock
DefaultValue: None
SupportsWildcards: false
Aliases:
- ScriptBlock
ParameterSets:
- Name: (All)
  Position: 3
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Address

Hook the method at the address specified.
This can be used to hook methods that are not publicly exported by name.
The `-DllName` and `-MethodName` must still be set to provide enough metadata for the reflected method to be generated.
By default the `Address` is the offset in the current process but `-AddressIsOffset` can be used to specify the address is an offset from the loaded `DllName` address of the process.

```yaml
Type: IntPtr
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

### -AddressIsOffset

Will automatically calculated the actual method address from the loaded Dll module specified by `-DllName` based on the `-Address` value.
For example if `Kernel32.dll` is loaded at the address `0x0000F000` and the specified address is `-Address 0x0001`, the final method address will be `0x0000F001`.

```yaml
Type: SwitchParameter
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

### -DllName

The DLL name or path where the C function to hook is defined.

```yaml
Type: String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 1
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -MethodName

The C function name/symbol to hook.

```yaml
Type: String
DefaultValue: None
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: 2
  IsRequired: true
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

### None

## OUTPUTS

### PSDetour.Commands.ScriptBlockHook

An object that contains the necessary information for `Start-PSDetour` to setup the required hooks.

## NOTES

## RELATED LINKS

- [about_PSDetour](./about_PSDetour.md)

