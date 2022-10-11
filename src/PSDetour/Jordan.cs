using PSDetour.Native;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PSDetour;

public static class Hook
{
    private static IntPtr OriginalMethod;
    private static IntPtr DelegateAddr;

    public static void Start(PSDetour.Commands.Hook hook)
    {
        // TODO: Move this into the Hook
        using SafeLoadedLibrary advapi = Kernel32.LoadLibraryW(hook.DllName);
        OriginalMethod = Kernel32.GetProcAddress(advapi.DangerousGetHandle(), hook.MethodName);
        hook.CustomType.GetField("OriginalMethod", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, OriginalMethod);

        Delegate myDelegate = Delegate.CreateDelegate(hook.DelegateType, hook.CustomType, "Invoke");
        DelegateAddr = Marshal.GetFunctionPointerForDelegate(myDelegate);

        Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());
        Detour.DetourAttach(ref OriginalMethod, DelegateAddr);
        Detour.DetourTransactionCommit();
    }

    public static void End()
    {
        Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());
        Detour.DetourDetach(ref OriginalMethod, DelegateAddr);
        Detour.DetourTransactionCommit();
    }

    [DllImport("Advapi32.dll")]
    public static extern IntPtr OpenSCManagerW(
        IntPtr lpMachineName,
        IntPtr lpDatabaseName,
        int dwDesiredAccess);

    [DllImport("Advapi32.dll")]
    public static extern bool OpenProcessToken(
        IntPtr ProcessHandle,
        int DesiredAccess,
        out IntPtr TokenHandle);

    [DllImport("Kernel32.dll")]
    public static extern IntPtr OpenProcess(
        int dwDesiredAccess,
        bool bInheritHandle,
        int dwProcessId);
}
