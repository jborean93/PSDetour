# PSDetour
## about_PSDetour

# SHORT DESCRIPTION
The PSDetour module is a PowerShell module that can be used to hook C functions in either the current process or other processes on the host.
It is designed to work with PowerShell 7.2 and newer on Windows using the Detours library from Microsoft to do the actual API hooks.

# LONG DESCRIPTION
A hook is used to replace a C function in a dll that the process may call so that the hook can modify input/output data or just log certain details about the call.
As well as hooking functions in the current process, the module also offers a way to bootstrap the current PowerShell version into another process so it can setup hooks in that process.
For more information around using PSDetour sessions in other processes, see [about_PSDetourSessions](./about_PSDetourSessions.md).

As hooks traverse the boundary between managed (dotnet) and unmanaged (C) code, the way that arguments are transferred across these boundaries is very important.
Using bad type arguments can cause the whole process to crash.
For more information around type managemenet in hooks, see [about_PSDetourMarshalling](./about_PSDetourMarshalling.md).

# HOOK DETAILS
A hook in PSDetour is define by a PowerShell scriptblock.
When the function is hooked, the PowerShell scriptblock will be invoked instead.
A hook can either be run in the same Runspace as where PowerShell is running, or a brand new Runspace is created.
The existing Runspace is used only if:

* The hooked function is running on the same thread as PowerShell itself, and
* No `$using:...` variables were referenced in the hook

If the detoured function was called from another thread, or `$using:...` was used in the action, then the hook is run in a brand new Runspace.
This Runspace won't have access to the parents scope, so any outside information must be passed in with the `$using:...` syntax of through `$this.State` and a state object when starting the detour.

The `$using:` prefix can be used to inject variables from the outside, just like it can be done with `ForEach-Object -Parallel`.
It is up to the autor to define what the scriptblock can do but keep in mind doing nothing could lead to fatal errors as the callers might expect things to happen during that call.
The key components of a scriptblock that is used a hook are:

* `[OutputType]` - to denote the return type of the function to hook
* `param(...)` - all the arguments/parameters and their types of the function to hook
* `$this` - a special variable that contains metadata about the hook, currently only the `Invoke` method and `State` property is implemented.
* `$using:...` - injects the variable named by `...` into the running action hook

An example of a hook that just logs the input parameters and return values would look like:

```powershell
$Hooks = [System.Collections.Generic.List[string]]::new()

# The DllName represents the DLL where the function is defined. The MethodName
# is the name of the function/symbol in that DLL to hook.
$hook = New-PSDetourHook -DllName Kernel32 -MethodName OpenProcess -Action {
    # [OutputType] defines the return type of the hook/
    [OutputType([IntPtr])]
    param (
        # Each parameter represents the function to hooks arguments and their
        # types. Ensure each parameter is typed to the proper type and [object]
        # is not used.
        [int]$Access,
        [bool]$InheritHandle,
        [int]$ProcessId
    )

    # $this.Invoke(...) invokes the actual method being hooked. It takes in
    # the same arguments and return value as the scriptblock definition.
    $res = $this.Invoke($Access, $InheritHandle, $ProcessId)

    # Wrapping $using:Hooks will inject the variable into the action and can
    # be used like any other object.
    ($using:Hooks).Add("OpenProcess($Access, $InheritHandle, $ProcessId) -> $res")

    # The last output value in the scriptblock is treated as the return value.
    # It should match the same `[OutputType]` defined or else `T = default;` is
    # used. Any preceding output is ignored and discarded.
    $res
}
```

The `New-PSDetourHook` only defines the hook metadata, a hook is not enabled until it is passed into `Start-PSDetour`.

The state object is another method that can be used to access data inside the running hook.
Unlike the `$using:...` syntax, or refering to a variable outside, the state object works when the action is run in the current runspace or when a new one is spawned.
The state object can be any object, including a hashtable with custom key/values.
It is specified under the `-State` parameter of `Start-PSDetour` and accessed in the running action through `$this.State` like so:

```powershell
$state = @{
    foo = 'bar'
}

$hook = New-PSDetourHook -DllName Kernel32 -MethodName OpenProcess -Action {
    [OutputType([IntPtr])]
    param (
        [int]$Access,
        [bool]$InheritHandle,
        [int]$ProcessId
    )

    Write-Host "OpenProcess ran with foo state $($this.State.foo)"
    $this.Invoke($Access, $InheritHandle, $ProcessId)

    # The state object can also be modified in the hook action.
    $this.State.foo = 'other'
}

Start-PSDetour -Hook $hook -State $state

...

Stop-PSDetour

# The foo key will be modified if the hook was run
$state
```
