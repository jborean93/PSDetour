---
external help file: PSDetour.dll-Help.xml
Module Name: PSDetour
online version: https://github.com/jborean93/PSDetour/blob/main/docs/en-US/New-PSDetourHook.md
schema: 2.0.0
---

# New-PSDetourHook

## SYNOPSIS

Create a PSDetour hook from a scriptblock.

## SYNTAX

```
New-PSDetourHook [-DllName] <String> [-MethodName] <String> [-Action] <ScriptBlock> [<CommonParameters>]
```

## DESCRIPTION

Creates a hook object from a PowerShell scriptblock to be used as a hook for a native C function.
The scriptblock should contain an `[OutputType([...])]` attribute to denote what the return value is of the C function.
It should also contain a `param()` block with parameters for each C function argument and their types.
When the hook is run, the `$this` variable contains information about the hook itself and can be used to invoke the actual API through the `Invoke` method.

Take care when typing the return and parameter types as a misconfiguration can cause the whole process to crash.
A parameter can be marked with the `MarshalAs` attribute to denote the marshalling type behaviour to use.
Currently only the `UnmanagedType`, `In`, and `Out` attributes can be set.

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
Parameter Sets: (All)
Aliases: ScriptBlock

Required: True
Position: 3
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -DllName

The DLL name or path where the C function to hook is defined.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 1
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -MethodName

The C function name/symbol to hook.

```yaml
Type: String
Parameter Sets: (All)
Aliases:

Required: True
Position: 2
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters
This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable. For more information, see [about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None
## OUTPUTS

### PSDetour.Commands.ScriptBlockHook
An object that contains the necessary information for `Start-PSDetour` to setup the required hooks.

## NOTES

## RELATED LINKS
