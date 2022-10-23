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
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if !PWSH72
using System.Globalization;
using System.Diagnostics;
#endif

namespace PSDetour;

#if PWSH72
internal sealed class InjectedPipeClientSessionTransportManager : NamedPipeClientSessionTransportManagerBase
#else
internal sealed class InjectedPipeClientSessionTransportManager : ClientSessionTransportManagerBase
#endif
{
    private const string _threadName = "PSDetour NamedPipeTransport Reader Thread";

    private readonly InjectedPipeConnectionInfo _connectionInfo;

    // Pointers to remote process objects that need to be cleaned up at the end.
    private SafeProcessHandle? _remoteProcess = null;
    private SafeRemoteLoadedLibrary? _psdetourNativeMod = null;
    private SafeRemoteMemory? _pwshAssemblyMem = null;
    private SafeRemoteMemory? _psdetourAssemblyPathMem = null;
    private SafeRemoteMemory? _runtimeConfigPathMem = null;
    private SafeRemoteMemory? _workerArgs = null;
    private SafeNativeHandle? _injectThread = null;

#if !PWSH72
    private NamedPipeClientStream? _clientPipe = null;
#endif

    public InjectedPipeClientSessionTransportManager(
        InjectedPipeConnectionInfo connectionInfo,
        Guid runspaceId,
        PSRemotingCryptoHelper cryptoHelper)
#if PWSH72
        : base(connectionInfo, runspaceId, cryptoHelper, _threadName)
#else
        : base(runspaceId, cryptoHelper)
#endif
    {
        _connectionInfo = connectionInfo;
    }

    protected override void CleanupConnection()
    {
#if PWSH72
        base.CleanupConnection();
#endif
        if (_injectThread != null)
        {
            Kernel32.WaitForSingleObject(_injectThread, Kernel32.INFINITE);
        }
    }

    public override void CreateAsync()
    {
        const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
            ProcessAccessRights.DupHandle |
            ProcessAccessRights.QueryInformation |
            ProcessAccessRights.VMOperation |
            ProcessAccessRights.VMRead |
            ProcessAccessRights.VMWrite;
        _remoteProcess = Kernel32.OpenProcess(_connectionInfo.ProcessId, procRights, false);

        // Load PSDetourNative.dll in the target process
        _psdetourNativeMod = LoadRemoteLibrary(_remoteProcess, GlobalState.NativePath);
        IntPtr remoteInjectAddr = GlobalState.GetRemoteInjectAddr(_psdetourNativeMod.DangerousGetHandle());

        // Create pipe for the current user only that is used for basic
        // comms with injected process.
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

        using NamedPipeServerStream _pipe = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            32768,
            32768,
            pipeSecurity,
            HandleInheritability.None);
        using SafeDuplicateHandle _remotePipe = InjectClientPipeIntoProcess(_pipe, pipeName, _remoteProcess);

        // Build arg struct to remote process
        string assemblyLocation = typeof(InjectedPipeClientSessionTransportManager).Assembly.Location;
        byte[] pwshAssemblyBytes = Encoding.Unicode.GetBytes(GlobalState.PwshAssemblyDir);
        byte[] psdetourAssemblyPathBytes = Encoding.Unicode.GetBytes(assemblyLocation);
        byte[] runtimeConfigPathBytes = Encoding.Unicode.GetBytes(Path.ChangeExtension(assemblyLocation,
            "runtimeconfig.json"));

        _pwshAssemblyMem = WriteProcessMemory(_remoteProcess, pwshAssemblyBytes);
        _psdetourAssemblyPathMem = WriteProcessMemory(_remoteProcess, psdetourAssemblyPathBytes);
        _runtimeConfigPathMem = WriteProcessMemory(_remoteProcess, runtimeConfigPathBytes);

        WorkerArgs remoteArgs = new()
        {
            Pipe = _remotePipe.DangerousGetHandle(),
            PowerShellDir = _pwshAssemblyMem.DangerousGetHandle(),
            AssemblyPath = _psdetourAssemblyPathMem.DangerousGetHandle(),
            RuntimeConfigPath = _runtimeConfigPathMem.DangerousGetHandle(),
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
            byte[] tempBuffer = new byte[1];
            Task<int> readTask = _pipe.ReadAsync(tempBuffer, 0, 1, cancelToken.Token);
            int res = Task.WaitAny(new[] { readTask }, _connectionInfo.OpenTimeout);

            if (res == -1)
            {
                cancelToken.Cancel();
                throw new TimeoutException(
                    $"Timeout while waiting for remote process {_connectionInfo.ProcessId} to connect");
            }

            readTask.GetAwaiter().GetResult();
            if (tempBuffer[0] != 0)
            {
                byte[] errorLengthBuffer = new byte[4];
                _pipe.Read(errorLengthBuffer, 0, errorLengthBuffer.Length);
                int errorLength = BitConverter.ToInt32(errorLengthBuffer, 0);

                byte[] errorMsgBuffer = new byte[errorLength];
                _pipe.Read(errorMsgBuffer, 0, errorMsgBuffer.Length);
                string errorMsg = Encoding.Unicode.GetString(errorMsgBuffer);
                throw new PSRemotingTransportException(
                    $"Error when bootstrapping dotnet onto the remote process {_connectionInfo.ProcessId}: {errorMsg}");
            }
        }

#if PWSH72
        _clientPipe = new RemoteSessionNamedPipeClient(_connectionInfo.ProcessId, "");
        _clientPipe.Connect(_connectionInfo.OpenTimeout);

        stdInWriter = new OutOfProcessTextWriter(_clientPipe.TextWriter);
        StartReaderThread(_clientPipe.TextReader);
#else
        string pwshPipeName = CreateProcessPipeName(_connectionInfo.ProcessId);
        _clientPipe = new(".", pwshPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        _clientPipe.Connect(_connectionInfo.OpenTimeout);

        StreamWriter sw = new(_clientPipe);
        sw.AutoFlush = true;
        SetMessageWriter(sw);
        StartReaderThread(new StreamReader(_clientPipe));
#endif
    }

#if PWSH72
    public override void Dispose(bool isDisposing)
#else
    protected override void Dispose(bool isDisposing)
#endif
    {
        base.Dispose(isDisposing);

        if (isDisposing)
        {
            _injectThread?.Dispose();
            _pwshAssemblyMem?.Dispose();
            _psdetourAssemblyPathMem?.Dispose();
            _runtimeConfigPathMem?.Dispose();
            _workerArgs?.Dispose();
            _psdetourNativeMod?.Dispose();
            _remoteProcess?.Dispose();
        }
    }

#if PWSH72
    public void AbortConnect()
    {
        _clientPipe?.AbortConnect();
    }
#else
    private void StartReaderThread(StreamReader reader)
    {
        Thread readerThread = new Thread(() => ProcessReaderThread(reader));
        readerThread.Name = _threadName;
        readerThread.IsBackground = true;
        readerThread.Start();
    }

    private void ProcessReaderThread(StreamReader reader)
    {
        try
        {
            // Send one fragment.
            SendOneItem();

            // Start reader loop.
            while (true)
            {
                string? data = reader.ReadLine();
                if (data == null)
                {
                    // End of stream indicates that the SSH transport is broken.
                    // SSH will return the appropriate error in StdErr stream so
                    // let the error reader thread report the error.
                    break;
                }

                HandleDataReceived(data);
            }
        }
        catch (ObjectDisposedException)
        {
            // Normal reader thread end.
        }
        catch (Exception e)
        {
            string errorMsg = e.Message ?? string.Empty;
            RaiseErrorHandler(
                new TransportErrorOccuredEventArgs(
                    new PSRemotingTransportException(
                        $"The SSH client session has ended reader thread with message: {errorMsg}"),
                    TransportMethodEnum.CloseShellOperationEx));

            // _connectionInfo.StopConnect();
        }
    }

    private static string CreateProcessPipeName(int pid)
    {
        using Process proc = Process.GetProcessById(pid);
        StringBuilder pipeNameBuilder = new();
        pipeNameBuilder.Append("PSHost.")
            .Append(proc.StartTime.ToFileTime().ToString(CultureInfo.InvariantCulture))
            .Append('.')
            .Append(proc.Id.ToString(CultureInfo.InvariantCulture))
            .Append(".DefaultAppDomain.")
            .Append(proc.ProcessName);

        return pipeNameBuilder.ToString();
    }
#endif

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
}

public sealed class InjectedPipeConnectionInfo : RunspaceConnectionInfo
{
#if PWSH72
    public new const int DefaultOpenTimeout = 5000;
#else
    public const int DefaultOpenTimeout = 5000;
#endif

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

#if PWSH72
    // InternalCopy was renamed to Clone when the API went public
    public override RunspaceConnectionInfo InternalCopy() => new InjectedPipeConnectionInfo(ProcessId, OpenTimeout);
#else
    public override RunspaceConnectionInfo Clone() => new InjectedPipeConnectionInfo(ProcessId, OpenTimeout);
#endif

    public override BaseClientSessionTransportManager CreateClientSessionTransportManager(Guid instanceId,
        string sessionName, PSRemotingCryptoHelper cryptoHelper)
    {
        return new InjectedPipeClientSessionTransportManager(
            connectionInfo: this,
            runspaceId: instanceId,
            cryptoHelper: cryptoHelper);
    }
}
