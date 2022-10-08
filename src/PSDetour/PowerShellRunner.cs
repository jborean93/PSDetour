using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace PSDetour;

public static class PowerShellRunner
{
    public static string Run(string cmd)
    {
        using Runspace rs = RunspaceFactory.CreateRunspace();
        rs.Open();
        using PowerShell ps = PowerShell.Create();
        ps.Runspace = rs;
        ps.AddScript(cmd);
        string cmdOut = ps.Invoke<string>()[0].ToString();

        return cmdOut;
    }
}
