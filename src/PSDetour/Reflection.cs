using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Remoting;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PSDetour;

internal static class ReflectionInfo
{
    private static ModuleBuilder? _builder = null;
    private static ConstructorInfo? _invokeContextCtor = null;
    private static ConstructorInfo? _marshalAsCtor = null;
    private static FieldInfo? _ipcNamedPipeServerEnabledField = null;
    private static MethodInfo? _addrOfPinnedObjFunc = null;
    private static MethodInfo? _cmdletGetContext = null;
    private static MethodInfo? _createIPCNamedPipeServerFunc = null;
    private static MethodInfo? _getDelegateForFunc = null;
    private static MethodInfo? _getLastErrorFunc = null;
    private static MethodInfo? _getOriginalMethodFunc = null;
    private static MethodInfo? _runServerModeFunc = null;
    private static MethodInfo? _sbkToPwshConverterType = null;
    private static MethodInfo? _setLastPInvokeErrorFunc = null;
    private static MethodInfo? _wrapInvokeFunc = null;
    private static MethodInfo? _wrapInvokeVoidFunc = null;

    public static ModuleBuilder Module
    {
        get
        {
            if (_builder == null)
            {
                const string assemblyName = "PSDetour.Dynamic";
                AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(
                    new(assemblyName),
                    AssemblyBuilderAccess.Run);

                // CustomAttributeBuilder ignoresAccessChecksTo = new(
                //     typeof(IgnoresAccessChecksToAttribute).GetConstructor(new Type[] { typeof(string) })!,
                //     new object[] { typeof(GlobalState).Assembly.GetName().Name! }
                // );
                // builder.SetCustomAttribute(ignoresAccessChecksTo);

                _builder = builder.DefineDynamicModule(assemblyName);
            }

            return _builder;
        }
    }

    public static ConstructorInfo InvokeContextCtor
    {
        get
        {
            if (_invokeContextCtor == null)
            {
                _invokeContextCtor = typeof(InvokeContext).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] {
                        typeof(ScriptBlock),
                        typeof(PSHost),
                        typeof(Dictionary<string, object>),
                        typeof(GCHandle)
                    })!;
            }

            return _invokeContextCtor;
        }
    }

    public static ConstructorInfo MarshalAsCtor
    {
        get
        {
            if (_marshalAsCtor == null)
            {
                _marshalAsCtor = typeof(MarshalAsAttribute).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(UnmanagedType) })!;
            }

            return _marshalAsCtor;
        }
    }

    public static FieldInfo IPCNamedPipeServerEnabledField
    {
        get
        {
            if (_ipcNamedPipeServerEnabledField == null)
            {
                _ipcNamedPipeServerEnabledField = typeof(RemoteSessionNamedPipeServer).GetField(
                    "IPCNamedPipeServerEnabled",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
            }

            return _ipcNamedPipeServerEnabledField;
        }
    }

    public static MethodInfo AddrOfPinnedObjFunc
    {
        get
        {
            if (_addrOfPinnedObjFunc == null)
            {
                _addrOfPinnedObjFunc = typeof(GCHandle).GetMethod(
                    "AddrOfPinnedObject",
                    Array.Empty<Type>())!;
            }

            return _addrOfPinnedObjFunc;
        }
    }

    public static MethodInfo CmdletGetContext
    {
        get
        {
            if (_cmdletGetContext == null)
            {
                _cmdletGetContext = typeof(PSCmdlet).GetMethod(
                    "get_Context",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    Array.Empty<Type>())!;
            }

            return _cmdletGetContext;
        }
    }

    public static MethodInfo CreateIPCNamedPipeServerFunc
    {
        get
        {
            if (_createIPCNamedPipeServerFunc == null)
            {
                _createIPCNamedPipeServerFunc = typeof(RemoteSessionNamedPipeServer).GetMethod(
                    "CreateIPCNamedPipeServerSingleton",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    Array.Empty<Type>())!;
            }

            return _createIPCNamedPipeServerFunc;
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

    public static MethodInfo GetLastErrorFunc
    {
        get
        {
            if (_getLastErrorFunc == null)
            {
                _getLastErrorFunc = typeof(InvokeContext).GetMethod(
                    nameof(InvokeContext.GetLastError),
                    BindingFlags.Static | BindingFlags.NonPublic,
                    Array.Empty<Type>())!;
            }

            return _getLastErrorFunc;
        }
    }

    public static MethodInfo GetOriginalMethodFunc
    {
        get
        {
            if (_getOriginalMethodFunc == null)
            {
                _getOriginalMethodFunc = typeof(InvokeContext).GetMethod(
                    "get_OriginalMethod",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    Array.Empty<Type>())!;
            }

            return _getOriginalMethodFunc;
        }
    }

    public static MethodInfo RunServerModeFunc
    {
        get
        {
            if (_runServerModeFunc == null)
            {
                _runServerModeFunc = typeof(RemoteSessionNamedPipeServer).GetMethod(
                    "RunServerMode",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(string) })!;
            }

            return _runServerModeFunc;
        }
    }

    public static MethodInfo SbkToPwshConverterType
    {
        get
        {
            if (_sbkToPwshConverterType == null)
            {
                Type actualType = typeof(PSObject).Assembly.GetType(
                    "System.Management.Automation.ScriptBlockToPowerShellConverter", true)!;
                Type contextType = typeof(PSObject).Assembly.GetType(
                    "System.Management.Automation.ExecutionContext", true)!;

                _sbkToPwshConverterType = actualType.GetMethod(
                    "GetUsingValuesAsDictionary",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlock), typeof(bool), contextType, typeof(Dictionary<string, object>) })!;
            }

            return _sbkToPwshConverterType;
        }
    }

    public static MethodInfo SetLastPInvokeErrorFunc
    {
        get
        {
            if (_setLastPInvokeErrorFunc == null)
            {
                _setLastPInvokeErrorFunc = typeof(Marshal).GetMethod(
                    "SetLastPInvokeError",
                    BindingFlags.Static | BindingFlags.Public,
                    new[] { typeof(int) })!;
            }

            return _setLastPInvokeErrorFunc;
        }
    }

    public static MethodInfo WrapInvokeFunc
    {
        get
        {
            if (_wrapInvokeFunc == null)
            {
                _wrapInvokeFunc = typeof(InvokeContext).GetMethod(
                    nameof(InvokeContext.WrapInvoke),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(InvokeContext), typeof(object[]) })!;
            }

            return _wrapInvokeFunc;
        }
    }

    public static MethodInfo WrapInvokeVoidFunc
    {
        get
        {
            if (_wrapInvokeVoidFunc == null)
            {
                _wrapInvokeVoidFunc = typeof(InvokeContext).GetMethod(
                    nameof(InvokeContext.WrapInvokeVoid),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(InvokeContext), typeof(object[]) })!;
            }

            return _wrapInvokeVoidFunc;
        }
    }
}
