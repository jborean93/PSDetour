$moduleName = (Get-Item ([IO.Path]::Combine($PSScriptRoot, '..', 'module', '*.psd1'))).BaseName
$manifestPath = [IO.Path]::Combine($PSScriptRoot, '..', 'output', $moduleName)

if (-not (Get-Module -Name $moduleName -ErrorAction SilentlyContinue)) {
    Import-Module $manifestPath
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

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

        [DllImport("Advapi32.dll")]
        public static extern bool OpenProcessToken(
            IntPtr hProcess,
            int dwAccess,
            out IntPtr hToken);

        [DllImport("Kernel32.dll")]
        public static extern void Sleep(int dwMilliSeconds);
    }
}
'@
