using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.IO;
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

        Dictionary<string, Dictionary<string, InvokeContext>> detouredContexts = new();

        foreach (DetourHook hook in hooks)
        {
            IntPtr originalMethodPtr;
            if (hook.Address != IntPtr.Zero)
            {
                if (hook.AddressIsOffset)
                {
                    IntPtr dllHandle = GlobalState.GetModuleHandle(hook.DllName);
                    originalMethodPtr = IntPtr.Add(dllHandle, hook.Address.ToInt32());
                }
                else
                {
                    originalMethodPtr = hook.Address;
                }
            }
            else
            {
                originalMethodPtr = GlobalState.GetProcAddress(hook.DllName, hook.MethodName);
            }

            GCHandle originalMethod = GCHandle.Alloc(originalMethodPtr, GCHandleType.Pinned);

            RunningHook runningHook = hook.CreateRunningHook(originalMethod, detouredContexts);

            string extensionLessDllName = Path.GetFileNameWithoutExtension(hook.DllName);
            Dictionary<string, InvokeContext> moduleContexts;
            if (!detouredContexts.TryGetValue(extensionLessDllName, out moduleContexts!))
            {
                moduleContexts = new();
                detouredContexts[extensionLessDllName] = moduleContexts;
            }

            moduleContexts[hook.MethodName] = runningHook.InvokeContext;

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

public abstract class DetourHook
{
    public IntPtr Address { get; }
    public string DllName { get; }
    public string MethodName { get; }
    public bool AddressIsOffset { get; }

    internal DetourHook(string dllName, string methodName, IntPtr address, bool addressIsOffset)
    {
        DllName = dllName;
        MethodName = methodName;
        Address = address;
        AddressIsOffset = addressIsOffset;
    }

    internal abstract RunningHook CreateRunningHook(GCHandle originalMethod,
        Dictionary<string, Dictionary<string, InvokeContext>> detouredContexts);
}

public class InvokeContext
{
    internal GCHandle OriginalMethod { get; }

    public object? State { get; }

    public Dictionary<string, Dictionary<string, InvokeContext>> DetouredModules { get; }

    internal InvokeContext(object? state, Dictionary<string, Dictionary<string, InvokeContext>> detouredModules,
        GCHandle originalMethod)
    {
        State = state;
        DetouredModules = detouredModules;
        OriginalMethod = originalMethod;
    }
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
    /// Contains the dynamic context object. The structure of this object
    /// depends on implementing class.
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
