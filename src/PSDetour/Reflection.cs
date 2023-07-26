using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Language;
using System.Management.Automation.Remoting;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PSDetour;

internal static class ReflectionInfo
{
    private static ModuleBuilder? _builder = null;
    private static ConstructorInfo? _marshalAsCtor = null;
    private static ConstructorInfo? _sbkAstCtor = null;
    private static ConstructorInfo? _sbkInvokeContextCtor = null;
    private static FieldInfo? _ipcNamedPipeServerEnabledField = null;
    private static MethodInfo? _addrOfPinnedObjFunc = null;
    private static MethodInfo? _cmdletGetContext = null;
    private static MethodInfo? _createIPCNamedPipeServerFunc = null;
    private static MethodInfo? _errorRecordFromPSObject = null;
    private static MethodInfo? _getDelegateForFunc = null;
    private static MethodInfo? _getOriginalMethodFunc = null;
    private static MethodInfo? _informationRecordFromPSObject = null;
    private static MethodInfo? _runServerModeFunc = null;
    private static MethodInfo? _sbkGetLastErrorFunc = null;
    private static MethodInfo? _sbkToPwshConverterType = null;
    private static MethodInfo? _sbkWrapInvokeFunc = null;
    private static MethodInfo? _sbkWrapInvokeVoidFunc = null;
    private static MethodInfo? _setLastPInvokeErrorFunc = null;

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

    public static ConstructorInfo SbkAstCtor
    {
        get
        {
            if (_sbkAstCtor == null)
            {
                Type paramMetaProviderType = typeof(FunctionDefinitionAst).Assembly.GetType(
                    "System.Management.Automation.Language.IParameterMetadataProvider")!;
                _sbkAstCtor = typeof(ScriptBlock).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] {
                        paramMetaProviderType,
                        typeof(bool)
                    })!;
            }

            return _sbkAstCtor;
        }
    }

    public static ConstructorInfo SbkInvokeContextCtor
    {
        get
        {
            if (_sbkInvokeContextCtor == null)
            {
                _sbkInvokeContextCtor = typeof(ScriptBlockInvokeContext).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] {
                        typeof(ScriptBlock),
                        typeof(PSHost),
                        typeof(Dictionary<string, object>),
                        typeof(object),
                        typeof(Dictionary<string, Dictionary<string, InvokeContext>>),
                        typeof(GCHandle)
                    })!;
            }

            return _sbkInvokeContextCtor;
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

    public static MethodInfo ErrorRecordFromPSObject
    {
        get
        {
            if (_errorRecordFromPSObject == null)
            {
                _errorRecordFromPSObject = typeof(ErrorRecord).GetMethod(
                    "FromPSObjectForRemoting",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(PSObject) })!;
            }

            return _errorRecordFromPSObject;
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

    public static MethodInfo InformationRecordFromPSObject
    {
        get
        {
            if (_informationRecordFromPSObject == null)
            {
                _informationRecordFromPSObject = typeof(InformationRecord).GetMethod(
                    "FromPSObjectForRemoting",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(PSObject) })!;
            }

            return _informationRecordFromPSObject;
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

    public static MethodInfo SbkGetLastErrorFunc
    {
        get
        {
            if (_sbkGetLastErrorFunc == null)
            {
                _sbkGetLastErrorFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.GetLastError),
                    BindingFlags.Static | BindingFlags.NonPublic,
                    Array.Empty<Type>())!;
            }

            return _sbkGetLastErrorFunc;
        }
    }

    public static MethodInfo SbkWrapInvokeFunc
    {
        get
        {
            if (_sbkWrapInvokeFunc == null)
            {
                _sbkWrapInvokeFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.WrapInvoke),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlockInvokeContext), typeof(object[]) })!;
            }

            return _sbkWrapInvokeFunc;
        }
    }

    public static MethodInfo SbkWrapInvokeVoidFunc
    {
        get
        {
            if (_sbkWrapInvokeVoidFunc == null)
            {
                _sbkWrapInvokeVoidFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.WrapInvokeVoid),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlockInvokeContext), typeof(object[]) })!;
            }

            return _sbkWrapInvokeVoidFunc;
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
}
