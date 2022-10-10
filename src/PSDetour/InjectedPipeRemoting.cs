using Microsoft.Win32.SafeHandles;
using PSDetour.Native;
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Internal;
using System.Management.Automation.Remoting;
using System.Management.Automation.Remoting.Client;
using System.Management.Automation.Remoting.Server;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PSDetour;

// It's important this is a standalone class and doesn't inherit from anything
// in S.M.A. By putting it in its own class the assembly resolver code in the
// caller will be able to run.
internal static class PSRemotingServer
{
    internal static void Run(NamedPipeClientStream pipe)
    {
        UTF8Encoding utf8Encoding = new(false);
        StreamReader inputReader = new(
            pipe,
            encoding: utf8Encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: -1,
            leaveOpen: true);
        StreamWriter outputWriter = new(pipe, encoding: utf8Encoding, bufferSize: -1, leaveOpen: true);
        outputWriter.AutoFlush = true;

        InjectedPipeProcessMediator instance = new(inputReader, new OutOfProcessTextWriter(outputWriter));
        instance.Run();
    }
}

// OutOfProcessMediatorBase is internal in S.M.A 7.2.x but there is work in
// 7.3.x to make public. This should be revisited later to avoid using internal
// functionality if the server side bits become public.
internal class InjectedPipeProcessMediator : OutOfProcessMediatorBase
{
    public InjectedPipeProcessMediator(TextReader inputPipe, OutOfProcessTextWriter outputPipe) : base(exitProcessOnError: false)
    {
        originalStdIn = inputPipe;
        originalStdOut = outputPipe;
        originalStdErr = outputPipe;
    }

    internal void Run()
    {
        Start(
            initialCommand: null,
            cryptoHelper: new PSRemotingCryptoHelperServer(),
            workingDirectory: null,
            configurationName: null);
    }
}

// OutOfProcessClientSessionTransportManagerBase is insternal in S.M.A 7.2.x
// but there is a generic client mechanism that can be used for custom
// transports in 7.3.x which should be used when available.
internal class InjectedPipeClientSessionTransportManager : OutOfProcessClientSessionTransportManagerBase
{
    private readonly InjectedPipeConnectionInfo _connInfo;

    // Client side objects that live throughout the connection
    private NamedPipeServerStream? _pipe = null;
    private NamedPipeClientStream? _clientPipe = null;

    // Pointers to remote process objects that need to be cleaned up at the end.
    private SafeProcessHandle? _remoteProcess = null;
    private SafeDuplicateHandle? _remotePipe = null;
    private SafeRemoteLoadedLibrary? _psdetourNativeMod = null;
    private SafeRemoteMemory? _pwshAssemblyMem = null;
    private SafeRemoteMemory? _workerArgs = null;
    private SafeNativeHandle? _injectThread = null;

    public InjectedPipeClientSessionTransportManager(InjectedPipeConnectionInfo connectionInfo, Guid runspaceId,
        PSRemotingCryptoHelper cryptoHelper) : base(runspaceId, cryptoHelper)
    {
        _connInfo = connectionInfo;
    }

    public override void CreateAsync()
    {
        const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
            ProcessAccessRights.DupHandle |
            ProcessAccessRights.QueryInformation |
            ProcessAccessRights.VMOperation |
            ProcessAccessRights.VMRead |
            ProcessAccessRights.VMWrite;
        _remoteProcess = Kernel32.OpenProcess(_connInfo.ProcessId, procRights, false);

        // Load PSDetourNative.dll in the target process
        _psdetourNativeMod = LoadRemoteLibrary(_remoteProcess, PSDetourNative.NativePath);
        IntPtr remoteInjectAddr = PSDetourNative.GetRemoteInjectAddr(_psdetourNativeMod.DangerousGetHandle());

        // Create anon pipes for in/out and duplicate into remote process
        using SafeProcessHandle currentProcess = Kernel32.GetCurrentProcess();
        using SafeAccessTokenHandle currentToken = Advapi32.OpenProcessToken(currentProcess, TokenAccessLevels.Query);
        SecurityIdentifier pipeUser;
        try
        {
            pipeUser = Advapi32.GetTokenLogonSid(currentToken);
        }
        catch (Win32Exception)
        {
            pipeUser = Advapi32.GetTokenUser(currentToken);
        }

        const PipeAccessRights pipeRights = PipeAccessRights.Read |
            PipeAccessRights.Write |
            PipeAccessRights.Synchronize |
            PipeAccessRights.CreateNewInstance;
        PipeSecurity pipeSecurity = new();
        pipeSecurity.AddAccessRule(new(pipeUser, pipeRights, AccessControlType.Allow));
        string pipeName = $"PSDetour-{Guid.NewGuid()}";

        _pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            32768,
            32768,
            pipeSecurity,
            HandleInheritability.None);
        using (NamedPipeClientStream clientPipe = new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Anonymous,
            HandleInheritability.None))
        {
            clientPipe.Connect();
            _pipe.WaitForConnection();

            _remotePipe = Kernel32.DuplicateHandle(
                currentProcess,
                clientPipe.SafePipeHandle,
                _remoteProcess,
                0,
                false,
                DuplicateHandleOptions.SameAccess,
                true);
        }

        // Build arg struct to remote process
        byte[] pwshAssemblyBytes = Encoding.Unicode.GetBytes(PSDetourNative.PwshAssemblyDir);
        _pwshAssemblyMem = WriteProcessMemory(_remoteProcess, pwshAssemblyBytes);

        WorkerArgs remoteArgs = new()
        {
            Pipe = _remotePipe.DangerousGetHandle(),
            PowerShellDir = _pwshAssemblyMem.DangerousGetHandle(),
            PowerShellDirCount = PSDetourNative.PwshAssemblyDir.Length,
        };
        unsafe
        {
            _workerArgs = WriteProcessMemory(_remoteProcess,
                new ReadOnlySpan<byte>(&remoteArgs, Marshal.SizeOf<WorkerArgs>()));
        }

        // Start inject method on remote thread and wait for confirmation it
        // has started
        _injectThread = CreateRemoteThread(_remoteProcess, remoteInjectAddr, _workerArgs.DangerousGetHandle());

        using (CancellationTokenSource cancelToken = new())
        {
            //Task waitConnect = _pipe.WaitForConnectionAsync(cancelToken.Token);
            //int res = Task.WaitAny(new[] { waitConnect }, _connInfo.OpenTimeout);

            byte[] tempBuffer = new byte[1];
            Task<int> readTask = _pipe.ReadAsync(tempBuffer, 0, 1, cancelToken.Token);
            int res = Task.WaitAny(new[] { readTask }, _connInfo.OpenTimeout);

            if (res == -1)
            {
                cancelToken.Cancel();
                throw new TimeoutException($"Timeout while waiting for remote process {_connInfo.ProcessId} to connect");
            }

            //waitConnect.GetAwaiter().GetResult();
            readTask.GetAwaiter().GetResult();
        }

        UTF8Encoding utf8Encoding = new(false);
        StreamWriter pipeWriter = new(_pipe, encoding: utf8Encoding, bufferSize: -1, leaveOpen: true);
        pipeWriter.AutoFlush = true;
        StreamReader pipeReader = new(
            _pipe,
            encoding: utf8Encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: -1,
            leaveOpen: true);

        stdInWriter = new OutOfProcessTextWriter(pipeWriter);
        Thread readerThread = new Thread(() => ProcessReaderThread(pipeReader));
        readerThread.Name = $"PSDetourClient({_connInfo.ProcessId})";
        readerThread.IsBackground = true;
        readerThread.Start();
    }

    protected override void CleanupConnection()
    {
        Console.WriteLine("CleanupConnection");
        if (_injectThread != null)
        {
            Kernel32.WaitForSingleObject(_injectThread, Kernel32.INFINITE);
        }
    }

    public override void Dispose(bool isDisposing)
    {
        Console.WriteLine("Dispose");
        base.Dispose(isDisposing);

        if (isDisposing)
        {
            _injectThread?.Dispose();
            _remotePipe?.Dispose();
            _pwshAssemblyMem?.Dispose();
            _workerArgs?.Dispose();
            _psdetourNativeMod?.Dispose();
            _remoteProcess?.Dispose();
            _clientPipe?.Dispose();
            _pipe?.Dispose();
        }
    }

    private void ProcessReaderThread(StreamReader pipeReader)
    {
        try
        {
            // Send one fragment.
            SendOneItem();
            using FileStream fs = File.OpenWrite(@"C:\temp\Detour\psremoting.xml");
            using StreamWriter sw = new(fs);

            // Start reader loop.
            while (true)
            {
                string? data = pipeReader.ReadLine();
                sw.WriteLine(data);
                sw.Flush();
                if (data == null)
                {
                    // End of stream indicates the target process was lost.
                    // Raise transport exception to invalidate the client remote runspace.
                    PSRemotingTransportException psrte = new PSRemotingTransportException(
                        PSRemotingErrorId.IPCServerProcessReportedError,
                        RemotingErrorIdStrings.IPCServerProcessReportedError,
                        RemotingErrorIdStrings.NamedPipeTransportProcessEnded);
                    RaiseErrorHandler(new TransportErrorOccuredEventArgs(psrte, TransportMethodEnum.ReceiveShellOutputEx));
                    break;
                }

                if (data.StartsWith(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend, StringComparison.OrdinalIgnoreCase))
                {
                    // Error message from the server.
                    string errorData = data.Substring(System.Management.Automation.Remoting.Server.NamedPipeErrorTextWriter.ErrorPrepend.Length);
                    HandleErrorDataReceived(errorData);
                }
                else
                {
                    // Normal output data.
                    HandleOutputDataReceived(data);
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Normal reader thread end.
        }
        catch (Exception e)
        {
            string errorMsg = e.Message ?? string.Empty;
            _tracer.WriteMessage("NamedPipeClientSessionTransportManager", "StartReaderThread", Guid.Empty,
                "Transport manager reader thread ended with error: {0}", errorMsg);
        }
    }

    private static SafeRemoteLoadedLibrary LoadRemoteLibrary(SafeProcessHandle process, string module)
    {
        byte[] moduleBytes = Encoding.Unicode.GetBytes(module);
        using (SafeRemoteMemory remoteArgAddr = WriteProcessMemory(process, moduleBytes))
        {
            using SafeNativeHandle thread = CreateRemoteThread(process, PSDetourNative.LoadLibraryAddr.Value,
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
}

public sealed class InjectedPipeConnectionInfo : RunspaceConnectionInfo
{
    public new const int DefaultOpenTimeout = 5000;

    public int ProcessId { get; set; }

    public InjectedPipeConnectionInfo(int processId) : this(processId, DefaultOpenTimeout)
    { }

    public InjectedPipeConnectionInfo(int processId, int openTimeout)
    {
        ProcessId = processId;
        OpenTimeout = openTimeout;
    }

    public override string ComputerName
    {
        get => "localhost";
        set => throw new NotImplementedException();
    }

    public override PSCredential Credential
    {
        get => PSCredential.Empty;
        set => throw new NotImplementedException();
    }

    public override AuthenticationMechanism AuthenticationMechanism
    {
        get => AuthenticationMechanism.Default;
        set => throw new NotImplementedException();
    }

    public override string CertificateThumbprint
    {
        get => string.Empty;
        set => throw new NotImplementedException();
    }

    public override RunspaceConnectionInfo InternalCopy() => new InjectedPipeConnectionInfo(ProcessId, OpenTimeout);

    public override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId,
        string sessionName, PSRemotingCryptoHelper cryptoHelper)
    {
        return new InjectedPipeClientSessionTransportManager(
            connectionInfo: this,
            runspaceId: instanceId,
            cryptoHelper: cryptoHelper);
    }
}
