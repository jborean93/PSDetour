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
    public ProcessIntString[] ProcessId { get; set; } = Array.Empty<ProcessIntString>();

    [Parameter()]
    public int OpenTimeout { get; set; } = InjectedPipeConnectionInfo.DefaultOpenTimeout;

    protected override void ProcessRecord()
    {
        foreach (ProcessIntString pid in ProcessId)
        {
            InjectedPipeConnectionInfo connInfo = new(pid.ProcessObj.Id, OpenTimeout);

            // Cannot use CreateRunspace in 7.2.x as it explicitly checks the
            // type of connInfo to be one of the builtin ones of pwsh. Instead
            // will create the RemoteRunspace object directly.
            RemoteRunspace rs = new(TypeTable.LoadDefaultTypeFiles(), connInfo, Host, null);
            rs.Open();
            WriteObject(new PSSession(rs));
        }
    }
}
