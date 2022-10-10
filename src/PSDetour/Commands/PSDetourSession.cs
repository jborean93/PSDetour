using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourSession")]
[OutputType(typeof(PSSession))]
public class NewPSDetourSession : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    public int[] ProcessId { get; set; } = Array.Empty<int>();

    [Parameter()]
    public int OpenTimeout { get; set; } = InjectedPipeConnectionInfo.DefaultOpenTimeout;

    protected override void ProcessRecord()
    {
        foreach (int pid in ProcessId)
        {
            InjectedPipeConnectionInfo connInfo = new(pid, OpenTimeout);

            // Cannot use CreateRunspace in 7.2.x as it explicitly checks the
            // type of connInfo to be one of the builtin ones of pwsh. Instead
            // will create the RemoteRunspace object directly.
            RemoteRunspace rs = new(null, connInfo, null, null);
            rs.Open();
            WriteObject(new PSSession(rs));
        }
    }
}
