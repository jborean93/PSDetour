$hooks = @(
    New-PSDetourHook -DllName Advapi32 -MethodName OpenSCManagerW -Action {
        [OutputType([IntPtr])]
        param ([IntPtr]$MachineName, [IntPtr]$DatabaseName, [int]$Access)

        Write-Host "OpenSCManagerW MachineName($MachineName) DatabaseName($DatabaseName) Access($Access)"

        $res = $this.Invoke($MachineName, $DatabaseName, $Access)
        Write-Host "OpenSCManagerW res - $($this.Invoke)"
        # $res = [IntPtr]10

        $res
    }
    New-PSDetourHook -DllName Kernel32 -MethodName OpenProcess -Action {
        [OutputType([IntPtr])]
        param ([int]$Access, [bool]$InheritHandle, [int]$ProcessId)

        Write-Host "OpenProcess Access($Access) InheritHandle($InheritHandle) ProcessId($ProcessId)"

        [IntPtr]20
    }
    New-PSDetourHook -DllName Advapi32 -MethodName OpenProcessToken -Action {
        [OutputType([bool])]
        param ([IntPtr]$Process, [int]$Access, [PSDetour.Ref[IntPtr]]$Token)

        Write-Host "OpenProcessToken Process($Process) Access($Access) ProcessId($($Token.Value))"
        $Token.Value = [IntPtr]1234

        $true
    }

)

[PSDetour.Hook]::Start($hooks)

[PSDetour.Hook]::OpenSCManagerW([IntPtr]1, [IntPtr]2, 3)

# [PSDetour.Hook]::OpenProcess(20, $true, 192)

# $token = [IntPtr]5
# [PSDetour.Hook]::OpenProcessToken([IntPtr]1, 2, [ref]$token)
# $token

[PSDetour.Hook]::End()
