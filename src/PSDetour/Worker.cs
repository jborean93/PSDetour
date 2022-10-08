using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace PSDetour;

[StructLayout(LayoutKind.Sequential)]
public struct WorkerArgs
{
    public IntPtr PipeIn;
    public IntPtr PipeOut;
    public IntPtr PowerShellDir;
    public int PowerShellDirCount;
}

public class RemoteWorker
{
    [UnmanagedCallersOnly]
    public static void Main(WorkerArgs args)
    {
        string pwshDependencyDir = Marshal.PtrToStringUni(args.PowerShellDir, args.PowerShellDirCount) ?? "";

        UTF8Encoding utf8Encoding = new UTF8Encoding();
        using AnonymousPipeClientStream pipeIn = new AnonymousPipeClientStream(PipeDirection.In, new SafePipeHandle(args.PipeIn, false));
        using StreamReader pipeReader = new StreamReader(pipeIn, utf8Encoding);

        using AnonymousPipeClientStream pipeOut = new AnonymousPipeClientStream(PipeDirection.Out, new SafePipeHandle(args.PipeOut, false));
        using StreamWriter sw = new StreamWriter(pipeOut, utf8Encoding);
        sw.AutoFlush = true;
        sw.WriteLine("connected");

        PowerShellAssemblyResolver resolver = new(pwshDependencyDir);
        AssemblyLoadContext.Default.Resolving += resolver.ResolvePwshDeps;

        string cmdToRun = pipeReader.ReadLine() ?? "unknown";
        string res = PowerShellRunner.Run(cmdToRun);
        sw.WriteLine(res);

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
