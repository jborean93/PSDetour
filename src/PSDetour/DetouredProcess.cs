using Microsoft.Win32.SafeHandles;
using PSDetour.Native;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PSDetour;

internal sealed class DetouredProcess
{
    /// <summary>
    /// Injects a PowerShell server that runs a named pipe server for PSSession
    /// communication. This taints the remote process as there is no way to
    /// remove the loaded assemblies in the remote process or stop the named
    /// pipe listener once it has started.
    /// </summary>
    /// <param name="processId">The process to inject PowerShell into.</param>
    /// <param name="timeoutMs">Timeout in milliseconds to wait for a response.</param>
    /// <param name="powershellDir">The PowerShell install to inject.</param>
    public static void InjectPowerShell(int processId, int timeoutMs,
        string powershellDir)
    {
        const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
            ProcessAccessRights.DupHandle |
            ProcessAccessRights.QueryInformation |
            ProcessAccessRights.VMOperation |
            ProcessAccessRights.VMRead |
            ProcessAccessRights.VMWrite;

        using SafeProcessHandle process = Kernel32.OpenProcess(processId, procRights, false);
        using SafeRemoteLoadedLibrary psdetourNativeLib = LoadRemoteLibrary(process, GlobalState.NativePath);
        IntPtr remoteInjectAddr = GlobalState.GetRemoteInjectAddr(psdetourNativeLib.DangerousGetHandle());

        // Create a named pipe that is only usuable by the current security
        // principal and inject that into the remote process. This means only
        // this logonsession/user and that remote process can talk back to us.
        string pipeName = $"PSDetour-{Guid.NewGuid()}";
        using NamedPipeServerStream pipe = CreateSecurePipe(pipeName);
        using SafeDuplicateHandle remotePipe = InjectClientPipeIntoProcess(pipe, pipeName, process);

        // While the thread may outlive these arguments, the value on the
        // remote thread only lives until it sends back the confirmation
        // message. It is safe to unload this info once that point has been
        // reached.
        string assemblyLocation = typeof(DetouredProcess).Assembly.Location;
        byte[] pwshDirBytes = Encoding.Unicode.GetBytes(powershellDir);
        byte[] psdetourAssemblyPathBytes = Encoding.Unicode.GetBytes(assemblyLocation);
        using SafeRemoteMemory pwshDirMem = WriteProcessMemory(process, pwshDirBytes);
        using SafeRemoteMemory psdetourAssemblyPathMem = WriteProcessMemory(process, psdetourAssemblyPathBytes);
        using SafeRemoteMemory workerArgs = CreateWorkerArgs(process, remotePipe, pwshDirMem,
            psdetourAssemblyPathMem);
        using SafeNativeHandle runningThread = CreateRemoteThread(process, remoteInjectAddr,
            workerArgs.DangerousGetHandle());

        using (CancellationTokenSource cancelToken = new())
        {
            // First message back is going to be the rc as an int32 value. If
            // non-zero then another int32 value contains the length of the
            // UTF-16-LE encoded error message from the remote process. A
            // timeout is also used in case of a catastrophic error to avoid a
            // deadlock.
            byte[] tempBuffer = new byte[4];
            Task<int> readTask = pipe.ReadAsync(tempBuffer, 0, 4, cancelToken.Token);
            int res = Task.WaitAny(new[] { readTask }, timeoutMs);

            if (res == -1)
            {
                cancelToken.Cancel();
                throw new TimeoutException($"Timeout while waiting for remote process {processId} to connect");
            }

            readTask.GetAwaiter().GetResult();
            int rc = BitConverter.ToInt32(tempBuffer);
            if (rc != 0)
            {
                byte[] errorLengthBuffer = new byte[4];
                pipe.Read(errorLengthBuffer, 0, errorLengthBuffer.Length);
                int errorLength = BitConverter.ToInt32(errorLengthBuffer, 0);

                byte[] errorMsgBuffer = new byte[errorLength];
                pipe.Read(errorMsgBuffer, 0, errorMsgBuffer.Length);
                string errorMsg = Encoding.Unicode.GetString(errorMsgBuffer);

                throw PSDetourBoostrapError(processId, rc, errorMsg);
            }
        }
    }

    private static NamedPipeServerStream CreateSecurePipe(string name)
    {
        using SafeAccessTokenHandle currentToken = Advapi32.OpenProcessToken(
            Kernel32.GetCurrentProcess(),
            TokenAccessLevels.Query);

        // The pipe is created so only the current logon session (fallback to
        // user for SYSTEM) can connect to it. This ensures that no other
        // logon can talk to us. The client handle is duplicated to the remote
        // process so the access check will not apply there.
        SecurityIdentifier currentPrincipal;
        try
        {
            // This fails when running as SYSTEM so fallback to using the token user
            currentPrincipal = Advapi32.GetTokenLogonSid(currentToken);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 0x00000490) // ERROR_NOT_FOUND
        {
            currentPrincipal = Advapi32.GetTokenUser(currentToken);
        }

        const PipeAccessRights pipeRights = PipeAccessRights.Read |
            PipeAccessRights.Write |
            PipeAccessRights.Synchronize |
            PipeAccessRights.CreateNewInstance;

        PipeSecurity pipeSecurity = new();
        pipeSecurity.AddAccessRule(new(currentPrincipal, pipeRights, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            32768,
            32768,
            pipeSecurity,
            HandleInheritability.None);
    }

    private static SafeDuplicateHandle InjectClientPipeIntoProcess(NamedPipeServerStream pipeServer, string pipeName,
        SafeProcessHandle targetProcess)
    {
        using NamedPipeClientStream clientPipe = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous,
            HandleInheritability.None);

        clientPipe.Connect();
        pipeServer.WaitForConnection();

        return Kernel32.DuplicateHandle(
            Kernel32.GetCurrentProcess(),
            clientPipe.SafePipeHandle,
            targetProcess,
            0,
            false,
            DuplicateHandleOptions.SameAccess,
            true);
    }

    private static SafeRemoteMemory CreateWorkerArgs(SafeProcessHandle process, SafeDuplicateHandle remotePipe,
        SafeRemoteMemory powershellDir, SafeRemoteMemory assemblyPath)
    {
        WorkerArgs remoteArgs = new()
        {
            Pipe = remotePipe.DangerousGetHandle(),
            PowerShellDir = powershellDir.DangerousGetHandle(),
            AssemblyPath = assemblyPath.DangerousGetHandle(),
        };
        unsafe
        {
            return WriteProcessMemory(process,
                new ReadOnlySpan<byte>(&remoteArgs, Marshal.SizeOf<WorkerArgs>()));
        }
    }

    private static SafeRemoteLoadedLibrary LoadRemoteLibrary(SafeProcessHandle process, string module)
    {
        byte[] moduleBytes = Encoding.Unicode.GetBytes(module);
        using (SafeRemoteMemory remoteArgAddr = WriteProcessMemory(process, moduleBytes))
        {
            using SafeNativeHandle thread = CreateRemoteThread(process, GlobalState.LoadLibraryAddr.Value,
                remoteArgAddr.DangerousGetHandle());
            Kernel32.WaitForSingleObject(thread, Kernel32.INFINITE);
        }

        SafeRemoteLoadedLibrary? remoteMod = null;
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
                remoteMod = new(process, mod);
                break;
            }
        }
        if (remoteMod == null)
        {
            throw new InvalidOperationException($"Unknown failure while trying to load {module} into remote process");
        }

        return remoteMod;
    }

    private static SafeNativeHandle CreateRemoteThread(SafeProcessHandle process, IntPtr func, IntPtr args)
    {
        return Kernel32.CreateRemoteThread(process, 0, func, args, ThreadCreationFlags.None, out var _);
    }

    private static SafeRemoteMemory WriteProcessMemory(SafeProcessHandle process, ReadOnlySpan<byte> data)
    {
        IntPtr remoteAddr = Kernel32.VirtualAllocEx(process,
            IntPtr.Zero,
            data.Length,
            MemoryAllocationType.Reserve | MemoryAllocationType.Commit,
            MemoryProtection.ExecuteReadWrite);
        SafeRemoteMemory remoteMemory = new(process, remoteAddr);

        Kernel32.WriteProcessMemory(process, remoteAddr, data);

        return remoteMemory;
    }

    private static Exception PSDetourBoostrapError(int pid, int rc, string msg)
    {
        string contextError;
        Exception? innerException = null;

        if (msg.StartsWith("LoadLibraryW()") || msg.StartsWith("GetProcAddress()"))
        {
            // These 2 calls have the rc correlate to a Win32 error code. Use
            // the Win32Exception to get more details.
            innerException = new Win32Exception(rc);
            contextError = string.Format("{0} - 0x{1:X8} {2}", msg, rc, innerException.Message);
        }
        else if (msg.StartsWith("hostfxr_"))
        {
            // Any call starting with hostfxr_ is a dotnet hosting error. Map
            // the rc to the known list of error codes.
            HostFXRError error = (HostFXRError)rc;
            string errorMsg = HostFXRErrorHelper.GetErrorMessage(error);
            contextError = string.Format("{0} - {1} 0x{2:X8} {3}", msg, error.ToString(), rc, errorMsg);
        }
        else
        {
            // Something else, cannot get more context behind it.
            contextError = msg;
        }

        // FIXME: Use better exception
        return new Exception(
            $"Error when bootstrapping dotnet onto the remote process {pid}: {contextError}",
            innerException);
    }
}
