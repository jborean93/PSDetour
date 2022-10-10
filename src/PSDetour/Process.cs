using System;
using System.Diagnostics;

namespace PSDetour;

public sealed class ProcessIntString
{
    internal Process ProcessObj { get; set; }

    public ProcessIntString(int pid)
    {
        ProcessObj = Process.GetProcessById(pid);
    }

    public ProcessIntString(string name)
    {
        Process[] processes = Process.GetProcessesByName(name);
        if (processes.Length != 1)
        {
            throw new ArgumentException($"Found {processes.Length} processes called '{name}' when only 1 can be used");
        }

        ProcessObj = processes[0];
    }

    public ProcessIntString(Process process)
    {
        ProcessObj = process;
    }
}
