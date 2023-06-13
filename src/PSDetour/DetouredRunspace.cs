using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;

namespace PSDetour;

internal sealed class DetouredRunspace
{
    private ManualResetEvent? _openEvent = null;
    private Runspace _runspace;

    private DetouredRunspace(Runspace runspace)
    {
        _runspace = runspace;
    }

    internal void Open(CancellationToken? cancelToken)
    {
        using (_openEvent = new ManualResetEvent(false))
        {
            using CancellationTokenRegistration? tokenRegister = cancelToken?.Register(() => SetOpenEvent());
            _runspace.StateChanged += HandleRunspaceStateChanged;
            _runspace.OpenAsync();
            _openEvent?.WaitOne();

            if (_runspace.RunspaceStateInfo.State == RunspaceState.Broken)
            {
                throw _runspace.RunspaceStateInfo.Reason;
            }
        }
    }

    private void HandleRunspaceStateChanged(object? source, RunspaceStateEventArgs stateEventArgs)
    {
        switch (stateEventArgs.RunspaceStateInfo.State)
        {
            case RunspaceState.Opened:
            case RunspaceState.Closed:
            case RunspaceState.Broken:
                if (_runspace != null)
                {
                    _runspace.StateChanged -= HandleRunspaceStateChanged;
                }

                SetOpenEvent();
                break;
        }
    }

    private void SetOpenEvent()
    {
        try
        {
            _openEvent?.Set();
        }
        catch (ObjectDisposedException) { }
    }

    public static Runspace Create(
        Process process,
        PSPrimitiveDictionary? applicationArguments = null,
        PSHost? host = null,
        string? name =  null,
        int timeoutMs = 5000,
        CancellationToken? cancelToken = null
    )
    {
        string pipeName = GetProcessPipeName(process);
        if (Directory.GetFiles(@"\\.\pipe\", pipeName).Length < 1)
        {
            DetouredProcess.InjectPowerShell(process.Id, timeoutMs, GlobalState.PwshAssemblyDir);
        }

        NamedPipeConnectionInfo connInfo = new(pipeName, timeoutMs);

        Runspace rs = RunspaceFactory.CreateRunspace(
            connInfo,
            host,
            TypeTable.LoadDefaultTypeFiles(),
            applicationArguments,
            name: name);
        new DetouredRunspace(rs).Open(cancelToken);

        using PowerShell ps = PowerShell.Create();
        ps.Runspace = rs;
        ps.AddCommand("Import-Module")
            .AddParameter("Name", GlobalState.ModulePath)
            .AddParameter("Global", true);
        ps.Invoke();

        return rs;
    }

    private static string GetProcessPipeName(Process proc)
    {
        // This is the same logic used in pwsh internally
        StringBuilder pipeNameBuilder = new();
        pipeNameBuilder.Append("PSHost.")
            .Append(proc.StartTime.ToFileTime().ToString(CultureInfo.InvariantCulture))
            .Append('.')
            .Append(proc.Id.ToString(CultureInfo.InvariantCulture))
            .Append(".DefaultAppDomain.")
            .Append(proc.ProcessName);

        return pipeNameBuilder.ToString();
    }
}
