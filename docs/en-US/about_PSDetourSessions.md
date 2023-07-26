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

Another option is to use `Trace-PSDetourProcess` to create a session in another process with a special state helper with the hooks.
This special state object can be used to more easily send data back to the calling pipeline and provide more host interaction which might not be present when tracing other processes.
The following code can be used to trace another process

```powershell
$hooks = New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId -Action {
    [OutputType([int])]
    param ()

    $this.State.WriteObject("object")

    $this.Invoke()
}

# Will block until the process has ended or the hook has called StopTrace.
Trace-PSDetourProcess -ProcessId $otherProc -Hooks $hooks
```

To stop a trace, press `ctrl+c`, or ensure one of your hooks sends the `StopTrace` signal.
The `State` object set by `Trace-PSDetourProcess` has the following methods:

* `WriteDebug(string text)`
    * Writes a debug record
* `WriteError(ErrorRecord errorRecord)`
    * Writes an error record
* `WriteInformation(InformationRecord informationRecord)`
    * Writes an information record
* `WriteObject(object sendToPipeline)`
    * Writes an object to the output pipeline with a depth of 1
* `WriteObject(object sendToPipeline, int depth)`
    * Writes an object to the output pipeline with a custom depth level
* `WriteProgress(ProgressRecord progressRecord)`
    * Writes a progress record
* `WriteVerbose(string text)`
    * Writes a verbose record
* `WriteWarning(sting text)`
    * Writes a warning record
* `WriteLine()`
    * Writes an empty line to the PSHost, i.e. `$host.UI.WriteLine()`
* `WriteLine(string line, params string[] arg)`
    * Writes a line with optional format string args to the PSHost, i.e. `$host.UI.WriteLine('line')`
* `ReadLine()`
    * Reads a line from the PSHost, i.e. `$host.UI.ReadLine()`
* `ReadLine(string? prompt)`
    * Reads a line from the PSHost with a custom prompt
* `StopTrace()`
    * Stops the current trace to unblock the caller
