using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace PSDetour.Commands;

[Cmdlet(VerbsLifecycle.Start, "PSDetour")]
[OutputType(typeof(void))]
public class StartPSDetour : PSCmdlet
{
    private List<ScriptBlockHook> _hooks = new();

    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    [ValidateNotNullOrEmpty]
    public ScriptBlockHook[] Hook { get; set; } = Array.Empty<ScriptBlockHook>();

    protected override void ProcessRecord()
    {
        _hooks.AddRange(Hook);
    }

    protected override void EndProcessing()
    {
        PSDetour.Hook.Start(_hooks);
    }
}

[Cmdlet(VerbsLifecycle.Stop, "PSDetour")]
[OutputType(typeof(void))]
public class StopPSDetour : PSCmdlet
{
    protected override void EndProcessing()
    {
        Hook.Stop();
    }
}
