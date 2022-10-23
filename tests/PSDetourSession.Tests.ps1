. ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "New-PSDetourSession" {
    It "Creates pssession in remote process" {
        $proc = Start-Process -FilePath cmd.exe -WindowStyle Hidden -PassThru
        try {
            $session = $proc | New-PSDetourSession
            $actual = Invoke-Command -Session $session -ScriptBlock { $pid }

            $actual | Should -Be $proc.Id
        }
        finally {
            if ($session) { $session | Remove-PSSession }
            $proc | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }

    It "Creates pssession with arguments" {
        $proc = Start-Process -FilePath cmd.exe -WindowStyle Hidden -PassThru
        try {
            $session = $proc.Id | New-PSDetourSession -ApplicationArguments @{foo = 'bar' }
            $actual = Invoke-Command -Session $session -ScriptBlock { $PSSenderInfo.ApplicationArguments }

            $actualKeys = [string[]]@($actual.Keys | Sort-Object)
            $actualKeys.Count | Should -Be 2
            $actualKeys[0] | Should -Be foo
            $actualKeys[1] | Should -Be PSVersionTable
            $actual.foo | Should -Be bar
            $actual.PSVersionTable.PSVersion | Should -Be $PSVersionTable.PSVersion
        }
        finally {
            if ($session) { $session | Remove-PSSession }
            $proc | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }

    It "Creates pssession with loaded PSDetour module" {
        $expected = Get-Module -Name PSDetour
        $proc = Start-Process -FilePath cmd.exe -WindowStyle Hidden -PassThru
        try {
            $session = $proc.Id | New-PSDetourSession
            $actual = Invoke-Command -Session $session -ScriptBlock { Get-Module -Name PSDetour }

            $actual.Name | Should -Be $expected.Name
            $actual.ModuleBase | Should -Be $expected.ModuleBase
            $actual.Version | Should -Be $expected.Version
        }
        finally {
            if ($session) { $session | Remove-PSSession }
            $proc | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}
