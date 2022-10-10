using PSDetour.Native;
using System;
using System.IO;
using System.Management.Automation;

namespace PSDetour;

internal static class PSDetourNative
{
    internal static SafeLoadedLibrary? _nativePSDetour = null;

    public static string NativePath { get; } = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(PSDetourNative).Assembly.Location) ?? "", "PSDetourNative.dll"));
    public static string PwshAssemblyDir { get; } = Path.GetDirectoryName(typeof(PSObject).Assembly.Location) ?? "";
    public static Lazy<IntPtr> InjectAddr = new(()
        => Kernel32.GetProcAddress(_nativePSDetour?.DangerousGetHandle() ?? IntPtr.Zero, "inject"));
    public static readonly Lazy<IntPtr> Kernel32Addr = new(()
        => Kernel32.GetModuleHandleW("Kernel32.dll"));
    public static readonly Lazy<IntPtr> LoadLibraryAddr = new(()
        => Kernel32.GetProcAddress(Kernel32Addr.Value, "LoadLibraryW"));
    public static readonly Lazy<IntPtr> FreeLibraryAddr = new(()
        => Kernel32.GetProcAddress(Kernel32Addr.Value, "FreeLibrary"));

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
        if (!File.Exists(PSDetourNative.NativePath))
        {
            throw new FileLoadException($"Missing PSDetourNative.dll, expecting at '{PSDetourNative.NativePath}'");
        }
        PSDetourNative._nativePSDetour = Kernel32.LoadLibraryW(PSDetourNative.NativePath);
    }

    public void OnRemove(PSModuleInfo module)
    {
        PSDetourNative._nativePSDetour?.Dispose();
        PSDetourNative._nativePSDetour = null;
    }
}
