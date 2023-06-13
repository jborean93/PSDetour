using System;
using System.Management.Automation;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourHook")]
[OutputType(typeof(ScriptBlockHook))]
public class NewPSDetourHook : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 1
    )]
    public string DllName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 2
    )]
    public string MethodName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 3
    )]
    [Alias("ScriptBlock")]
    public ScriptBlock Action { get; set; } = default!;

    [Parameter]
    public IntPtr Address { get; set; } = IntPtr.Zero;

    [Parameter]
    public SwitchParameter AddressIsOffset { get; set; }

    protected override void EndProcessing()
    {
        WriteObject(new ScriptBlockHook(DllName, MethodName, Action, Address, AddressIsOffset));
    }
}
