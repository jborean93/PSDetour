# PSDetour Parameter and Return Marshalling
## about_PSDetourMarshalling

# SHORT DESCRIPTION
For dotnet to call C functions it needs to know how to convert types that exist in dotnet to types that exist in the C layer.
This document aims to help document the way this is done in PSDetour and some things to lookout for.

# LONG DESCRIPTION
Due to the native of C functions working outside of the dotnet garbage collector (GC) it is important to distinguish these two boundaries.
This guide will use the terms managed code to reference the dotnet side and unmanaged code for the C side.
As unmanaged code is run outside of dotnet it has no ideal how to deal with classes and complex types that you can typically use in PowerShell.
The behaviour around type marshalling in PSDetour is the same behaviour that is used when using PInvoke to call C functions directly in dotnet.
Typically types are split into two difference categories: value types and reference types.

# VALUE TYPES
Value types are more primitive types that typically map to a common byte representation and are passed by value to functions.
Examples of value types are:

* `int` and the various sizes they emcompass like `Int32`, `Byte`, `SByte`, `UInt32`, etc
* `bool`
* `IntPtr`
* `structs`

You can check if a type is a value type by running `$obj.GetType().IsValue` in PowerShell.

Passing an object by value typically means the actual bytes of that object are copied to where it needs to go rather than a reference/pointer being passed.
These value types have a counterpark in the unmanaged code but the names of these types vary from standard to standard.
Due to the simplistic nature of value types, these can be used directly by dotnet when crossing the managed/unmanaged boundary and the marshaler will handle all the work to do so.

One other value type that can be used are structs.
Like integers, bools, structs are also passed by value across the boundary and can contain multiple fields denoted by name.
Dotnet will handle the marshalling of a struct based on the code that was used to define it.
Keep in mind some C APIs use struct references, or pointers to structs.
This is not the same as using the struct type directly on the parameter types but must be passed in as a reference.
See the VALUE PASS BY REFERENCE section for more information.
There are a few builtin structs in dotnet which may map to an actual struct define in the C API.
It is also possible to define custom struct types using `Add-Type` with C# code for use in PSDetours.
Take care that the struct is defined in the process that the hook is running on if dealing with remoted sessions.

When in doubt, search online for what a C type is represented in dotnet PInvoke.
Some common mappings between Win32/C types and dotnet are

|C Type|Dotnet Type|Notes|
|-|-|-|
|VOID|`[void]`||
|HANDLE|`[IntPtr]`||
|BYTE|`[Byte]`||
|SHORT|`[Int16]`||
|WORD|`[UInt16]`|Can typically be used with `[Int16]`|
|INT|`[Int32]`||
|UINT|`[UInt32]`||
|LONG|`[Int32]`||
|DWORD|`[UInt32]`|Can typically be used with `[Int32]`|
|ULONG|`[UInt32]`||
|BOOL|`[bool]`||
|BOOLEAN|`[bool]`|Use with `MarshalAs(UnmanagedType.U1)`|
|CHAR|`[char]`|Use with `MarshalAs(UnamangedType.LPStr)`|
|WCHAR|`[char]`|Use with `MarshalAs(UnamangedType.LPWStr)`|
|LPSTR|`[System.Text.StringBuilder]`|Use with `MarshalAs(UnamangedType.LPStr)`|
|LPCSTR|`[string]`|Use with `MarshalAs(UnamangedType.LPStr)`|
|LPWSTR|`[System.Text.StringBuilder]`|Use with `MarshalAs(UnmanagedType.LPWStr)`|
|LPCWSTR|`[string]`|Use with `MarshalAs(UnmanagedType.LPWStr)`|

The `LPSTR` and `LPCSTR` string are similar with the difference being `LPCSTR` being a constant value.
This means the underlying buffer won't be changed by the C function.
Because an `LPSTR` can be modified by the C function, the `StringBuilder` type is usually recommended for these types.
The same also applies to `LPWSTR` and `LPCWSTR`.
A string type can also use `IntPtr` but will have to be manually converted in the hook if the string value is needed.

Standard convention for Win32 APIs is to prefix types with `P` to denote a pointer to that type.
For example `PHANDLE` is `HANDLE*` or a pointer to `HANDLE`.
These types should be passed in as a by reference value, see VALUE PASS BY REFERENCE for more information.

# REFERENCE TYPES
Reference types are the rest of the types in dotnet.
When calling a function with a reference type as a parameter the value in the background is not passed directly to that function, rather a pointer/reference is used instead.
Like with PInvoke using reference types in dotnet to cross the managed and unmanaged boundary is certainly possible.
A common reference type that is used with PInvoke code is the `string` or `StringBuilder` types.
The dotnet marshaler knows how to pass these types by reference/pointer to the unmanaged code but it is important in PSDetour to denote the `MarshalAs` attribute to denote what type of string it is.
For example the wide variant Win32 APIs, ones ending with W, use wide strings or `LPWSTR` and dotnet needs to know to use the Unicode/UTF-16-LE encoding when marshalling those types.
This is done using the `[System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]` attribute alongside the type declaration.
For example a Win32 C API that has a `LPWSTR` typed argument would look like this as a scriptblook hook.

```powershell
{
    param (
        [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
        [string]$Parameter
    )

    ...
}
```

See the MARSHAL AS ATTRIBUTE section for more details.

Other classes typicall act like struct value types but instead they are always passed by reference.
Typically it's better to use structs when possible as it can be passed by value, and optionally by reference.

# TYPE DEFINITIONS
To declare a return type for a C function hook, the `[OutputType]` attribute is used in the hook scriptblook.
If hooking a C API that returns a `DWORD` then the `[OutputType([Int32])]` or `[OutputType([UInt32])]` type will be used.

For example the `GetCurrentProcessId` function is a simple function without any parameter and a `DWORD` return value.
The scriptblock to hook this in PowerShell would look like

```powershell
{
    [OutputType([int])]
    param ()

    ...
}
```

The return type is simply denoted by the `[OutputType(...)]` attribute.
Another common return type in Windows is the `HANDLE` return type which is essentially a pointer and represented by `IntPtr` in C#.
The `GetCurrentProcess` function is an example of a C API that returns a `HANDLE` and would look like

```powershell
{
    [OutputType([IntPtr])]
    param ()

    ...
}
```

While in PInvoke code, `HANDLE` return types can be defined with a `SafeHandle` return type, it is not recommended to use these for PSDetour hooking.
This is because having a `SafeHandle` defined in the hook might be freed by the GC before it reaches back to the caller invaliding the pointer.
It is possible to define a custom `MarshalAs` behaviour on the return type, see MARSHAL AS ATTRIBUTE for more information.
If no `[OutputType]` is declared in the hook scriptblock, it is assumed the C function as no return type and is `[OutputType([void])]`.

Parameter definitions are simply PowerShell parameters in a `param ()` block.
It is important to specify a specific type for each parameter that fits with the C API being hooked.
For example the `OpenProcess` Win32 function definition looks like the following:

```
HANDLE OpenProcess(
  [in] DWORD dwDesiredAccess,
  [in] BOOL  bInheritHandle,
  [in] DWORD dwProcessId
);
```

The return type is an `IntPtr` to represent the `HANDLE` and the parameters simply match the value types the C types represents.
The scriptblock hook for this function looks like the following:

```powershell
{
    [OutputType([IntPtr])]
    param (
        [int]$Access,
        [bool]$InheritHandle,
        [int]$ProcessId
    )

    ...
}
```

When in doubt as to what types to use, it is best to search online for the PInvoke definitions for these functions and replicate how they are defined in C#.

# MARSHAL AS ATTRIBUTE
The `MarshalAs` attribute is a special attribute used by the dotnet marshaler that provides extra metadata on how it should transfer the arguments across the boundaries.
As mentioned above, the most common use case for this attribute is when marshalling string types and the type of string needs to be known by the marshaler.
To specify these attributes for either the output type or parameter itself, it should simply by provided alongside the definition.

A more complex Win32 function that requires complex marshalling is the `CreateSymbolicLinkW` function.
The C function is defined as

```
BOOLEAN CreateSymbolicLinkW(
    [in] LPCWSTR lpSymlinkFileName,
    [in] LPCWSTR lpTargetFileName,
    [in] DWORD dwFlags
);
```

Not only does it use string for arguments that need to be documented as `LPWSTR` but the return value is a C `BOOLEAN` type.
Unlike a C `BOOL`, a `BOOLEAN` is a single byte value so dotnet needs to know how to marshal this properly.
The scriptblook hook for this function looks like the following:

```powershell
{
    [OutputType([bool])]
    [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::U1)]
    param (
        [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
        [string]$SymlinkFileName,

        [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
        [string]$TargetFileName,

        [int]$Flags
    )
}
```

Currently only a simple `MarshalAs` attribute is supported where the `UnamangedType` can be specified.
More complex scenarios where things like `SizeConst` is used are currently not implemented.

# VALUE PASS BY REFERENCE
Passing a value by reference is a way to pass a value type through a pointer/reference.
This typically means the C function will set the value in the unmanaged side and updates the managed value at the same time.
It is also commonly used when passing in structs by pointers rather than the full value.
Any of these definitions can simply be an `IntPtr` type but it is important to ensure these pointers are valid for the unmanaged code to use.
Using an invalid pointer can either lead to invalid data being used or more commonly crashing the entire process.

Another option to pass a value type by pointer is to use the `PSDetour.Ref<T>` type on the parameter.
This is a special type implemented by PSDetour that documents a parameter as a reference type and what the underlying reference is for.
The Win32 API `OpenProcessToken` is an example where a value is passed by reference.
The C definition for this function looks like:

```
BOOL OpenProcessToken(
  [in]  HANDLE  ProcessHandle,
  [in]  DWORD   DesiredAccess,
  [out] PHANDLE TokenHandle
);
```

The last parameter is shown as an `[out]` type for a `PHANDLE` (`HANDLE*`) which means the `PSDetour.Ref<IntPtr>` type can be used here.
The scriptblock hook for this function looks like the following:

```powershell
{
    [OutputType([bool])]
    param (
        [IntPtr]$Process,
        [int]$Access,
        [PSDetour.Ref[IntPtr]]$Token
    )

    # The $Var.Value property can be used to get/set the value for the caller
    $Token.Value

    # A PSDetour.Ref can be passed directly into the native function as a
    # reference value.
    $this.Invoke($Process, $Access, $Token)

    # Otherwise a temp value of the actual type can still be passed in with
    # [ref]. The value still needs to manually be set back to the PSDetour.Ref
    # variable.
    $tempToken = $Token.Value
    $this.Invoke($Process, $Access, [ref]$tempToken)
    $Token.Value = $tempToken.Value
}
```

The hook can get the input value using `$Var.Value` and can set a value using `$Var.Value = ...`.
The `PSDetour.Ref` type is also special here it can be passed in directly to `$this.Invoke` without having to use `[ref]`.
Only value types can be used with `PSDetour.Ref`.
