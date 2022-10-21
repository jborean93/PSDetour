using PSDetour.Commands;
using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PSDetour;

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
    public object InvokeContext { get; }

    /// <summary>
    /// Contains a pinned ptr to the address of the original method that is
    /// being hooked. GCHandle is used to ensure the address of this pointer
    /// is not moved by the GC as it can be referenced by Detours during the
    /// lifetime of the hook as it updates the value.
    /// </summary>
    public GCHandle OriginalMethod { get; }

    public RunningHook(Delegate detourDelegate, object invokeContext, GCHandle originalMethod)
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

public static class Hook
{
    private static List<RunningHook> RunningHooks = new();

    public static void Start(IEnumerable<ScriptBlockHook> hooks)
    {
        if (RunningHooks.Count > 0)
        {
            throw new Exception("Already in transaction");
        }

        using var _ = Detour.DetourTransactionBegin();
        Detour.DetourUpdateThread(Kernel32.GetCurrentThread());

        foreach (ScriptBlockHook hook in hooks)
        {
            IntPtr originalMethodPtr = GlobalState.GetProcAddress(hook.DllName, hook.MethodName);
            GCHandle originalMethod = GCHandle.Alloc(originalMethodPtr, GCHandleType.Pinned);

            string hookName = $"{hook.DllName}.{hook.MethodName}";
            ScriptBlockDelegate sbkDelegate = ScriptBlockDelegate.Create(
                hook.DllName,
                hook.MethodName,
                hook.ReturnType,
                hook.ParameterTypes);

            InvokeContext invokeContext = sbkDelegate.CreateInvokeContext(hook.Action, hook.Host, hook.UsingVars,
                originalMethod);
            Delegate invokeDelegate = sbkDelegate.CreateNativeDelegate(invokeContext);

            RunningHook runningHook = new(invokeDelegate, invokeContext, originalMethod);
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
}
