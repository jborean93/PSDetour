using PSDetour.Native;
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;

namespace PSDetour;

public static class Hook
{
    private static IntPtr OriginalMethod;
    private static IntPtr DelegateAddr;
    private static ScriptBlock Action;

    public static void Start(PSDetour.Commands.Hook hook)
    {
        // TODO: Move this into the Hook
        using SafeLoadedLibrary advapi = Kernel32.LoadLibraryW(hook.DllName);
        OriginalMethod = Kernel32.GetProcAddress(advapi.DangerousGetHandle(), hook.MethodName);
        hook.CustomType.GetField("OriginalMethod", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, OriginalMethod);

        Action = hook.Action;
        // Delegate myDelegate = Delegate.CreateDelegate(hook.DelegateType, hook.CustomType, "Invoke");
        Delegate myDelegate = Delegate.CreateDelegate(hook.DelegateType, typeof(Hook), "OpenDelegate");
        DelegateAddr = Marshal.GetFunctionPointerForDelegate(myDelegate);
        // Delegate myDelegate = Delegate.CreateDelegate(typeof(OpenProcessTokenDelegate), typeof(Hook), "OpenProc");
        // DelegateAddr = Marshal.GetFunctionPointerForDelegate(myDelegate);

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

    public static bool OpenDelegate(IntPtr handle, int access, ref IntPtr token)
    {
        List<PSVariable> varSbkVars = new()
        {
            new PSVariable("this", OriginalMethod),
        };

        PSReference a = new(token);
        var varResult = Hook.Action.InvokeWithContext(null, varSbkVars, new object[] { handle, access, a });
        token = (IntPtr)a.Value;
        int count = varResult.Count;
        if (count > 0)
        {
            if (varResult[count - 1].BaseObject is bool ret)
            {
                return ret;
            }
        }

        return default;
    }

    public delegate bool OpenProcessTokenDelegate(IntPtr handle, int access, out IntPtr token);

    public static bool OpenProc(IntPtr handle, int access, out IntPtr token)
    {
        Console.WriteLine("test");
        token = (IntPtr)1;
        return false;
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
