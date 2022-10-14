. ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "New-PSDetourHook" {
    It "Creates hook with no parameters or return value" {
        $dllName = 'Kernel32'
        $methodName = 'Sleep'
        $action = {}

        $actual = New-PSDetourHook -DllName $dllName -MethodName $methodName -Action $action
        $actual.DllName | Should -Be $dllName
        $actual.MethodName | Should -Be $methodName
        $actual.Action | Should -Be $action
        $actual.ReturnType.Type | Should -Be ([void])
        $actual.ReturnType.MarshalAs | Should -BeNullOrEmpty
        $actual.ReturnType.IsIn | Should -Be $false
        $actual.ReturnType.IsOut | Should -Be $false
        $actual.ParameterTypes.Count | Should -Be 0
    }

    It "Creates hook with only return value" {
        $dllName = 'Kernel32'
        $methodName = 'GetProcessById'
        $action = {
            [OutputType([int])]
            param ()

            0
        }

        $actual = New-PSDetourHook -DllName $dllName -MethodName $methodName -Action $action
        $actual.DllName | Should -Be $dllName
        $actual.MethodName | Should -Be $methodName
        $actual.Action | Should -Be $action
        $actual.ReturnType.Type | Should -Be ([int])
        $actual.ReturnType.MarshalAs | Should -BeNullOrEmpty
        $actual.ReturnType.IsIn | Should -Be $false
        $actual.ReturnType.IsOut | Should -Be $false
        $actual.ParameterTypes.Count | Should -Be 0
    }

    It "Creates hook with only return value with simple MarshalAs attribute" {
        $dllName = 'MyDll'
        $methodName = 'SomeMethod'
        $action = {
            [OutputType([bool])]
            [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::U1)]
            param ()

            $false
        }

        $actual = New-PSDetourHook -DllName $dllName -MethodName $methodName -Action $action
        $actual.DllName | Should -Be $dllName
        $actual.MethodName | Should -Be $methodName
        $actual.Action | Should -Be $action
        $actual.ReturnType.Type | Should -Be ([bool])
        $actual.ReturnType.MarshalAs.GetType() | Should -Be ([System.Runtime.InteropServices.MarshalAsAttribute])
        $actual.ReturnType.MarshalAs.Value | Should -Be ([System.Runtime.InteropServices.UnmanagedType]::U1)
        $actual.ReturnType.IsIn | Should -Be $false
        $actual.ReturnType.IsOut | Should -Be $false
        $actual.ParameterTypes.Count | Should -Be 0
    }

    It "Creates hook with parameters and no return value" {
        $dllName = 'MyDll'
        $methodName = 'SomeMethod'
        $action = {
            param (
                [IntPtr]$Reference,
                [int]$Access
            )
        }

        $actual = New-PSDetourHook -DllName $dllName -MethodName $methodName -Action $action
        $actual.DllName | Should -Be $dllName
        $actual.MethodName | Should -Be $methodName
        $actual.Action | Should -Be $action
        $actual.ReturnType.Type | Should -Be ([void])
        $actual.ReturnType.MarshalAs | Should -BeNullOrEmpty
        $actual.ReturnType.IsIn | Should -Be $false
        $actual.ReturnType.IsOut | Should -Be $false
        $actual.ParameterTypes.Count | Should -Be 2

        $actual.ParameterTypes[0].Name | Should -Be Reference
        $actual.ParameterTypes[0].Type | Should -Be ([IntPtr])
        $actual.ParameterTypes[0].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[0].IsIn | Should -Be $false
        $actual.ParameterTypes[0].IsOut | Should -Be $false

        $actual.ParameterTypes[1].Name | Should -Be Access
        $actual.ParameterTypes[1].Type | Should -Be ([int])
        $actual.ParameterTypes[1].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[1].IsIn | Should -Be $false
        $actual.ParameterTypes[1].IsOut | Should -Be $false
    }

    It "Creates hook with complex marshalling" {
        $dllName = 'Kernel32'
        $methodName = 'CreateFileW'
        $action = {
            [OutputType([IntPtr])]
            param (
                [System.Runtime.InteropServices.MarshalAs([System.Runtime.InteropServices.UnmanagedType]::LPWStr)]
                [string]$FileName,

                [int]$Access,

                [System.Runtime.InteropServices.In()]
                [int]$ShareMode,

                [PSDetour.Ref[IntPtr]]$Template,

                [System.Runtime.InteropServices.Out()]
                [int]$Size,

                [System.Runtime.InteropServices.In()]
                [System.Runtime.InteropServices.Out()]
                [int]$Length
            )
        }

        $actual = New-PSDetourHook -DllName $dllName -MethodName $methodName -Action $action
        $actual.DllName | Should -Be $dllName
        $actual.MethodName | Should -Be $methodName
        $actual.Action | Should -Be $action
        $actual.ReturnType.Type | Should -Be ([IntPtr])
        $actual.ReturnType.MarshalAs | Should -BeNullOrEmpty
        $actual.ReturnType.IsIn | Should -Be $false
        $actual.ReturnType.IsOut | Should -Be $false
        $actual.ParameterTypes.Count | Should -Be 6

        $actual.ParameterTypes[0].Name | Should -Be FileName
        $actual.ParameterTypes[0].Type | Should -Be ([string])
        $actual.ParameterTypes[0].MarshalAs.GetType() | Should -Be ([System.Runtime.InteropServices.MarshalAsAttribute])
        $actual.ParameterTypes[0].MarshalAs.Value | Should -Be ([System.Runtime.InteropServices.UnmanagedType]::LPWStr)
        $actual.ParameterTypes[0].IsIn | Should -Be $false
        $actual.ParameterTypes[0].IsOut | Should -Be $false

        $actual.ParameterTypes[1].Name | Should -Be Access
        $actual.ParameterTypes[1].Type | Should -Be ([int])
        $actual.ParameterTypes[1].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[1].IsIn | Should -Be $false
        $actual.ParameterTypes[1].IsOut | Should -Be $false

        $actual.ParameterTypes[2].Name | Should -Be ShareMode
        $actual.ParameterTypes[2].Type | Should -Be ([int])
        $actual.ParameterTypes[2].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[2].IsIn | Should -Be $true
        $actual.ParameterTypes[2].IsOut | Should -Be $false

        $actual.ParameterTypes[3].Name | Should -Be Template
        $actual.ParameterTypes[3].Type | Should -Be ([IntPtr].MakeByRefType())
        $actual.ParameterTypes[3].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[3].IsIn | Should -Be $false
        $actual.ParameterTypes[3].IsOut | Should -Be $false

        $actual.ParameterTypes[4].Name | Should -Be Size
        $actual.ParameterTypes[4].Type | Should -Be ([int])
        $actual.ParameterTypes[4].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[4].IsIn | Should -Be $false
        $actual.ParameterTypes[4].IsOut | Should -Be $true

        $actual.ParameterTypes[5].Name | Should -Be Length
        $actual.ParameterTypes[5].Type | Should -Be ([int])
        $actual.ParameterTypes[5].MarshalAs | Should -BeNullOrEmpty
        $actual.ParameterTypes[5].IsIn | Should -Be $true
        $actual.ParameterTypes[5].IsOut | Should -Be $true
    }
}
