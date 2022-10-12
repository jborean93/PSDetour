using PSDetour.Commands;
using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PSDetour;

internal sealed class RunningHook : IDisposable
{
    public IntPtr InvokeDelegate { get; }
    public IntPtr OriginalMethod { get; }
    public GCHandle PinnedMethod { get; }

    public RunningHook(IntPtr invokeDelegate, IntPtr originalMethod)
    {
        InvokeDelegate = invokeDelegate;
        OriginalMethod = originalMethod;
        PinnedMethod = GCHandle.Alloc(originalMethod, GCHandleType.Pinned);
    }

    public void Dispose()
    {
        PinnedMethod.Free();
        GC.SuppressFinalize(this);
    }
    ~RunningHook() => Dispose();
}

public static class Hook
{
    private static List<RunningHook> RunningHooks = new();

    public static void Start(ScriptBlockHook[] hooks)
    {
        if (RunningHooks.Count > 0)
        {
            throw new Exception("Already in transaction");
        }

        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (ScriptBlockHook hook in hooks)
        {
            IntPtr originalMethod = GlobalState.GetProcAddress(hook.DllName, hook.MethodName);

            string hookName = $"{hook.DllName}.{hook.MethodName}";
            ScriptBlockDelegate sbkDelegate = ScriptBlockDelegate.Create(
                hook.DllName,
                hook.MethodName,
                hook.ReturnType,
                hook.ParameterTypes);

            object invokeContext = Activator.CreateInstance(sbkDelegate.ContextType)!;
            sbkDelegate.RunnerType
                .GetField("Action", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, hook.Action);
            sbkDelegate.RunnerType
                .GetField("ThisContext", BindingFlags.NonPublic | BindingFlags.Static)
                ?.SetValue(null, invokeContext);

            IntPtr invokeDelegate = Marshal.GetFunctionPointerForDelegate(sbkDelegate.NativeDelegate);

            RunningHook runningHook = new(invokeDelegate, originalMethod);
            sbkDelegate.ContextType.GetField("OriginalMethod", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(invokeContext, runningHook.PinnedMethod);
            sbkDelegate.ContextType.GetField("OriginalMethod2", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(invokeContext, originalMethod);

            Detour.DetourAttach(runningHook.PinnedMethod.AddrOfPinnedObject(), runningHook.InvokeDelegate);
            RunningHooks.Add(runningHook);
        }
    }

    public static void End()
    {
        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (RunningHook hook in RunningHooks)
        {
            Detour.DetourDetach(hook.PinnedMethod.AddrOfPinnedObject(), hook.InvokeDelegate);
            hook.Dispose();
        }

        RunningHooks.Clear();
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
