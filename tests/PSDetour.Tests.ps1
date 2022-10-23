. ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "Start|Stop-PSDetour" {
    It "Hooks a void method" {
        $state = @{}
        Start-PSDetour -Hook @(
            New-PSDetourHook -DllName Kernel32.dll -MethodName Sleep -Action {
                param([int]$Milliseconds)

                ($using:state)['args'] = $Milliseconds

                $this.Invoke($Milliseconds)
            }
        )
        [PSDetourTest.Native]::Sleep(5)
        Stop-PSDetour

        $state.args | Should -Be 5
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
}
