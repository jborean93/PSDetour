using Microsoft.Win32.SafeHandles;
using PSDetour.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PSDetour.Commands;

[Cmdlet(VerbsDiagnostic.Trace, "PSDetourProcess")]
public sealed class TracePSDetourProcessCommand : PSCmdlet
{
    private CancellationTokenSource? _cancelToken;
    private List<Dictionary<string, object>> _hooks = new();

    [Parameter(
        Mandatory = true,
        Position = 0,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true
    )]
    [ValidateNotNullOrEmpty]
    public DetourHook[] Hook { get; set; } = Array.Empty<DetourHook>();

    [Parameter]
    [Alias("Process")]
    public ProcessIntString? ProcessId { get; set; }

    [Parameter]
    [Alias("Functions")]
    public IDictionary? FunctionsToDefine { get; set; }

    protected override void ProcessRecord()
    {
        foreach (DetourHook h in Hook)
        {
            if (h is ScriptBlockHook sbkHook)
            {
                _hooks.Add(new Dictionary<string, object>()
                {
                    { "Action", sbkHook.Action.ToString() },
                    { "Address", sbkHook.Address.ToInt64() },
                    { "AddressIsOffset", sbkHook.AddressIsOffset },
                    { "DllName", sbkHook.DllName },
                    { "MethodName", sbkHook.MethodName },
                });
            }
            else
            {
                ErrorRecord err = new(
                    new ArgumentException("Can only use ScriptBlockHooks with Trace-ProcessApi"),
                    "HookNotScriptBlock",
                    ErrorCategory.InvalidArgument,
                    null
                );
                WriteError(err);
            }
        }
    }

    protected override void EndProcessing()
    {
        using AnonymousPipeServerStream readPipe = new(PipeDirection.In, HandleInheritability.None);
        using AnonymousPipeServerStream writePipe = new(PipeDirection.Out, HandleInheritability.None);

        using CancellationTokenSource cancelToken = new();
        _cancelToken = cancelToken;

        const ProcessAccessRights dupRights = ProcessAccessRights.DupHandle;
        using SafeProcessHandle currentProces = Kernel32.GetCurrentProcess();
        SafeProcessHandle targetProcess;

        Runspace? rs = null;
        SafeHandle targetReader;
        SafeHandle targetWriter;
        if (ProcessId == null)
        {
            // InitialSessionState iss = InitialSessionState.CreateDefault2();
            // iss.ImportPSModule(new[] { GlobalState.ModulePath });
            // rs = RunspaceFactory.CreateRunspace(Host, iss);
            // // rs = RunspaceFactory.CreateRunspace(Host);
            // rs.Open();

            // using PowerShell ps = PowerShell.Create();
            // ps.Runspace = rs;
            // ps.AddCommand("Import-Module")
            //     .AddParameter("Name", GlobalState.ModulePath)
            //     .AddParameter("Global", true);
            // ps.Invoke();

            targetProcess = currentProces;
            targetReader = writePipe.ClientSafePipeHandle;
            targetWriter = readPipe.ClientSafePipeHandle;
        }
        else
        {
            rs = DetouredRunspace.Create(
                ProcessId.ProcessObj,
                host: Host,
                cancelToken: _cancelToken?.Token
            );

            targetProcess = Kernel32.OpenProcess(
                ProcessId.ProcessObj.Id,
                dupRights,
                false);
            targetReader = Kernel32.DuplicateHandle(
                currentProces,
                writePipe.ClientSafePipeHandle,
                targetProcess,
                0,
                false,
                DuplicateHandleOptions.SameAccess,
                true);
            writePipe.DisposeLocalCopyOfClientHandle();
            targetWriter = Kernel32.DuplicateHandle(
                currentProces,
                readPipe.ClientSafePipeHandle,
                targetProcess,
                0,
                false,
                DuplicateHandleOptions.SameAccess,
                true);
            readPipe.DisposeLocalCopyOfClientHandle();
        }

        // WriteVerbose("test");
        // WriteObject(5);

        using (targetProcess)
        using (rs)
        using (targetReader)
        using (targetWriter)
        {
            Trace(
                rs,
                readPipe,
                writePipe,
                targetReader,
                targetWriter,
                cancelToken.Token
            );
        }
    }

    protected override void StopProcessing()
    {
        _cancelToken?.Cancel();
    }

    private void Trace(
        Runspace? rs,
        AnonymousPipeServerStream readPipe,
        AnonymousPipeServerStream writePipe,
        SafeHandle targetReader,
        SafeHandle targetWriter,
        CancellationToken cancelToken
    )
    {
        PowerShell ps = PowerShell.Create();
        ps.Runspace = rs;
        ps.AddScript(@"
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [object[]]
    $Hooks,

    [Parameter(Mandatory)]
    [Int64]
    $ReadPipeId,

    [Parameter(Mandatory)]
    [Int64]
    $WritePipeId,

    [Parameter()]
    [System.Collections.IDictionary]
    $FunctionsToDefine
)

$ErrorActionPreference = 'Stop'

$reader = [System.IO.Pipes.AnonymousPipeClientStream]::new(
    [System.IO.Pipes.PipeDirection]::In,
    [Microsoft.Win32.SafeHandles.SafePipeHandle]::new([IntPtr]$ReadPipeId, $false)
)
$writer = [System.IO.Pipes.AnonymousPipeClientStream]::new(
    [System.IO.Pipes.PipeDirection]::Out,
    [Microsoft.Win32.SafeHandles.SafePipeHandle]::new([IntPtr]$WritePipeId, $false)
)

$state = [PSDetour.Commands.TraceState]::new($reader, $writer, $FunctionsToDefine)

[PSDetour.ScriptBlockHook[]]$processedHooks = @(foreach($h in $Hooks) {
    $address = [IntPtr]::Zero
    if ($h.Address) {
        $address = [IntPtr]$h.Address
    }
    $newHook = [PSDetour.ScriptBlockHook]::new(
        $h.DllName,
        $h.MethodName,
        [ScriptBlock]::Create($h.Action),
        $address,
        $h.AddressIsOffset
    )
    $newHook.SetHostContext($Host, $state)
    $newHook
})

[PSDetour.Hook]::Start($processedHooks)
");

        ps.AddParameters(new Dictionary<string, object?>() {
            { "Hooks", _hooks },
            { "ReadPipeId", targetReader.DangerousGetHandle().ToInt64() },
            { "WritePipeId", targetWriter.DangerousGetHandle().ToInt64() },
            { "FunctionsToDefine", FunctionsToDefine },
        });
        try
        {
            ps.Invoke();
        }
        catch (Exception e)
        {
            ErrorRecord err = new(
                e,
                "TraceInvokeError",
                ErrorCategory.NotSpecified,
                null
            );
            err.ErrorDetails = new ErrorDetails($"Failed to start trace session: {e.Message}");
            WriteError(err);
            return;
        }

        bool doCleanup = true;
        try
        {
            while (cancelToken.IsCancellationRequested != true)
            {
                // It will be disposed outside the function.
                using StreamWriter writer = new(writePipe, new UTF8Encoding(), -1, true);

                PipeMessageType messageType;
                string? data;
                try
                {
                    (messageType, data) = ReadPipe(readPipe, cancelToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (data == null)
                {
                    // The pipe has been closed which means the process has
                    // ended. We want to avoid trying to cleanup the handles in
                    // that other process as it will have been ended.
                    if (targetReader is SafeDuplicateHandle)
                    {
                        targetReader.SetHandleAsInvalid();
                    }
                    if (targetWriter is SafeDuplicateHandle)
                    {
                        targetWriter.SetHandleAsInvalid();
                    }
                    doCleanup = false;
                    break;
                }
                else if (messageType == PipeMessageType.Stop)
                {
                    break;
                }

                ProcessPipeMessage(messageType, data, writer);
            }
        }
        finally
        {
            if (doCleanup)
            {
                ps = PowerShell.Create();
                ps.Runspace = rs;
                ps.AddScript("[PSDetour.Hook]::Stop()").Invoke();
            }
        }
    }

    private void ProcessPipeMessage(PipeMessageType messageType, string data, StreamWriter writer)
    {
        switch (messageType)
        {
            case PipeMessageType.Output:
                WriteObject(PSSerializer.Deserialize(data));
                break;
            case PipeMessageType.Error:
                PSObject errObj = (PSObject)PSSerializer.Deserialize(data);
                ErrorRecord err = (ErrorRecord)ReflectionInfo.ErrorRecordFromPSObject.Invoke(
                    null, new[] { errObj })!;
                WriteError(err);
                break;
            case PipeMessageType.Verbose:
                WriteVerbose(data);
                break;
            case PipeMessageType.Warning:
                WriteWarning(data);
                break;
            case PipeMessageType.Debug:
                WriteDebug(data);
                break;
            case PipeMessageType.Information:
                PSObject infoObj = (PSObject)PSSerializer.Deserialize(data);
                InformationRecord info = (InformationRecord)ReflectionInfo.InformationRecordFromPSObject.Invoke(
                    null, new[] { infoObj })!;
                WriteInformation(info);
                break;
            case PipeMessageType.Progress:
                ProgressRecord prog = (ProgressRecord)PSSerializer.Deserialize(data);
                WriteProgress(prog);
                break;
            case PipeMessageType.WriteLine:
                Host?.UI?.WriteLine(data);
                break;
            case PipeMessageType.ReadLine:
                string? response = null;
                try
                {
                    response = Host?.UI?.ReadLine();
                }
                catch (Exception e)
                {
                    // If in non-interactive mode it will raise an exception,
                    // ensure a response is sent back to avoid it being
                    // blocked.
                    response = e.Message;
                }

                writer.WriteLine(response ?? "");
                break;
        }
    }

    private static (PipeMessageType, string?) ReadPipe(PipeStream pipe, CancellationToken cancelToken)
    {
        PipeMessageType messageType = PipeMessageType.Stop;

        byte[] lengthBuffer = new byte[8];
        if (!TryReadPipeRaw(pipe, lengthBuffer, cancelToken))
        {
            return (messageType, null);
        }

        messageType = (PipeMessageType)BitConverter.ToInt32(lengthBuffer, 0);
        int dataLength = BitConverter.ToInt32(lengthBuffer, 4);
        byte[] buffer = new byte[dataLength];

        if (!TryReadPipeRaw(pipe, buffer, cancelToken))
        {
            return (messageType, null);
        }

        return (messageType, Encoding.UTF8.GetString(buffer));
    }

    private static bool TryReadPipeRaw(PipeStream pipe, byte[] data, CancellationToken cancelToken)
    {
        int offset = 0;
        int length = data.Length;
        while (offset < length)
        {
            Task<int> readTask = pipe.ReadAsync(data, offset, length - offset, cancelToken);
            int read = readTask.WaitAsync(cancelToken).ConfigureAwait(false).GetAwaiter().GetResult();
            if (read == 0)
            {
                return false;
            }
            offset += read;
        }

        return true;
    }
}

internal enum PipeMessageType
{
    Stop,
    Output,
    Error,
    Verbose,
    Warning,
    Debug,
    Information,
    Progress,
    WriteLine,
    ReadLine
}

public sealed class TraceState
{
    private Encoding _utf8 = new UTF8Encoding();
    private StreamReader _reader;
    private AnonymousPipeClientStream _writer;
    private Dictionary<string, ScriptBlock> _functions = new();

    public TraceState(AnonymousPipeClientStream reader, AnonymousPipeClientStream writer, IDictionary? functions)
    {
        _reader = new StreamReader(reader, _utf8);
        _writer = writer;

        if (functions != null)
        {
            foreach (DictionaryEntry entry in functions)
            {
                string functionName = entry.Key.ToString() ?? "";
                ScriptBlock functionDef = ScriptBlock.Create(entry.Value?.ToString() ?? "");
                _functions[functionName] = functionDef;
            }
        }
    }

    public ScriptBlock GetFunction(string function)
    {
        return _functions[function];
    }

    public void WriteDebug(string text)
    {
        WritePipe(text, PipeMessageType.Debug);
    }

    public void WriteError(ErrorRecord errorRecord)
    {
        string errorClixml = PSSerializer.Serialize(errorRecord);
        WritePipe(errorClixml, PipeMessageType.Error);
    }

    public void WriteInformation(InformationRecord informationRecord)
    {
        string informationClixml = PSSerializer.Serialize(informationRecord);
        WritePipe(informationClixml, PipeMessageType.Information);
    }

    public void WriteObject(object sendToPipeline) => WriteObject(sendToPipeline, 1);

    public void WriteObject(object sendToPipeline, int depth)
    {
        string objectClixml = PSSerializer.Serialize(sendToPipeline, depth);
        WritePipe(objectClixml, PipeMessageType.Output);
    }

    public void WriteProgress(ProgressRecord progressRecord)
    {
        string progressClixml = PSSerializer.Serialize(progressRecord);
        WritePipe(progressClixml, PipeMessageType.Progress);
    }

    public void WriteVerbose(string text)
    {
        WritePipe(text, PipeMessageType.Verbose);
    }

    public void WriteWarning(string text)
    {
        WritePipe(text, PipeMessageType.Warning);
    }

    public void WriteLine() => WriteLine("");

    public void WriteLine(string line, params string[] arg)
    {
        string formattedLine = string.Format(line, arg);
        WritePipe(formattedLine, PipeMessageType.WriteLine);
    }

    public string ReadLine() => ReadLine(null);

    public string ReadLine(string? prompt)
    {
        if (!string.IsNullOrEmpty(prompt))
        {
            WriteLine(prompt);
        }

        WritePipe("", PipeMessageType.ReadLine);
        return _reader.ReadLine() ?? "";
    }

    public void StopTrace()
    {
        WritePipe("", PipeMessageType.Stop);
    }

    private void WritePipe(string data, PipeMessageType messageType)
    {
        byte[] rawData = _utf8.GetBytes(data);

        Span<byte> typeLength = stackalloc byte[8];
        BitConverter.TryWriteBytes(typeLength, (int)messageType);
        BitConverter.TryWriteBytes(typeLength[4..], rawData.Length);
        _writer.Write(typeLength);
        _writer.Write(rawData, 0, rawData.Length);
        _writer.Flush();
    }
}
