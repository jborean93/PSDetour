using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSDetour.Native;

internal static class Detour
{
    private const int ERROR_INVALID_BLOCK = 1;
    private const int ERROR_INVALID_HANDLE = 1;
    private const int ERROR_INVALID_OPERATION = 1;
    private const int ERROR_NOT_ENOUGH_MEMORY = 1;
    private const int ERROR_INVALID_DATA = 1;


    [DllImport("PSDetourNative.dll", EntryPoint = "DetourAttach")]
    private static extern int NativeDetourAttach(
        ref IntPtr ppPointer,
        IntPtr pDetour);

    public static void DetourAttach(ref IntPtr pointer, IntPtr detour)
    {
        int res = NativeDetourAttach(ref pointer, detour);
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourDetach")]
    private static extern int NativeDetourDetach(
        ref IntPtr ppPointer,
        IntPtr pDetour);

    public static void DetourDetach(ref IntPtr pointer, IntPtr detour)
    {
        int res = NativeDetourDetach(ref pointer, detour);
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourTransactionBegin")]
    private static extern int NativeDetourTransactionBegin();

    public static void DetourTransactionBegin()
    {
        int res = NativeDetourTransactionBegin();
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

    [DllImport("PSDetourNative.dll", EntryPoint = "DetourTransactionCommit")]
    private static extern int NativeDetourTransactionCommit();

    public static void DetourTransactionCommit()
    {
        int res = NativeDetourTransactionCommit();
        if (res != 0)
        {
            throw new Win32Exception(res);
        }
    }

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
