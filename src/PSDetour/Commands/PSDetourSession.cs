using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

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
                DetouredProcess.InjectPowerShell(proc.ProcessObj.Id, OpenTimeoutMS, GlobalState.PwshAssemblyDir);
            }

            NamedPipeConnectionInfo connInfo = new(pipeName, OpenTimeoutMS);

            Runspace rs = RunspaceFactory.CreateRunspace(
                connInfo, Host, TypeTable.LoadDefaultTypeFiles(), ApplicationArguments, name: Name);
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
                    proc.ProcessObj.Id));
                continue;
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
