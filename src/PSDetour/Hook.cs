using PSDetour.Commands;
using PSDetour.Native;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace PSDetour;

public static class Hook
{
    private static List<(GCHandle, ScriptBlockDelegate)> RunningDelegates = new();

    public static void Start(ScriptBlockHook[] hooks)
    {
        if (RunningDelegates.Count > 0)
        {
            throw new Exception("Already in transaction");
        }

        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (ScriptBlockHook hook in hooks)
        {
            string hookName = $"{hook.DllName}.{hook.MethodName}";

            ScriptBlockDelegate sbkDelegate;
            if (!GlobalState.NativeDelegates.TryGetValue(hookName, out sbkDelegate!))
            {
                sbkDelegate = ScriptBlockDelegate.Create(
                    hook.DllName,
                    hook.MethodName,
                    hook.ReturnType,
                    hook.ParameterTypes,
                    hook.Action);
                GlobalState.NativeDelegates[hookName] = sbkDelegate;
            }

            GCHandle pin = GCHandle.Alloc(sbkDelegate.OriginalMethod, GCHandleType.Pinned);
            Detour.DetourAttach(pin.AddrOfPinnedObject(), sbkDelegate.NativeAddr);
            RunningDelegates.Add((pin, sbkDelegate));
        }
    }

    public static void End()
    {
        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach ((GCHandle pin, ScriptBlockDelegate hook) in RunningDelegates)
        {
            Detour.DetourDetach(pin.AddrOfPinnedObject(), hook.NativeAddr);
            pin.Free();
        }

        RunningDelegates.Clear();
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
