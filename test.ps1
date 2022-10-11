# $hook = New-PSDetourHook -DllName Advapi32 -MethodName OpenSCManagerW -Action {
#     [OutputType([IntPtr])]
#     param ([IntPtr]$MachineName, [IntPtr]$DatabaseName, [int]$Access)

#     Write-Host "OpenSCManagerW MachineName($MachineName) DatabaseName($DatabaseName) Access($Access)"

#     [IntPtr]10
# }

# [PSDetour.Hook]::Start($hook)

# [PSDetour.Hook]::OpenSCManagerW([IntPtr]1, [IntPtr]2, 3)

# [PSDetour.Hook]::End()

# $hook = New-PSDetourHook -DllName Kernel32 -MethodName OpenProcess -Action {
#     [OutputType([IntPtr])]
#     param ([int]$Access, [bool]$InheritHandle, [int]$ProcessId)

#     Write-Host "OpenProcess Access($Access) InheritHandle($InheritHandle) ProcessId($ProcessId)"

#     [IntPtr]20
# }

# [PSDetour.Hook]::Start($hook)

# [PSDetour.Hook]::OpenProcess(20, $true, 192)

# [PSDetour.Hook]::End()

$hook = New-PSDetourHook -DllName Advapi32 -MethodName OpenProcessToken -Action {
    [OutputType([bool])]
    param ([IntPtr]$Process, [int]$Access, [ref][IntPtr]$Token)

    Write-Host "OpenProcessToken Process($Process) Access($Access) ProcessId($($Token.Value))"
    $Token.Value = [IntPtr]1234

    $true
}

[PSDetour.Hook]::Start($hook)

$token = [IntPtr]5
[PSDetour.Hook]::OpenProcessToken([IntPtr]1, 2, [ref]$token)
$token

[PSDetour.Hook]::End()
