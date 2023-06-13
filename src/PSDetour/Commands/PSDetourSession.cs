using System;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourSession")]
[OutputType(typeof(PSSession))]
public class NewPSDetourSession : PSCmdlet
{
    private CancellationTokenSource? _cancelToken;

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
        using (_cancelToken = new())
        {
            foreach (ProcessIntString proc in ProcessId)
            {
                Runspace rs;
                try
                {
                    rs = DetouredRunspace.Create(
                        proc.ProcessObj,
                        applicationArguments: ApplicationArguments,
                        host: Host,
                        name: Name,
                        timeoutMs: OpenTimeoutMS,
                        cancelToken: _cancelToken?.Token
                    );
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

                using PowerShell ps = PowerShell.Create();
                ps.Runspace = rs;
                ps.AddCommand("Import-Module")
                    .AddParameter("Name", GlobalState.ModulePath)
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
    }

    protected override void StopProcessing()
    {
        _cancelToken?.Cancel();
    }
}
