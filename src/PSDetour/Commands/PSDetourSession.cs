using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourSession")]
[OutputType(typeof(PSSession))]
public class NewPSDetourSession : PSCmdlet
{
    private ManualResetEvent? _openEvent = null;
    private Runspace? _runspace = null;

    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    public ProcessIntString[] ProcessId { get; set; } = Array.Empty<ProcessIntString>();

    [Parameter()]
    public int OpenTimeoutMS { get; set; } = 5000;

    [Parameter()]
    public PSPrimitiveDictionary? ApplicationArguments { get; set; }

    [Parameter()]
    public string? Name { get; set; }

    protected override void ProcessRecord()
    {
        string modulePath = Path.GetFullPath(Path.Combine(
            typeof(NewPSDetourSession).Assembly.Location,
            "..",
            "..",
            "..",
            "PSDetour.psd1"));

        foreach (ProcessIntString proc in ProcessId)
        {
            string pipeName = GetProcessPipeName(proc.ProcessObj);
            if (Directory.GetFiles(@"\\.\pipe\", pipeName).Length < 1)
            {
                try
                {
                    DetouredProcess.InjectPowerShell(proc.ProcessObj.Id, OpenTimeoutMS, GlobalState.PwshAssemblyDir);
                }
                catch (Exception e)
                {
                    ErrorRecord err = new(
                        e,
                        "PSDetourFailedInjection",
                        ErrorCategory.ConnectionError,
                        proc.ProcessObj.Id);
                    WriteError(err);
                    continue;
                }
            }

            NamedPipeConnectionInfo connInfo = new(pipeName, OpenTimeoutMS);

            Runspace rs = RunspaceFactory.CreateRunspace(
                connInfo, Host, TypeTable.LoadDefaultTypeFiles(), ApplicationArguments, name: Name);

            using (_openEvent = new ManualResetEvent(false))
            {
                rs.StateChanged += HandleRunspaceStateChanged;
                rs.OpenAsync();
                _openEvent?.WaitOne();

                if (rs.RunspaceStateInfo.State == RunspaceState.Broken)
                {
                    ErrorRecord err = new(
                        rs.RunspaceStateInfo.Reason,
                        "PSDetourFailedConnection",
                        ErrorCategory.ConnectionError,
                        proc.ProcessObj.Id);

                    WriteError(err);
                    continue;
                }
            }

            using PowerShell ps = PowerShell.Create();
            ps.Runspace = rs;
            ps.AddCommand("Import-Module")
                .AddParameter("Name", modulePath)
                .AddParameter("Global", true);
            ps.Invoke();

#if PWSH72
            PSSession session = new((RemoteRunspace)rs);
#else
            PSSession session = PSSession.Create(
                runspace: rs,
                transportName: "PSDetour",
                psCmdlet: this);
#endif
            WriteObject(session);
        }
    }

    protected override void StopProcessing()
    {
        SetOpenEvent();
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

    internal static string GetProcessPipeName(Process proc)
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
