# PSDetour Process Sessions
## about_PSDetourSessions

# SHORT DESCRIPTION
As well as providing a mechanism to hook C function calls in PowerShell, PSDetour also provides a mechanism to provide remote hooking in other processes on the same system.
This is done through embedding a PowerShell instance in the target process and exposes a normal PSRemoting session that can be used with `Enter-PSSession` or `Invoke-Command`.
This feature is experimental and can have side effects in the target process.
If done incorrectly, the hook can cause the process to crash.

Note that creating a session in a process is an irreversible action.
Once PowerShell has been loaded it will continue to run for the lifetime of the process and cannot be stopped.

# LONG DESCRIPTION
A remote PSDetour session is split into 2 parts:

* PowerShell injection into other process
* PSRemoting through named pipes

The PSRemoting component is a standard feature since PowerShell 5.1 and is simply the `PSHost` named pipe that every PowerShell process creates when it starts.
This named pipe is used after PowerShell is injected to provide the remoting features that can be used with `Enter-PSSession` and `Invoke-Command`.

PowerShell injection into non-PowerShell processes is the more complex component to PSDetour remoting.
Included with this library is a basic x64 DLL that is remotely loaded into the target process.
This DLL will create a new dotnet host that loads the PowerShell assembly and then starts the named pipe for use with PSRemoting.
As this is an x64 compiled DLL, this can currently only be used with x64 processes.
Further architectures will be available in the future.

To create a PSDetour session, use the [New-PSDetourSession](./New-PSDetourSession.md) cmdlet.
This is a simple cmdlet that accepts either a process id, process object, or a process name (as long as there is only one process with that name).
The output object is a `PSSession` object that can be used with any builtin cmdlet that uses the PSSession `-Session` parameter like `Invoke-Command` and `Enter-PSSession`.

For example to start a PSDetour hook in notepad.exe, the process would look something like

```powershell
$session = New-PSDetourSession -ProcessId notepad
Invoke-Command -Session $session -ScriptBlock {
    # The PSDetour module may need to be re-imported depending on where it is installed

    $hook = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId -Action {
        [OutputType([int])]
        param ()

        $this.Invoke()
    }
    Start-PSDetour -Hook $hook

    ... code that waits until the hook needs to be stopped

    Stop-PSDetour
}
```

It can also be used interactively with `Enter-PSSession` which gives the caller more control over when the hooks start and stop.

Once the session is no longer needed, use `Remove-PSSession` to close the connection.
Remember that once a process has been tainted with PSDetour it will still continue to run the listener in the background until the process ends.
