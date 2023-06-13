. ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "Start|Stop-PSDetour" {
    It "Hooks a void method" {
        $state = @{}
        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName Sleep -Action {
                param([int]$Milliseconds)

                # By referencing $using: it will run in a new runspace
                ($using:state)['args'] = $Milliseconds
                ($using:state)['rpid'] = [Runspace]::DefaultRunspace.Id

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $state.args | Should -Be 5
        $state.rpid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
    }

    It "Hooks a method by address" {
        $lib = [System.Runtime.InteropServices.NativeLibrary]::Load("Kernel32.dll")
        $addr = [System.Runtime.InteropServices.NativeLibrary]::GetExport($lib, "Sleep")
        $state = @{}
        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32 -MethodName Sleep -Address $addr -Action {
                param([int]$Milliseconds)

                # By referencing $using: it will run in a new runspace
                ($using:state)['args'] = $Milliseconds
                ($using:state)['rpid'] = [Runspace]::DefaultRunspace.Id

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $state.args | Should -Be 5
        $state.rpid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
    }

    It "Hooks a method by address offset" {
        $lib = [System.Runtime.InteropServices.NativeLibrary]::Load("Kernel32.dll")
        $methodAddr = [System.Runtime.InteropServices.NativeLibrary]::GetExport($lib, "Sleep")
        $addr = [IntPtr]($methodAddr.ToInt64() - $lib.ToInt64())
        $state = @{}
        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32 -MethodName Sleep -Address $addr -AddressIsOffset -Action {
                param([int]$Milliseconds)

                # By referencing $using: it will run in a new runspace
                ($using:state)['args'] = $Milliseconds
                ($using:state)['rpid'] = [Runspace]::DefaultRunspace.Id

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $state.args | Should -Be 5
        $state.rpid | Should -Not -Be ([Runspace]::DefaultRunspace.Id)
    }

    It "Hooks method in same runspace if no using vars are present" {
        $state = @{}
        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName Sleep -Action {
                param([int]$Milliseconds)

                $state.args = $Milliseconds
                $state.rpid = [Runspace]::DefaultRunspace.Id

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $state.args | Should -Be 5
        $state.rpid | Should -Be ([Runspace]::DefaultRunspace.Id)
    }

    It "Hooks method with custom state object" {
        $customState = [PSCustomObject]@{
            Args = $null
            RPid = $null
            Type = $null
        }
        Start-PSDetour -State $customState -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName Sleep -Action {
                param([int]$Milliseconds)

                $this.State.Args = $Milliseconds
                $this.State.RPid = [Runspace]::DefaultRunspace.Id
                $this.State.Type = $this.State.GetType().Name

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $customState.Args | Should -Be 5
        $customState.RPid | Should -Be ([Runspace]::DefaultRunspace.Id)
        $customState.Type | Should -Be 'PSCustomObject'
    }

    It "Hooks a method with no parameters" {
        $expected = $pid
        $state = @{}

        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName GetCurrentProcessId -Action {
                [OutputType([int])]
                param()

                ($using:state)['res'] = $this.Invoke()

                1
            }
        )
        $actual = [PSDetourTest.Native]::GetCurrentProcessId()
        Stop-PSDetour

        $actual | Should -Be 1
        $state.res | Should -Be $expected
    }

    It "Hooks a method with custom marshal as attributes" {
        $state = @{}

        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName CreateSymbolicLinkW -Action {
                [OutputType([bool])]
                [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::U1)]
                param (
                    [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
                    [string]$Symlink,

                    [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
                    [string]$Target,

                    [int]$Flags
                )

                ($using:state)['args'] = @($Symlink, $Target, $Flags)
                ($using:state)['res'] = $this.Invoke($Symlink, $Target, $Flags)

                $true
            }
        )

        $actual = [PSDetourTest.Native]::CreateSymbolicLinkW(":symlink:", "target", 1)
        Stop-PSDetour

        $state.args.Count | Should -Be 3
        $state.args[0] | Should -Be ':symlink:'
        $state.args[1] | Should -Be target
        $state.args[2] | Should -Be 1
        $state.res | Should -BeFalse
        $actual | Should -BeTrue
    }

    It "Hooks a method with a reference argument" {
        $state = @{}

        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Advapi32.dll -MethodName OpenProcessToken -Action {
                [OutputType([bool])]
                param (
                    [IntPtr]$Process,
                    [int]$Access,
                    [PSDetour.Ref[IntPtr]]$Token
                )

                ($using:state)['args'] = @($Process, $Access, $Token.Value)
                ($using:state)['res'] = $this.Invoke($Process, $Access, $Token)
                ($using:state)['ref'] = $Token.Value

                1  # Tests it ignores intermediate output
                $false
            }
        )

        $token = [IntPtr]1
        $actual = [PSDetourTest.Native]::OpenProcessToken([IntPtr]-1, 8, [ref]$token)
        Stop-PSDetour

        [PSDetourTest.Native]::CloseHandle($token)

        $token | Should -Not -Be ([IntPtr]1)
        $state.args.Count | Should -Be 3
        $state.args[0] | Should -Be ([IntPtr]-1)
        $state.args[1] | Should -Be 8
        $state.args[2] | Should -Be ([IntPtr]1)
        $state.ref | Should -Be $token
        $state.res | Should -BeTrue
        $actual | Should -BeFalse
    }

    It "Hooks a method with a reference argument and [ref] invoke" {
        $state = @{}

        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Advapi32.dll -MethodName OpenProcessToken -Action {
                [OutputType([bool])]
                param (
                    [IntPtr]$Process,
                    [int]$Access,
                    [PSDetour.Ref[IntPtr]]$Token
                )

                $internalToken = $Token.Value
                ($using:state)['args'] = @($Process, $Access, $Token.Value)
                ($using:state)['res'] = $this.Invoke($Process, $Access, [ref]$internalToken)
                ($using:state)['ref'] = $internalToken
                $Token.Value = $internalToken

                1  # Tests it ignores intermediate output
                $false
            }
        )

        $token = [IntPtr]1
        $actual = [PSDetourTest.Native]::OpenProcessToken([IntPtr]-1, 8, [ref]$token)
        Stop-PSDetour

        [PSDetourTest.Native]::CloseHandle($token)

        $token | Should -Not -Be ([IntPtr]1)
        $state.args.Count | Should -Be 3
        $state.args[0] | Should -Be ([IntPtr]-1)
        $state.args[1] | Should -Be 8
        $state.args[2] | Should -Be ([IntPtr]1)
        $state.ref | Should -Be $token
        $state.res | Should -BeTrue
        $actual | Should -BeFalse
    }

    It "Hooks a method that references another detoured method" {
        $state = @{
            OpenProcess = $false
            OpenProcessToken = $false
            OpenRes = $false
        }

        Start-PSDetour -State $state -Hook @(
            New-PSDetourHook -DllName Advapi32.dll -MethodName OpenProcessToken -Action {
                [OutputType([bool])]
                param (
                    [IntPtr]$Process,
                    [int]$Access,
                    [PSDetour.Ref[IntPtr]]$Token
                )

                $this.State.OpenProcessToken = $true
                $this.Invoke($Process, $Access, $Token)
            }

            New-PSDetourHook -DllName Kernel32.dll -MethodName OpenProcess -Action {
                [OutputType([IntPtr])]
                param (
                    [int]$DesiredAccess,
                    [bool]$InheritHandle,
                    [int]$ProcessId
                )

                $this.State.OpenProcess = $true
                $processHandle = $this.Invoke($DesiredAccess, $InheritHandle, $ProcessId)

                $accessToken = [IntPtr]::Zero
                $this.State.OpenRes = $this.DetouredModules.Advapi32.OpenProcessToken.Invoke(
                    $processHandle,
                    [System.Security.Principal.TokenAccessLevels]::Query,
                    [ref]$accessToken)
                if ($this.State.OpenRes) {
                    [PSDetourTest.Native]::CloseHandle($accessToken)
                }

                $processHandle
            }
        )

        $actual = [PSDetourTest.Native]::OpenProcess(0x0400, $false, $pid)
        Stop-PSDetour
        [PSDetourTest.Native]::CloseHandle($actual)

        $state.OpenProcess | Should -BeTrue
        $state.OpenProcessToken | Should -BeFalse
        $state.OpenRes | Should -BeTrue
    }
}
