using System;
using System.IO;
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

        foreach (ProcessIntString pid in ProcessId)
        {
            InjectedPipeConnectionInfo connInfo = new(pid.ProcessObj.Id, OpenTimeout);

#if PWSH72
            // Cannot use CreateRunspace in 7.2.x as it explicitly checks the
            // type of connInfo to be one of the builtin ones of pwsh. Instead
            // will create the RemoteRunspace object directly.
            RemoteRunspace rs = new(TypeTable.LoadDefaultTypeFiles(), connInfo, Host, ApplicationArguments, name: Name);
#else
            Runspace rs = RunspaceFactory.CreateRunspace(
                connInfo, Host, TypeTable.LoadDefaultTypeFiles(), ApplicationArguments, name: Name);
#endif
            try
            {
                // FIXME: Use OpenAsync so we can cancel it
                rs.Open();
            }
            catch (Exception e)
            {
                WriteError(new ErrorRecord(
                    // InnerException is most likely the one with more info.
                    e.InnerException ?? e,
                    "errorId",
                    ErrorCategory.OpenError,
                    pid.ProcessObj.Id));
                continue;
            }

            using PowerShell ps = PowerShell.Create();
            ps.Runspace = rs;
            ps.AddCommand("Import-Module")
                .AddParameter("Name", modulePath)
                .AddParameter("Global", true);
            ps.Invoke();

#if PWSH72
            PSSession session = new(rs);
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
