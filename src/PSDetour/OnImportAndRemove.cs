using PSDetour.Native;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace PSDetour;

internal static class GlobalState
{
    internal static SafeLoadedLibrary? _nativePSDetour = null;

    public static string ModulePath = Path.GetFullPath(Path.Combine(
        typeof(GlobalState).Assembly.Location,
        "..",
        "..",
        "..",
        "PSDetour.psd1"));

    public static string NativePath { get; } = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(GlobalState).Assembly.Location) ?? "",
        "..",
        RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        "PSDetourNative.dll"));
    public static string PwshAssemblyDir { get; } = Path.GetDirectoryName(typeof(PSObject).Assembly.Location) ?? "";
    public static Lazy<IntPtr> InjectAddr = new(() => GetProcAddress(NativePath, "inject"));
    public static readonly Lazy<IntPtr> LoadLibraryAddr = new(() => GetProcAddress("Kernel32.dll", "LoadLibraryW"));
    public static readonly Lazy<IntPtr> FreeLibraryAddr = new(() => GetProcAddress("Kernel32.dll", "FreeLibrary"));

    public static Dictionary<string, SafeLoadedLibrary> LoadedLibraries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static IntPtr GetModuleHandle(string library)
    {
        IntPtr modHandle;
        try
        {
            modHandle = Kernel32.GetModuleHandleW(library);
        }
        catch (Win32Exception)
        {
            if (!LoadedLibraries.ContainsKey(library))
            {
                LoadedLibraries.Add(library, Kernel32.LoadLibraryW(library));
            }

            modHandle = LoadedLibraries[library].DangerousGetHandle();
        }

        return modHandle;
    }

    public static IntPtr GetProcAddress(string library, string method)
    {
        IntPtr modHandle = GetModuleHandle(library);
        return Kernel32.GetProcAddress(modHandle, method);
    }

    public static IntPtr GetRemoteInjectAddr(IntPtr remoteAddr)
    {
        int injectOffset = (int)(InjectAddr.Value.ToInt64() - _nativePSDetour?.DangerousGetHandle().ToInt64() ?? 0);
        return IntPtr.Add(remoteAddr, injectOffset);
    }
}

public class OnModuleImportAndRemove : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    public void OnImport()
    {
        if (!File.Exists(GlobalState.NativePath))
        {
            throw new FileLoadException($"Missing PSDetourNative.dll, expecting at '{GlobalState.NativePath}'");
        }
        GlobalState._nativePSDetour = Kernel32.LoadLibraryW(GlobalState.NativePath);
    }

    public void OnRemove(PSModuleInfo module)
    {
        GlobalState._nativePSDetour?.Dispose();
        foreach (SafeLoadedLibrary lib in GlobalState.LoadedLibraries.Values)
        {
            lib.Dispose();
        }
        GlobalState.LoadedLibraries.Clear();
        GlobalState._nativePSDetour = null;
    }
}
