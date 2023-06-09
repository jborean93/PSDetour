using Microsoft.Win32.SafeHandles;
using System;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace PSDetour;

[StructLayout(LayoutKind.Sequential)]
public struct WorkerArgs
{
    public IntPtr Pipe;
    public IntPtr PowerShellDir;
    public IntPtr AssemblyPath;
}

public static class RemoteWorker
{
    [UnmanagedCallersOnly]
    public static void Main(WorkerArgs args)
    {
        using (NamedPipeClientStream pipe = new(PipeDirection.InOut, false, true, new SafePipeHandle(args.Pipe, false)))
        {
            try
            {
                ReflectionInfo.IPCNamedPipeServerEnabledField.SetValue(null, true);
                ReflectionInfo.CreateIPCNamedPipeServerFunc.Invoke(null, Array.Empty<object>());
            }
            catch (Exception e)
            {
                string errMsg = $"Worker error {e.GetType().Name}: {e.Message}";
                byte[] msgBytes = Encoding.Unicode.GetBytes(errMsg);
                pipe.Write(BitConverter.GetBytes(1));
                pipe.Write(BitConverter.GetBytes(msgBytes.Length));
                pipe.Write(msgBytes);

                return;
            }

            pipe.Write(BitConverter.GetBytes(0));  // Signals all is good and to connect to the normal pipe.
        }

        while (true)
        {
            ReflectionInfo.RunServerModeFunc.Invoke(null, new object?[] { null });
        }
    }
}
