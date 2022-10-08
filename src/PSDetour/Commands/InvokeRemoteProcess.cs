using PSDetour.Native;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Text;

namespace PSDetour.Commands;

[Cmdlet(VerbsLifecycle.Invoke, "RemoteProcess")]
public class InvokeRemoteProcess : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 0
    )]
    public int ProcessId { get; set; } = 0;

    [Parameter(
        Mandatory = true,
        Position = 1
    )]
    public ScriptBlock ScriptBlock { get; set; } = ScriptBlock.Create("");

    protected override void EndProcessing()
    {
        WriteObject(RemoteInvoke(ProcessId, ScriptBlock.ToString()));
    }

    private static string RemoteInvoke(int pid, string cmd)
    {
        string assemblyPath = typeof(InvokeRemoteProcess).Assembly.Location;
        string targetDll = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyPath) ?? "", "PSDetourNative.dll"));

        IntPtr lib = Kernel32.LoadLibraryW(targetDll);
        try
        {
            IntPtr localInjectAddr = Kernel32.GetProcAddress(lib, "inject");

            const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
                ProcessAccessRights.DupHandle |
                ProcessAccessRights.QueryInformation |
                ProcessAccessRights.VMOperation |
                ProcessAccessRights.VMRead |
                ProcessAccessRights.VMWrite;
            using var process = Kernel32.OpenProcess(pid, procRights, false);
            IntPtr remoteModAddr = LoadRemoteLibrary(process, targetDll);
            try
            {
                int injectOffset = (int)(localInjectAddr.ToInt64() - lib.ToInt64());
                IntPtr remoteInjectAddr = IntPtr.Add(remoteModAddr, injectOffset);

                UTF8Encoding utf8Encoding = new UTF8Encoding();

                (AnonymousPipeServerStream pipeOut, SafeDuplicateHandle clientPipeIn) = CreateAnonPipePair(process,
                    PipeDirection.Out);
                StreamWriter pipeWriter = new(pipeOut, utf8Encoding, 4096, true);
                pipeWriter.AutoFlush = true;

                (AnonymousPipeServerStream pipeIn, SafeDuplicateHandle clientPipeOut) = CreateAnonPipePair(process,
                    PipeDirection.In);
                StreamReader pipeReader = new(pipeIn, utf8Encoding, false, 4096, true);

                using (clientPipeIn)
                using (clientPipeOut)
                using (pipeIn)
                using (pipeOut)
                {
                    return ExecuteRemoteCode(process, remoteInjectAddr, pipeWriter, pipeReader,
                        clientPipeIn.DangerousGetHandle(), clientPipeOut.DangerousGetHandle(), cmd);
                }
            }
            finally
            {
                UnloadRemoteLibrary(process, remoteModAddr);
            }
        }
        finally
        {
            Kernel32.FreeLibrary(lib);
        }
    }

    private static string ExecuteRemoteCode(SafeNativeHandle process, IntPtr injectAddr, StreamWriter writer,
        StreamReader reader, IntPtr clientPipeIn, IntPtr clientPipeOut, string code)
    {
        string pwshAssemblyDir = Path.GetDirectoryName(typeof(PSObject).Assembly.Location) ?? "";
        byte[] pwshAssemblyBytes = Encoding.Unicode.GetBytes(pwshAssemblyDir);
        IntPtr remoteAssemblyAddr = WriteProcessMemory(process, pwshAssemblyBytes);
        try
        {
            WorkerArgs remoteArgs = new()
            {
                PipeIn = clientPipeIn,
                PipeOut = clientPipeOut,
                PowerShellDir = remoteAssemblyAddr,
                PowerShellDirCount = pwshAssemblyDir.Length,
            };
            unsafe
            {
                IntPtr remoteArgsPtr = WriteProcessMemory(process,
                    new ReadOnlySpan<byte>(&remoteArgs, Marshal.SizeOf<WorkerArgs>()));
                try
                {
                    using SafeNativeHandle remoteThread = CreateRemoteThread(process, injectAddr, remoteArgsPtr);

                    string confirmation = reader.ReadLine() ?? "no confirmation";
                    if (confirmation != "connected")
                    {
                        throw new Exception($"Invalid confirmation - '{confirmation}'");
                    }

                    writer.WriteLine(code);

                    string result = reader.ReadLine() ?? "no result";

                    Kernel32.WaitForSingleObject(remoteThread, Kernel32.INFINITE);

                    return result;
                }
                finally
                {
                    FreeProcessMemory(process, remoteArgsPtr);
                }
            }
        }
        finally
        {
            FreeProcessMemory(process, remoteAssemblyAddr);
        }
    }

    private static IntPtr LoadRemoteLibrary(SafeNativeHandle process, string module)
    {
        IntPtr funcAddr = Kernel32.GetProcAddress(Kernel32.GetModuleHandleW("Kernel32.dll"), "LoadLibraryW");

        byte[] moduleBytes = Encoding.Unicode.GetBytes(module);
        IntPtr remoteArgAddr = WriteProcessMemory(process, moduleBytes);
        try
        {
            using SafeNativeHandle thread = CreateRemoteThread(process, funcAddr, remoteArgAddr);
            Kernel32.WaitForSingleObject(thread, Kernel32.INFINITE);
        }
        finally
        {
            FreeProcessMemory(process, remoteArgAddr);
        }

        IntPtr remoteModAddr = IntPtr.Zero;
        foreach (IntPtr mod in Psapi.EnumProcessModulesEx(process, EnumProcessModulesFilterFlag.All))
        {
            if (mod == IntPtr.Zero)
            {
                continue;
            }

            string moduleName;
            try
            {
                moduleName = Kernel32.GetModuleFileNameW(mod);
            }
            catch (Win32Exception)
            {
                continue;
            }

            if (string.Equals(moduleName, module, StringComparison.OrdinalIgnoreCase))
            {
                remoteModAddr = mod;
                break;
            }
        }
        if (remoteModAddr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Unknown failure while trying to load {module} into remote process");
        }

        return remoteModAddr;
    }

    private static void UnloadRemoteLibrary(SafeNativeHandle process, IntPtr module)
    {
        IntPtr funcAddr = Kernel32.GetProcAddress(Kernel32.GetModuleHandleW("Kernel32.dll"), "FreeLibrary");

        using SafeNativeHandle thread = CreateRemoteThread(process, funcAddr, module);
        Kernel32.WaitForSingleObject(thread, Kernel32.INFINITE);
    }

    private static SafeNativeHandle CreateRemoteThread(SafeNativeHandle process, IntPtr func, IntPtr args)
    {
        return Kernel32.CreateRemoteThread(process, 0, func, args, ThreadCreationFlags.None, out var _);
    }

    private static IntPtr WriteProcessMemory(SafeNativeHandle process, ReadOnlySpan<byte> data)
    {
        IntPtr remoteAddr = Kernel32.VirtualAllocEx(process,
            IntPtr.Zero,
            data.Length,
            MemoryAllocationType.Reserve | MemoryAllocationType.Commit,
            MemoryProtection.ExecuteReadWrite);

        Kernel32.WriteProcessMemory(process, remoteAddr, data);

        return remoteAddr;
    }

    private static void FreeProcessMemory(SafeNativeHandle process, IntPtr addr)
    {
        Kernel32.VirtualFreeEx(process, addr, 0, MemoryFreeType.Release);
    }

    private static (AnonymousPipeServerStream, SafeDuplicateHandle) CreateAnonPipePair(SafeNativeHandle process,
        PipeDirection direction)
    {
        AnonymousPipeServerStream pipe = new(direction, HandleInheritability.None);
        SafeDuplicateHandle clientPipe = Kernel32.DuplicateHandle(
            Kernel32.GetCurrentProcess(),
            pipe.ClientSafePipeHandle,
            process,
            0,
            false,
            DuplicateHandleOptions.SameAccess,
            true);
        pipe.DisposeLocalCopyOfClientHandle();

        return (pipe, clientPipe);
    }
}
