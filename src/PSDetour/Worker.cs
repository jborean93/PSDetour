using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace PSDetour;

[StructLayout(LayoutKind.Sequential)]
public struct WorkerArgs
{
    public IntPtr Pipe;
    public IntPtr PowerShellDir;
    public int PowerShellDirCount;
}

public class RemoteWorker
{
    [UnmanagedCallersOnly]
    public static void Main(WorkerArgs args)
    {
        string pwshDependencyDir = Marshal.PtrToStringUni(args.PowerShellDir, args.PowerShellDirCount) ?? "";

        using NamedPipeClientStream pipe = new(PipeDirection.InOut, false, true, new SafePipeHandle(args.Pipe, false));

        // Signal parent it has started properly and is ready for PSRemoting.
        pipe.WriteByte(0);

        PowerShellAssemblyResolver resolver = new(pwshDependencyDir);
        AssemblyLoadContext.Default.Resolving += resolver.ResolvePwshDeps;

        PSRemotingServer.Run(pipe);

        AssemblyLoadContext.Default.Resolving -= resolver.ResolvePwshDeps;
    }
}

internal class PowerShellAssemblyResolver
{
    private readonly string _depPath;

    public PowerShellAssemblyResolver(string depPath)
    {
        _depPath = depPath;
    }

    public Assembly? ResolvePwshDeps(AssemblyLoadContext defaultAlc, AssemblyName assemblyToResolve)
    {
        string assemblyPath = Path.Combine(_depPath, $"{assemblyToResolve.Name}.dll");

        if (File.Exists(assemblyPath))
        {
            return Assembly.LoadFrom(assemblyPath);
        }

        return null;
    }
}
