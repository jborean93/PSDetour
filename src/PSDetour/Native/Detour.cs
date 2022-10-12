using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSDetour.Native;

internal static class Detour
{
    [DllImport("PSDetourNative.dll", EntryPoint = "DetourAttach")]
    private static extern int NativeDetourAttach(
        IntPtr ppPointer,
        IntPtr pDetour);

    public static void DetourAttach(IntPtr pointer, IntPtr detour)
    {
        int res = NativeDetourAttach(pointer, detour);
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourDetach")]
    private static extern int NativeDetourDetach(
        IntPtr ppPointer,
        IntPtr pDetour);

    public static void DetourDetach(IntPtr pointer, IntPtr detour)
    {
        int res = NativeDetourDetach(pointer, detour);
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourTransactionBegin")]
    private static extern int NativeDetourTransactionBegin();

    public static SafeDetourTransaction DetourTransactionBegin()
    {
        int res = NativeDetourTransactionBegin();
        if (res != 0)
        {
            throw new Win32Exception(res);
        }

        return new SafeDetourTransaction();
    }

    [DllImport("PSDetourNative.dll")]
    public static extern int DetourTransactionCommit();

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourUpdateThread")]
    private static extern int NativeDetourUpdateThread(
        SafeNativeHandle hThread);

    public static void DetourUpdateThread(SafeNativeHandle thread)
    {
        int res = NativeDetourUpdateThread(thread);
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }
}

internal class SafeDetourTransaction : SafeHandle
{
    public SafeDetourTransaction() : base(IntPtr.Zero, true) { }

    public override bool IsInvalid => false;

    protected override bool ReleaseHandle()
    {
        return Detour.DetourTransactionCommit() == 0;
    }
}
