using System;
using System.Management.Automation;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourHook", DefaultParameterSetName = "Name")]
[OutputType(typeof(ScriptBlockHook))]
public class NewPSDetourHook : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 1,
        ParameterSetName = "Name"
    )]
    public string DllName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 2,
        ParameterSetName = "Name"
    )]
    public string MethodName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 1,
        ParameterSetName = "Address"
    )]
    public IntPtr Address { get; set; } = IntPtr.Zero;

    [Parameter(
        Mandatory = true,
        Position = 3,
        ParameterSetName = "Name"
    )]
    [Parameter(
        Mandatory = true,
        Position = 2,
        ParameterSetName = "Address"
    )]
    [Alias("ScriptBlock")]
    public ScriptBlock Action { get; set; } = default!;

    protected override void EndProcessing()
    {
        if (ParameterSetName == "Address")
        {
            WriteObject(new ScriptBlockHook(Address, Action));
        }
        else
        {
            WriteObject(new ScriptBlockHook(DllName, MethodName, Action));
        }
    }
}
