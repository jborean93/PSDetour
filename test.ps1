$hooks = @(
    New-PSDetourHook -DllName Advapi32 -MethodName OpenSCManagerW -Action {
        [OutputType([IntPtr])]
        param ([IntPtr]$MachineName, [IntPtr]$DatabaseName, [int]$Access)

        Write-Host "OpenSCManagerW MachineName($MachineName) DatabaseName($DatabaseName) Access($Access)"

        $res = $this.Invoke($MachineName, $DatabaseName, $Access)
        # $res = [IntPtr]10
        Write-Host "OpenSCManagerW res - $res"

        $res
    }
    New-PSDetourHook -DllName Kernel32 -MethodName OpenProcess -Action {
        [OutputType([IntPtr])]
        param ([int]$Access, [bool]$InheritHandle, [int]$ProcessId)

        Write-Host "OpenProcess Access($Access) InheritHandle($InheritHandle) ProcessId($ProcessId)"
        $res = $this.Invoke($Access, $InheritHandle, $ProcessId)
        Write-Host "OpenProcess res - $res"

        $res
    }
    New-PSDetourHook -DllName Advapi32 -MethodName OpenProcessToken -Action {
        [OutputType([bool])]
        param ([IntPtr]$Process, [int]$Access, [PSDetour.Ref[IntPtr]]$Token)

        Write-Host "OpenProcessToken Process($Process) Access($Access) ProcessId($($Token.Value))"
        [IntPtr]$tokenRef = $Token.Value
        $res = $this.Invoke($Process, $Access, [ref]$tokenRef)
        $Token.Value = $tokenRef
        Write-Host "OpenProcessToken res - $res - $($Token.Value)"

        $res
    }

    New-PSDetourHook -DllName Kernel32 -MethodName GetCurrentProcessId -Action {
        [OutputType([int])]
        param ()

        Write-Host "GetCurrentProcessId - $($this.Invoke)"
        $res = $this.Invoke()
        Write-Host "GetCurrentProcessById res - $res"

        $res
    }

    New-PSDetourHook -DllName Kernel32 -MethodName Sleep -Action {
        param ([int]$Millisecond)

        Write-Host "Sleep Millisecond($Millisecond)"
        $this.Invoke($Millisecond)
        Write-Host "Sleep done"
    }

)

[PSDetour.Hook]::Start($hooks)
# [PSDetour.Hook]::Start()

# [PSDetour.Hook]::OpenSCManagerW([IntPtr]1, [IntPtr]2, 3)
# [PSDetour.Hook]::OpenProcess(20, $true, 192)

$token = [IntPtr]0
[PSDetour.Hook]::OpenProcessToken([IntPtr]-1, 0x0008, [ref]$token)
$token

# [PSDetour.Hook]::GetCurrentProcessId()
# [PSDetour.Hook]::Sleep(5000)

[PSDetour.Hook]::End()
