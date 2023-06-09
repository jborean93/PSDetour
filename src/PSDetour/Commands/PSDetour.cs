using System;
using System.Collections.Generic;
using System.Management.Automation;

namespace PSDetour.Commands;

[Cmdlet(VerbsLifecycle.Start, "PSDetour")]
[OutputType(typeof(void))]
public class StartPSDetour : PSCmdlet
{
    private List<DetourHook> _hooks = new();

    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    [ValidateNotNullOrEmpty]
    public DetourHook[] Hook { get; set; } = Array.Empty<DetourHook>();

    [Parameter()]
    public object? State { get; set; }

    protected override void ProcessRecord()
    {
        foreach (DetourHook h in Hook)
        {
            if (h is ScriptBlockHook sbkHook)
            {
                sbkHook.SetHostContext(this, State);
            }

            _hooks.Add(h);
        }
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
