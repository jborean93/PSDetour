@{
    InvokeBuildVersion = '5.14.23'
    PesterVersion = '6.2.0'
    BuildRequirements = @(
        @{
            ModuleName = 'Microsoft.PowerShell.PSResourceGet'
            ModuleVersion = '1.2.0'
        }
        @{
            ModuleName = 'OpenAuthenticode'
            RequiredVersion = '0.6.3'
        }
        @{
            ModuleName = 'Microsoft.PowerShell.PlatyPS'
            RequiredVersion = '1.0.3'
        }
    )
    TestRequirements = @()
}
