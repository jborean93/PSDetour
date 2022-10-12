using PSDetour.Commands;
using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PSDetour;

internal sealed class RunningHook : IDisposable
{
    public IntPtr InvokeAddr { get; }
    public Delegate InvokeDelegate { get; }
    public IntPtr OriginalMethod { get; }
    public GCHandle PinnedMethod { get; }
    public object InvokeContext { get; }

    public RunningHook(IntPtr invokeAddr, Delegate invokeDelegate, IntPtr originalMethod, object invokeContext)
    {
        InvokeAddr = invokeAddr;
        InvokeDelegate = invokeDelegate;
        OriginalMethod = originalMethod;
        InvokeContext = invokeContext;
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

    public static void Start()
    {
        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());
        // IntPtr originalMethod = GlobalState.GetProcAddress("Advapi32.dll", "OpenSCManagerW");
        // IntPtr originalMethod = GlobalState.GetProcAddress("Kernel32.dll", "GetCurrentProcessId");
        IntPtr originalMethod = GlobalState.GetProcAddress("Kernel32.dll", "Sleep");

        // Delegate myDelegate = Delegate.CreateDelegate(typeof(OpenSCManagerDelegate), typeof(Hook), "OpenSCManagerFunc");
        // Delegate myDelegate = Delegate.CreateDelegate(typeof(GetCurrentProcessIdDelegate), typeof(Hook), "GetCurrentProcessIdFunc");
        Delegate myDelegate = Delegate.CreateDelegate(typeof(SleepDelegate), typeof(Hook), "SleepFunc");
        IntPtr invokeDelegate = Marshal.GetFunctionPointerForDelegate(myDelegate);

        RunningHook hook = new(invokeDelegate, myDelegate, originalMethod, null);
        RunningHooks.Add(hook);

        Detour.DetourAttach(hook.PinnedMethod.AddrOfPinnedObject(), hook.InvokeAddr);
    }

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

            RunningHook runningHook = new(invokeDelegate, sbkDelegate.NativeDelegate, originalMethod, invokeContext);
            sbkDelegate.ContextType.GetField("OriginalMethod", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.SetValue(invokeContext, runningHook.PinnedMethod);

            Detour.DetourAttach(runningHook.PinnedMethod.AddrOfPinnedObject(), runningHook.InvokeAddr);

            RunningHooks.Add(runningHook);
        }
    }

    public static void End()
    {
        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (RunningHook hook in RunningHooks)
        {
            Detour.DetourDetach(hook.PinnedMethod.AddrOfPinnedObject(), hook.InvokeAddr);
            hook.Dispose();
        }

        RunningHooks.Clear();
    }

    // [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr OpenSCManagerDelegate(IntPtr machineName, IntPtr databaseName, int desiredAccess);

    public static IntPtr OpenSCManagerFunc(IntPtr machineName, IntPtr databaseName, int desiredAccess)
    {
        RunningHook hook = RunningHooks[0];
        OpenSCManagerDelegate dele = Marshal.GetDelegateForFunctionPointer<OpenSCManagerDelegate>(
            Marshal.ReadIntPtr(hook.PinnedMethod.AddrOfPinnedObject()));
        IntPtr res = dele(machineName, databaseName, desiredAccess);

        return res;
    }

    public delegate int GetCurrentProcessIdDelegate();

    public static int GetCurrentProcessIdFunc()
    {
        RunningHook hook = RunningHooks[0];
        GetCurrentProcessIdDelegate dele = Marshal.GetDelegateForFunctionPointer<GetCurrentProcessIdDelegate>(
            Marshal.ReadIntPtr(hook.PinnedMethod.AddrOfPinnedObject()));
        int res = dele();

        return res;
    }

    public delegate void SleepDelegate(int milliseconds);

    public static void SleepFunc(int milliseconds)
    {
        RunningHook hook = RunningHooks[0];
        SleepDelegate dele = Marshal.GetDelegateForFunctionPointer<SleepDelegate>(
            Marshal.ReadIntPtr(hook.PinnedMethod.AddrOfPinnedObject()));
        dele(milliseconds);

        return;
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

    [DllImport("Kernel32.dll")]
    public static extern int GetCurrentProcessId();

    [DllImport("Kernel32.dll")]
    public static extern void Sleep(
        int dwMilliseconds);
}
