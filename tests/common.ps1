$moduleName = (Get-Item ([IO.Path]::Combine($PSScriptRoot, '..', 'module', '*.psd1'))).BaseName
$manifestPath = [IO.Path]::Combine($PSScriptRoot, '..', 'output', $moduleName)

if (-not (Get-Module -Name $moduleName -ErrorAction SilentlyContinue)) {
    Import-Module $manifestPath
}

$global:exampleDllPath = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, "NativeExamples", "bin", "NativeExamples.dll"))
if (-not (Test-Path -LiteralPath $exampleDllPath)) {
    throw "Failed to find NativeExamples dll at '$exampleDllPath'"
}

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace PSDetourTest
{
    public static class Native
    {
        [DllImport("Kernel32.dll")]
        public static extern bool CloseHandle(IntPtr pHandle);

        [DllImport("Kernel32.dll")]
        public static extern int GetCurrentProcessId();

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool CreateSymbolicLinkW(
            [MarshalAs(UnmanagedType.LPWStr)] string lpSymlinkFileName,
            [MarshalAs(UnmanagedType.LPWStr)] string lpTargetFileName,
            int dwFlags);

        [DllImport("Kernel32.dll")]
        public static extern IntPtr OpenProcess(
            int dwDesiredAccess,
            bool bInheritHandle,
            int dwProcessId);

        [DllImport("Advapi32.dll")]
        public static extern bool OpenProcessToken(
            IntPtr hProcess,
            int dwAccess,
            out IntPtr hToken);

        [DllImport("$($exampleDllPath -replace '\\', '\\\\')")]
        public static extern void VoidWithArg(int arg1);
    }

    public static class TestHelpers
    {
        public static Task VoidWithArg(int milliseconds, EventWaitHandle waitHandle)
        {
            return Task.Run(() => VoidWithArgAnotherThread(milliseconds, waitHandle));
        }

        private static void VoidWithArgAnotherThread(int milliseconds, EventWaitHandle waitHandle)
        {
            waitHandle.WaitOne();
            Native.VoidWithArg(milliseconds);
        }
    }

    public class Host : PSHost, IHostSupportsInteractiveSession
    {
        private readonly PSHost PSHost;
        private readonly HostUI HostUI;

        public Host(PSHost host){
            PSHost=host;
            HostUI = new HostUI(PSHost.UI);
        }

        public override CultureInfo CurrentCulture => PSHost.CurrentCulture;

        public override CultureInfo CurrentUICulture => PSHost.CurrentUICulture;

        public override Guid InstanceId => PSHost.InstanceId;

        public override string Name => PSHost.Name;

        public override PSHostUserInterface UI => HostUI;

        public override Version Version => PSHost.Version;

        public override void EnterNestedPrompt()
        {
            PSHost.EnterNestedPrompt();
        }

        public override void ExitNestedPrompt()
        {
            PSHost.ExitNestedPrompt();
        }

        public override void NotifyBeginApplication()
        {
            PSHost.NotifyBeginApplication();
        }

        public override void NotifyEndApplication()
        {
            PSHost.NotifyEndApplication();
        }

        public override void SetShouldExit(int exitCode)
        {
            PSHost.SetShouldExit(exitCode);
        }

        public void PushRunspace(Runspace runspace)
        {
            ((IHostSupportsInteractiveSession)PSHost).PushRunspace(runspace);
        }

        public void PopRunspace()
        {
            ((IHostSupportsInteractiveSession)PSHost).PopRunspace();
        }

        public bool IsRunspacePushed => ((IHostSupportsInteractiveSession)PSHost).IsRunspacePushed;

        public Runspace Runspace => ((IHostSupportsInteractiveSession)PSHost).Runspace;
    }

    public class HostUI : PSHostUserInterface, IHostUISupportsMultipleChoiceSelection
    {
        private readonly PSHostUserInterface PSHostUI;

        public readonly Dictionary<string, List<string>> WriteCalls = new Dictionary<string, List<string>>();

        public HostUI(PSHostUserInterface psHostUI)
        {
            PSHostUI = psHostUI;
            WriteCalls["Debug"] = new List<string>();
            WriteCalls["Error"] = new List<string>();
            WriteCalls["Progress"] = new List<string>();
            WriteCalls["Verbose"] = new List<string>();
            WriteCalls["Warning"] = new List<string>();
            WriteCalls["Write"] = new List<string>();
            WriteCalls["WriteLine"] = new List<string>();
        }

        public override PSHostRawUserInterface RawUI => PSHostUI.RawUI;

        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
        {
            return PSHostUI.Prompt(caption, message, descriptions);
        }

        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
        {
            return PSHostUI.PromptForChoice(caption, message, choices, defaultChoice);
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName, PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
        {
            return PSHostUI.PromptForCredential(caption, message, userName, targetName, allowedCredentialTypes, options);
        }

        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
        {
            return PSHostUI.PromptForCredential(caption, message, userName, targetName);
        }

        public override string ReadLine()
        {
            return "readline response";
        }

        public override SecureString ReadLineAsSecureString()
        {
            return PSHostUI.ReadLineAsSecureString();
        }

        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value)
        {
            WriteCalls["Write"].Add(value);
        }

        public override void Write(string value)
        {
            WriteCalls["Write"].Add(value);
        }

        public override void WriteDebugLine(string message)
        {
            WriteCalls["Debug"].Add(message);
        }

        public override void WriteErrorLine(string value)
        {
            WriteCalls["Error"].Add(value);
        }

        public override void WriteLine(string value)
        {
            WriteCalls["WriteLine"].Add(value);
        }

        public override void WriteProgress(long sourceId, ProgressRecord record)
        {
            WriteCalls["Progress"].Add(record.ToString());
        }

        public override void WriteVerboseLine(string message)
        {
            WriteCalls["Verbose"].Add(message);
        }

        public override void WriteWarningLine(string message)
        {
            WriteCalls["Warning"].Add(message);
        }

        public Collection<int> PromptForChoice(string caption,
            string message,
            Collection<ChoiceDescription> choices,
            IEnumerable<int> defaultChoices)
        {
            return ((IHostUISupportsMultipleChoiceSelection)PSHostUI).PromptForChoice(
                caption, message, choices, defaultChoices);
        }
    }
}
"@
