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
    public IntPtr Pipe;
    public IntPtr PowerShellDir;
    public IntPtr AssemblyPath;
    public IntPtr RuntimeConfigPath;
}

public class RemoteWorker
{
    [UnmanagedCallersOnly]
    public static void Main(WorkerArgs args)
    {
        PowerShellAssemblyResolver? resolver = null;

        using (NamedPipeClientStream pipe = new(PipeDirection.InOut, false, true, new SafePipeHandle(args.Pipe, false)))
        {
            try
            {
                string pwshDependencyDir = Marshal.PtrToStringUni(args.PowerShellDir) ?? "";

                resolver = new(pwshDependencyDir);
                AssemblyLoadContext.Default.Resolving += resolver.ResolvePwshDeps;

                ReflectionInfo.IPCNamedPipeServerEnabledField.SetValue(null, true);
                ReflectionInfo.CreateIPCNamedPipeServerFunc.Invoke(null, Array.Empty<object>());
            }
            catch (Exception e)
            {
                string errMsg = $"Worker error {e.GetType().Name}: {e.Message}";
                byte[] msgBytes = Encoding.Unicode.GetBytes(errMsg);
                byte[] msgLength = BitConverter.GetBytes(msgBytes.Length);
                pipe.WriteByte(1);
                pipe.Write(msgLength);
                pipe.Write(msgBytes);

                return;
            }

            pipe.WriteByte(0);  // Signals all is good and to connect to the normal pipe.
        }

        ReflectionInfo.RunServerModeFunc.Invoke(null, new object?[] { null });

        if (resolver != null)
        {
            AssemblyLoadContext.Default.Resolving -= resolver.ResolvePwshDeps;
        }
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
