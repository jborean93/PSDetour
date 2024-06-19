. ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))

Describe "Trace-PSDetourProcess" {
    It "Traces current process" {
        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
            param([int]$Milliseconds)

            $this.State.WriteObject($Milliseconds)
        }

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            $ErrorActionPreference = 'Stop'

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $Host
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual | Should -Be 5
    }

    It "Traces current process with outputs" {
        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
            param([int]$Milliseconds)

            $this.State.WriteDebug("debug")
            $this.State.WriteError([System.Management.Automation.ErrorRecord]::new(
                [System.Exception]"Exception",
                "ErrorId",
                "NotSpecified",
                $null
            ))
            $this.State.WriteInformation([System.Management.Automation.InformationRecord]::new(
                "object",
                "source"
            ))
            $this.State.WriteProgress([System.Management.Automation.ProgressRecord]::new(
                1,
                "activity",
                "description"
            ))
            $this.State.WriteVerbose("verbose")
            $this.State.WriteWarning("warning")
            $this.State.WriteLine()
            $this.State.WriteLine("line")
            $res1 = $this.State.ReadLine()
            $this.State.WriteLine($res1)
            $res2 = $this.State.ReadLine("readline prompt")
            $this.State.WriteLine($res2)
            $this.State.WriteObject($Milliseconds)
        }

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess -Debug -Verbose -WarningAction Continue
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $customHost = [PSDetourTest.Host]::new($Host)
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $customHost
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual | Should -Be 5
        $ps.Streams.Error.Count | Should -Be 1
        $ps.Streams.Error[0].ToString() | Should -Be Exception
        $ps.Streams.Progress.Count | Should -Be 1
        $ps.Streams.Progress[0].ActivityId | Should -Be 1
        $ps.Streams.Progress[0].Activity | Should -Be activity
        $ps.Streams.Progress[0].StatusDescription | Should -Be description
        $ps.Streams.Verbose.Count | Should -Be 1
        $ps.Streams.Verbose[0].ToString() | Should -Be verbose
        $ps.Streams.Debug.Count | Should -Be 1
        $ps.Streams.Debug[0].ToString() | Should -Be debug
        $ps.Streams.Warning.Count | Should -Be 1
        $ps.Streams.Warning[0].ToString() | Should -Be warning
        $ps.Streams.Information.Count | Should -Be 1
        $ps.Streams.Information[0].MessageData | Should -Be object
        $ps.Streams.Information[0].Source | Should -Be source

        $customHost.UI.WriteCalls.WriteLine.Count | Should -Be 5
        $customHost.UI.WriteCalls.WriteLine[0] | Should -Be ''
        $customHost.UI.WriteCalls.WriteLine[1] | Should -Be line
        $customHost.UI.WriteCalls.WriteLine[2] | Should -Be 'readline response'
        $customHost.UI.WriteCalls.WriteLine[3] | Should -Be 'readline prompt'
        $customHost.UI.WriteCalls.WriteLine[4] | Should -Be 'readline response'
    }

    It "Traces other process" {
        $proc = $waitEvent1 = $waitEvent2 = $null
        $registeredEvent = $false
        $eventName = [Guid]::NewGuid().Guid

        try {
            $waitEvent1 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-1")
            $waitEvent2 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-2")

            $procScript = {
                $ErrorActionPreference = 'Stop'

                $commonPath = 'REPLACE_COMMON_PATH'
                .$commonPath

                $eventName = 'REPLACE_EVENT_NAME'
                $waitEvent1 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-1")
                $waitEvent2 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-2")
                try {
                    $waitEvent1.Set()
                    if (-not $waitEvent2.WaitOne(5000)) {
                        throw "Timed out waiting to start"
                    }
                    [PSDetourTest.Native]::VoidWithArg(5)
                }
                finally {
                    $waitEvent1.Dispose()
                    $waitEvent2.Dispose()
                }

                "Done"
                Start-Sleep -Seconds 60
            }.ToString() -replace 'REPLACE_EVENT_NAME', $eventName -replace 'REPLACE_COMMON_PATH', ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))
            $encCommand = [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($procScript))

            $procParams = @{
                FilePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                ArgumentList = "-NoProfile -NonInteractive -EncodedCommand $encCommand"
                PassThru = $true
                WindowStyle = 'Hidden'
            }
            $proc = Start-Process @procParams

            $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
                param([int]$Milliseconds)

                $this.State.WriteObject($Milliseconds)
            }

            $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

            $ps = [PowerShell]::Create()
            $null = $ps.AddScript({
                param($ModulePath, $Hook, $Process)

                $ErrorActionPreference = 'Stop'

                Import-Module $ModulePath -ErrorAction Stop

                "started"
                $Hook | Trace-PSDetourProcess -ProcessId $Process
            }).AddParameters(@{ModulePath = $modulePath; Hook = $hook; Process = $proc})

            $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
            $registeredEvent = $true
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }

            if (-not $waitEvent1.WaitOne(5000)) {
                throw "timed out waiting for child process to be ready"
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            $waitEvent2.Set()

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}

        }
        finally {
            if ($waitEvent1) {
                $waitEvent1.Dispose()
            }
            if ($waitEvent2) {
                $waitEvent2.Dispose()
            }
            if ($proc) {
                $proc | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            if ($registeredEvent) {
                Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
                Unregister-Event -SourceIdentifier PSDetourAdded
            }
        }

        $actual | Should -Be 5
    }

    It "Traces other process with enum def" {
        $proc = $waitEvent1 = $waitEvent2 = $null
        $registeredEvent = $false
        $eventName = [Guid]::NewGuid().Guid

        try {
            $waitEvent1 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-1")
            $waitEvent2 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-2")

            $procScript = {
                $ErrorActionPreference = 'Stop'

                $commonPath = 'REPLACE_COMMON_PATH'
                .$commonPath

                $eventName = 'REPLACE_EVENT_NAME'
                $waitEvent1 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-1")
                $waitEvent2 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-2")
                try {
                    $waitEvent1.Set()
                    if (-not $waitEvent2.WaitOne(5000)) {
                        throw "Timed out waiting to start"
                    }
                    [PSDetourTest.Native]::VoidWithArg(0)
                }
                finally {
                    $waitEvent1.Dispose()
                    $waitEvent2.Dispose()
                }

                "Done"
                Start-Sleep -Seconds 60
            }.ToString() -replace 'REPLACE_EVENT_NAME', $eventName -replace 'REPLACE_COMMON_PATH', ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))
            $encCommand = [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($procScript))

            $procParams = @{
                FilePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                ArgumentList = "-NoProfile -NonInteractive -EncodedCommand $encCommand"
                PassThru = $true
                WindowStyle = 'Hidden'
            }
            $proc = Start-Process @procParams

            $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
                param([int]$Milliseconds)

                enum MyEnum {
                    Value1
                }
                $this.State.WriteObject([MyEnum]$Milliseconds)
            }

            $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

            $ps = [PowerShell]::Create()
            $null = $ps.AddScript({
                param($ModulePath, $Hook, $Process)

                $ErrorActionPreference = 'Stop'

                Import-Module $ModulePath -ErrorAction Stop

                "started"
                $Hook | Trace-PSDetourProcess -ProcessId $Process
            }).AddParameters(@{ModulePath = $modulePath; Hook = $hook; Process = $proc})

            $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
            $registeredEvent = $true
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }

            if (-not $waitEvent1.WaitOne(5000)) {
                throw "timed out waiting for child process to be ready"
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            $waitEvent2.Set()

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}

        }
        finally {
            if ($waitEvent1) {
                $waitEvent1.Dispose()
            }
            if ($waitEvent2) {
                $waitEvent2.Dispose()
            }
            if ($proc) {
                $proc | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            if ($registeredEvent) {
                Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
                Unregister-Event -SourceIdentifier PSDetourAdded
            }
        }

        $actual | Should -Be 0
        $actual.ToString() | Should -Be 'Value1'
    }

    It "Traces other process with outputs" {
        $proc = $waitEvent1 = $waitEvent2 = $null
        $registeredEvent = $false
        $eventName = [Guid]::NewGuid().Guid

        try {
            $waitEvent1 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-1")
            $waitEvent2 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-2")

            $procScript = {
                $ErrorActionPreference = 'Stop'

                $commonPath = 'REPLACE_COMMON_PATH'
                .$commonPath

                $eventName = 'REPLACE_EVENT_NAME'
                $waitEvent1 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-1")
                $waitEvent2 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-2")
                try {
                    $waitEvent1.Set()
                    if (-not $waitEvent2.WaitOne(5000)) {
                        throw "Timed out waiting to start"
                    }
                    [PSDetourTest.Native]::VoidWithArg(5)
                }
                finally {
                    $waitEvent1.Dispose()
                    $waitEvent2.Dispose()
                }

                "Done"
                Start-Sleep -Seconds 60
            }.ToString() -replace 'REPLACE_EVENT_NAME', $eventName -replace 'REPLACE_COMMON_PATH', ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))
            $encCommand = [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($procScript))

            $procParams = @{
                FilePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                ArgumentList = "-NoProfile -EncodedCommand $encCommand"
                PassThru = $true
                WindowStyle = 'Hidden'
            }
            $proc = Start-Process @procParams

            $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
                param([int]$Milliseconds)

                $this.State.WriteDebug("debug")
                $this.State.WriteError([System.Management.Automation.ErrorRecord]::new(
                    [System.Exception]"Exception",
                    "ErrorId",
                    "NotSpecified",
                    $null
                ))
                $this.State.WriteInformation([System.Management.Automation.InformationRecord]::new(
                    "object",
                    "source"
                ))
                $this.State.WriteProgress([System.Management.Automation.ProgressRecord]::new(
                    1,
                    "activity",
                    "description"
                ))
                $this.State.WriteVerbose("verbose")
                $this.State.WriteWarning("warning")
                $this.State.WriteLine()
                $this.State.WriteLine("line")
                $res1 = $this.State.ReadLine()
                $this.State.WriteLine($res1)
                $res2 = $this.State.ReadLine("readline prompt")
                $this.State.WriteLine($res2)
                $this.State.WriteObject($Milliseconds)
            }

            $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

            $ps = [PowerShell]::Create()
            $null = $ps.AddScript({
                param($ModulePath, $Hook, $Process)

                Import-Module $ModulePath -ErrorAction Stop

                "started"
                $Hook | Trace-PSDetourProcess -ProcessId $Process -Debug -Verbose -WarningAction Continue
            }).AddParameters(@{ModulePath = $modulePath; Hook = $hook; Process = $proc})

            $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
            $registeredEvent = $true

            $customHost = [PSDetourTest.Host]::new($Host)
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $customHost
            }

            if (-not $waitEvent1.WaitOne(5000)) {
                throw "timed out waiting for child process to be ready"
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            $waitEvent2.Set()

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}

        }
        finally {
            if ($waitEvent1) {
                $waitEvent1.Dispose()
            }
            if ($waitEvent2) {
                $waitEvent2.Dispose()
            }
            if ($proc) {
                $proc | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            if ($registeredEvent) {
                Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
                Unregister-Event -SourceIdentifier PSDetourAdded
            }
        }

        $actual | Should -Be 5
        $ps.Streams.Error.Count | Should -Be 1
        $ps.Streams.Error[0].ToString() | Should -Be Exception
        $ps.Streams.Progress.Count | Should -Be 1
        $ps.Streams.Progress[0].ActivityId | Should -Be 1
        $ps.Streams.Progress[0].Activity | Should -Be activity
        $ps.Streams.Progress[0].StatusDescription | Should -Be description
        $ps.Streams.Verbose.Count | Should -Be 1
        $ps.Streams.Verbose[0].ToString() | Should -Be verbose
        $ps.Streams.Debug.Count | Should -Be 1
        $ps.Streams.Debug[0].ToString() | Should -Be debug
        $ps.Streams.Warning.Count | Should -Be 1
        $ps.Streams.Warning[0].ToString() | Should -Be warning
        $ps.Streams.Information.Count | Should -Be 1
        $ps.Streams.Information[0].MessageData | Should -Be object
        $ps.Streams.Information[0].Source | Should -Be source

        $customHost.UI.WriteCalls.WriteLine.Count | Should -Be 5
        $customHost.UI.WriteCalls.WriteLine[0] | Should -Be ''
        $customHost.UI.WriteCalls.WriteLine[1] | Should -Be line
        $customHost.UI.WriteCalls.WriteLine[2] | Should -Be 'readline response'
        $customHost.UI.WriteCalls.WriteLine[3] | Should -Be 'readline prompt'
        $customHost.UI.WriteCalls.WriteLine[4] | Should -Be 'readline response'
    }

    It "Traces other process that has ended" {
        $proc = $waitEvent1 = $waitEvent2 = $null
        $registeredEvent = $false
        $eventName = [Guid]::NewGuid().Guid

        try {
            $waitEvent1 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-1")
            $waitEvent2 = [System.Threading.EventWaitHandle]::new(
                $false,
                [System.Threading.EventResetMode]::ManualReset,
                "Global\$eventName-2")

            $procScript = {
                $ErrorActionPreference = 'Stop'

                $commonPath = 'REPLACE_COMMON_PATH'
                .$commonPath

                $eventName = 'REPLACE_EVENT_NAME'
                $waitEvent1 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-1")
                $waitEvent2 = [System.Threading.EventWaitHandle]::OpenExisting("Global\$eventName-2")
                try {
                    $waitEvent1.Set()
                    if (-not $waitEvent2.WaitOne(5000)) {
                        throw "Timed out waiting to start"
                    }
                    [PSDetourTest.Native]::VoidWithArg(5)
                }
                finally {
                    $waitEvent1.Dispose()
                    $waitEvent2.Dispose()
                }
            }.ToString() -replace 'REPLACE_EVENT_NAME', $eventName -replace 'REPLACE_COMMON_PATH', ([IO.Path]::Combine($PSScriptRoot, 'common.ps1'))
            $encCommand = [System.Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($procScript))

            $procParams = @{
                FilePath = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
                ArgumentList = "-NoProfile -NonInteractive -EncodedCommand $encCommand"
                PassThru = $true
                WindowStyle = 'Hidden'
            }
            $proc = Start-Process @procParams

            $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
                param([int]$Milliseconds)

                $this.State.WriteObject($Milliseconds)
            }

            $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

            $ps = [PowerShell]::Create()
            $null = $ps.AddScript({
                param($ModulePath, $Hook, $Process)

                $ErrorActionPreference = 'Stop'

                Import-Module $ModulePath -ErrorAction Stop

                "started"
                $Hook | Trace-PSDetourProcess -ProcessId $Process
            }).AddParameters(@{ModulePath = $modulePath; Hook = $hook; Process = $proc})

            $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
            Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
            $registeredEvent = $true
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }

            if (-not $waitEvent1.WaitOne(5000)) {
                throw "timed out waiting for child process to be ready"
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            $waitEvent2.Set()

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.EndInvoke($task)
        }
        finally {
            if ($waitEvent1) {
                $waitEvent1.Dispose()
            }
            if ($waitEvent2) {
                $waitEvent2.Dispose()
            }
            if ($proc) {
                $proc | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            if ($registeredEvent) {
                Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
                Unregister-Event -SourceIdentifier PSDetourAdded
            }
        }

        $actual | Should -Be 5
    }

    It "Stops trace" {
        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
            param([int]$Milliseconds)

            $this.State.WriteObject($Milliseconds)
            $this.State.StopTrace()
        }

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            $ErrorActionPreference = 'Stop'

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.EndInvoke($task)
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual | Should -Be 5
    }

    It "Shares functions" {
        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
            param([int]$Milliseconds)

            $this.State.WriteObject((Test-Function1))
            $this.State.WriteObject((Test-Function2))
            # $this.State.WriteObject(2)
        }

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            $ErrorActionPreference = 'Stop'

            Function Test-Function { 2 }

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess -FunctionsToDefine @{
                'Test-Function1' = {1}
                'Test-Function2' = ${Function:Test-Function}
            }
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $actual = @(
                for ($i = 0; $i -lt 2; $i++) {
                    $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
                    if (-not $dataAdded) {
                        if ($task.IsCompleted) {
                            $ps.EndInvoke($task)
                        }
                        throw "timeout waiting for trace output"
                    }
                    $outputStream[$dataAdded.SourceEventArgs[0].Index]
                    Remove-Event -EventIdentifier $dataAdded.EventIdentifier
                }
            )

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual.Count | Should -Be 2
        $actual[0] | Should -Be 1
        $actual[1] | Should -Be 2
    }

    It "Shares CSharp def" {
        Function Trace-Sleep {
            param([int]$Milliseconds)

            $this.State.WriteObject([Namespace.Testing]::Value)
        }
        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action ${Function:Trace-Sleep}

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            $ErrorActionPreference = 'Stop'

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess -CSharpToLoad @'
using System;

namespace Namespace;

public enum Testing
{
    Value
}
'@
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual | Should -Be 0
        $actual.ToString() | Should -Be 'Value'
    }

    It "Hooks with address <Scenario>" -TestCases @(
        @{Scenario = 'Absolute'}
        @{Scenario = 'Relative'}
    ) {
        param($Scenario)

        $dllLib = [System.Runtime.InteropServices.NativeLibrary]::Load($exampleDllPath)
        $methAddr = [System.Runtime.InteropServices.NativeLibrary]::GetExport($dllLib, "VoidWithArg")
        $hookParams = @{}
        if ($Scenario -eq 'Absolute') {
            $hookParams.Address = $methAddr
        }
        else {
            $hookParams.Address = [IntPtr]($methAddr.ToInt64() - $dllLib.ToInt64())
            $hookParams.AddressIsOffset = $true
        }

        $hook = New-PSDetourHook -DllName $exampleDllPath -MethodName VoidWithArg -Action {
            param([int]$Milliseconds)

            $this.State.WriteObject($Milliseconds)
        }

        $modulePath = Join-Path (Get-Module -Name PSDetour).ModuleBase 'PSDetour.psd1'

        $ps = [PowerShell]::Create()
        $null = $ps.AddScript({
            param($ModulePath, $Hook)

            $ErrorActionPreference = 'Stop'

            Import-Module $ModulePath -ErrorAction Stop

            "started"
            $Hook | Trace-PSDetourProcess
        }).AddParameters(@{ModulePath = $modulePath; Hook = $hook})

        $inputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $outputStream = [System.Management.Automation.PSDataCollection[object]]::new()
        $null = Register-ObjectEvent -InputObject $outputStream -EventName DataAdded -SourceIdentifier PSDetourAdded
        try {
            $psi = [System.Management.Automation.PSInvocationSettings]@{
                Host = $host
            }
            $task = $ps.BeginInvoke($inputStream, $outputStream, $psi, $null, $null)

            # Waits until started has been written
            Wait-Event -SourceIdentifier PSDetourAdded | Remove-Event

            # Add a second padding for the trace to actually start
            Start-Sleep -Second 1
            [PSDetourTest.Native]::VoidWithArg(5)

            $dataAdded = Wait-Event -SourceIdentifier PSDetourAdded -Timeout 5
            if (-not $dataAdded) {
                if ($task.IsCompleted) {
                    $ps.EndInvoke($task)
                }
                throw "timeout waiting for trace output"
            }
            $actual = $outputStream[$dataAdded.SourceEventArgs[0].Index]

            $ps.Stop()
            try {
                $ps.EndInvoke($task)
            }
            catch [System.Management.Automation.PipelineStoppedException] {}
        }
        finally {
            Remove-Event -SourceIdentifier PSDetourAdded -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier PSDetourAdded
        }

        $actual | Should -Be 5
    }
}
