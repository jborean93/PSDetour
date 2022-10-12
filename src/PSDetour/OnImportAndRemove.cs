using PSDetour.Native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PSDetour;

internal static class GlobalState
{
    private static ModuleBuilder? _builder = null;
    private static ConstructorInfo? _listCtor = null;
    private static ConstructorInfo? _psvarCtor = null;
    private static MethodInfo? _addrOfPinnedObj = null;
    private static MethodInfo? _collectionCount = null;
    private static MethodInfo? _collectionGetItem = null;
    private static MethodInfo? _getDelegateForFunc = null;
    private static MethodInfo? _listAdd = null;
    private static MethodInfo? _psobjBaseObject = null;
    private static MethodInfo? _sbkInvokeWithContext = null;

    internal static SafeLoadedLibrary? _nativePSDetour = null;

    public static string NativePath { get; } = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(GlobalState).Assembly.Location) ?? "", "PSDetourNative.dll"));
    public static string PwshAssemblyDir { get; } = Path.GetDirectoryName(typeof(PSObject).Assembly.Location) ?? "";
    public static Lazy<IntPtr> InjectAddr = new(() => GetProcAddress(NativePath, "inject"));
    public static readonly Lazy<IntPtr> LoadLibraryAddr = new(() => GetProcAddress("Kernel32.dll", "LoadLibraryW"));
    public static readonly Lazy<IntPtr> FreeLibraryAddr = new(() => GetProcAddress("Kernel32.dll", "FreeLibrary"));

    public static Dictionary<string, SafeLoadedLibrary> LoadedLibraries { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static ModuleBuilder Builder
    {
        get
        {
            if (_builder == null)
            {
                const string assemblyName = "PSDetour.Dynamic";
                AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(
                    new(assemblyName),
                    AssemblyBuilderAccess.Run);

                CustomAttributeBuilder ignoresAccessChecksTo = new(
                    typeof(IgnoresAccessChecksToAttribute).GetConstructor(new Type[] { typeof(string) })!,
                    new object[] { typeof(GlobalState).Assembly.GetName().Name! }
                );
                builder.SetCustomAttribute(ignoresAccessChecksTo);

                _builder = builder.DefineDynamicModule(assemblyName);
            }

            return _builder;
        }
    }

    public static ConstructorInfo ListCtor
    {
        get
        {
            if (_listCtor == null)
            {
                _listCtor = typeof(List<PSVariable>).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    new Type[0])!;
            }

            return _listCtor;
        }
    }

    public static ConstructorInfo PSVarCtor
    {
        get
        {
            if (_psvarCtor == null)
            {
                // FIXME: Set ReadOnly
                _psvarCtor = typeof(PSVariable).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(string), typeof(object) })!;
            }

            return _psvarCtor;
        }
    }

    public static MethodInfo AddrOfPinnedObj
    {
        get
        {
            if (_addrOfPinnedObj == null)
            {
                _addrOfPinnedObj = typeof(GCHandle).GetMethod(
                    "AddrOfPinnedObject",
                    new Type[0])!;

            }

            return _addrOfPinnedObj;
        }
    }

    public static MethodInfo CollectionCount
    {
        get
        {
            if (_collectionCount == null)
            {
                _collectionCount = typeof(Collection<PSObject>).GetMethod(
                    "get_Count")!;
            }

            return _collectionCount;
        }

    }

    public static MethodInfo CollectionGetItem
    {
        get
        {
            if (_collectionGetItem == null)
            {
                _collectionGetItem = typeof(Collection<PSObject>).GetMethod(
                    "get_Item")!;
            }

            return _collectionGetItem;
        }
    }

    public static MethodInfo GetDelegateForFunc
    {
        get
        {
            if (_getDelegateForFunc == null)
            {
                _getDelegateForFunc = typeof(Marshal).GetMethod(
                    "GetDelegateForFunctionPointer",
                    new[] { typeof(IntPtr) })!;

            }

            return _getDelegateForFunc;
        }
    }

    public static MethodInfo ListAdd
    {
        get
        {
            if (_listAdd == null)
            {
                _listAdd = typeof(List<PSVariable>).GetMethod(
                    "Add",
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(PSVariable) })!;
            }

            return _listAdd;
        }
    }

    public static MethodInfo PSObjectBaseObject
    {
        get
        {
            if (_psobjBaseObject == null)
            {
                _psobjBaseObject = typeof(PSObject).GetMethod(
                    "get_BaseObject")!;
            }

            return _psobjBaseObject;
        }
    }

    public static MethodInfo SbkInvokeWithContext
    {
        get
        {
            if (_sbkInvokeWithContext == null)
            {
                _sbkInvokeWithContext = typeof(ScriptBlock).GetMethod(
                    "InvokeWithContext",
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(IDictionary), typeof(List<PSVariable>), typeof(object[]) })!;
            }

            return _sbkInvokeWithContext;
        }
    }

    public static IntPtr GetProcAddress(string library, string method)
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
