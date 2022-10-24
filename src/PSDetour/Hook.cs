using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PSDetour;

public static class Hook
{
    private static List<RunningHook> RunningHooks = new();

    public static void Start(IEnumerable<DetourHook> hooks)
    {
        if (RunningHooks.Count > 0)
        {
            throw new Exception("Already in transaction");
        }

        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (DetourHook hook in hooks)
        {
            string hookName = $"{hook.DllName}.{hook.MethodName}";
            IntPtr originalMethodPtr = GlobalState.GetProcAddress(hook.DllName, hook.MethodName);
            GCHandle originalMethod = GCHandle.Alloc(originalMethodPtr, GCHandleType.Pinned);

            RunningHook runningHook = hook.CreateRunningHook(originalMethod);
            runningHook.Attach();
            RunningHooks.Add(runningHook);
        }
    }

    public static void Stop()
    {
        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (RunningHook hook in RunningHooks)
        {
            hook.Dispose();
        }

        RunningHooks.Clear();
    }

    [DllImport("Kernel32.dll")]
    public static extern int GetCurrentProcessId();
}

public abstract class DetourHook
{
    public string DllName { get; }
    public string MethodName { get; }

    public DetourHook(string dllName, string methodName)
    {
        DllName = dllName;
        MethodName = methodName;
    }

    internal abstract RunningHook CreateRunningHook(GCHandle originalMethod);
}

internal sealed class RunningHook : IDisposable
{
    private bool _isAttached = false;

    /// <summary>The ptr to the delegate that is given to Detours.</summary>
    public IntPtr DetourMethod { get; }

    /// <summary>
    /// The delegate that defines how dotnet marshals the native call. This
    /// delegate is generated dynamically from the input scriptblock and must
    /// be kept alive for <c>DetourMethod</c> to remain valid.
    /// <summary>
    public Delegate DetourDelegate { get; }

    /// <summary>
    /// Contains the dynamic context object set as the $this variable during
    /// the hook run. This is also generated dynamically.
    /// </summary>
    public InvokeContext InvokeContext { get; }

    /// <summary>
    /// Contains a pinned ptr to the address of the original method that is
    /// being hooked. GCHandle is used to ensure the address of this pointer
    /// is not moved by the GC as it can be referenced by Detours during the
    /// lifetime of the hook as it updates the value.
    /// </summary>
    public GCHandle OriginalMethod { get; }

    public RunningHook(Delegate detourDelegate, InvokeContext invokeContext, GCHandle originalMethod)
    {
        DetourDelegate = detourDelegate;
        DetourMethod = Marshal.GetFunctionPointerForDelegate(DetourDelegate);
        InvokeContext = invokeContext;
        OriginalMethod = originalMethod;
    }

    public void Attach()
    {
        if (!_isAttached)
        {
            Detour.DetourAttach(OriginalMethod.AddrOfPinnedObject(), DetourMethod);
            _isAttached = true;
        }
    }

    public void Dispose()
    {
        if (_isAttached)
        {
            Detour.DetourDetach(OriginalMethod.AddrOfPinnedObject(), DetourMethod);
            _isAttached = false;
        }
        OriginalMethod.Free();
        GC.SuppressFinalize(this);
    }
    ~RunningHook() => Dispose();
}
