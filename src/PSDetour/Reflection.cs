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
    private static ModuleBuilder? s_builder = null;
    private static ConstructorInfo? s_marshalAsCtor = null;
    private static ConstructorInfo? s_sbkAstCtor = null;
    private static ConstructorInfo? s_sbkInvokeContextCtor = null;
    private static FieldInfo? s_ipcNamedPipeServerEnabledField = null;
    private static MethodInfo? s_addrOfPinnedObjFunc = null;
    private static MethodInfo? s_cmdletGetContext = null;
    private static MethodInfo? s_createIPCNamedPipeServerFunc = null;
    private static MethodInfo? s_errorRecordFromPSObject = null;
    private static MethodInfo? s_getDelegateForFunc = null;
    private static MethodInfo? s_getOriginalMethodFunc = null;
    private static MethodInfo? s_informationRecordFromPSObject = null;
    private static MethodInfo? s_runServerModeFunc = null;
    private static MethodInfo? s_getLastSystemErrorFunc = null;
    private static MethodInfo? s_sbkSetInvokeLastErrorFunc = null;
    private static MethodInfo? s_sbkToPwshConverterType = null;
    private static MethodInfo? s_sbkWrapInvokeFunc = null;
    private static MethodInfo? s_sbkWrapInvokeVoidFunc = null;

    public static ModuleBuilder Module
    {
        get
        {
            if (s_builder == null)
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

                s_builder = builder.DefineDynamicModule(assemblyName);
            }

            return s_builder;
        }
    }

    public static ConstructorInfo MarshalAsCtor
    {
        get
        {
            if (s_marshalAsCtor == null)
            {
                s_marshalAsCtor = typeof(MarshalAsAttribute).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(UnmanagedType) })!;
            }

            return s_marshalAsCtor;
        }
    }

    public static ConstructorInfo SbkAstCtor
    {
        get
        {
            if (s_sbkAstCtor == null)
            {
                Type paramMetaProviderType = typeof(FunctionDefinitionAst).Assembly.GetType(
                    "System.Management.Automation.Language.IParameterMetadataProvider")!;
                s_sbkAstCtor = typeof(ScriptBlock).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    new[] {
                        paramMetaProviderType,
                        typeof(bool)
                    })!;
            }

            return s_sbkAstCtor;
        }
    }

    public static ConstructorInfo SbkInvokeContextCtor
    {
        get
        {
            if (s_sbkInvokeContextCtor == null)
            {
                s_sbkInvokeContextCtor = typeof(ScriptBlockInvokeContext).GetConstructor(
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

            return s_sbkInvokeContextCtor;
        }
    }

    public static FieldInfo IPCNamedPipeServerEnabledField
    {
        get
        {
            if (s_ipcNamedPipeServerEnabledField == null)
            {
                s_ipcNamedPipeServerEnabledField = typeof(RemoteSessionNamedPipeServer).GetField(
                    "IPCNamedPipeServerEnabled",
                    BindingFlags.NonPublic | BindingFlags.Static)!;
            }

            return s_ipcNamedPipeServerEnabledField;
        }
    }

    public static MethodInfo AddrOfPinnedObjFunc
    {
        get
        {
            if (s_addrOfPinnedObjFunc == null)
            {
                s_addrOfPinnedObjFunc = typeof(GCHandle).GetMethod(
                    "AddrOfPinnedObject",
                    Array.Empty<Type>())!;
            }

            return s_addrOfPinnedObjFunc;
        }
    }

    public static MethodInfo CmdletGetContext
    {
        get
        {
            if (s_cmdletGetContext == null)
            {
                s_cmdletGetContext = typeof(PSCmdlet).GetMethod(
                    "get_Context",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    Array.Empty<Type>())!;
            }

            return s_cmdletGetContext;
        }
    }

    public static MethodInfo CreateIPCNamedPipeServerFunc
    {
        get
        {
            if (s_createIPCNamedPipeServerFunc == null)
            {
                s_createIPCNamedPipeServerFunc = typeof(RemoteSessionNamedPipeServer).GetMethod(
                    "CreateIPCNamedPipeServerSingleton",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    Array.Empty<Type>())!;
            }

            return s_createIPCNamedPipeServerFunc;
        }
    }

    public static MethodInfo ErrorRecordFromPSObject
    {
        get
        {
            if (s_errorRecordFromPSObject == null)
            {
                s_errorRecordFromPSObject = typeof(ErrorRecord).GetMethod(
                    "FromPSObjectForRemoting",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(PSObject) })!;
            }

            return s_errorRecordFromPSObject;
        }
    }

    public static MethodInfo GetDelegateForFunc
    {
        get
        {
            if (s_getDelegateForFunc == null)
            {
                s_getDelegateForFunc = typeof(Marshal).GetMethod(
                    "GetDelegateForFunctionPointer",
                    new[] { typeof(IntPtr) })!;
            }

            return s_getDelegateForFunc;
        }
    }

    public static MethodInfo GetOriginalMethodFunc
    {
        get
        {
            if (s_getOriginalMethodFunc == null)
            {
                s_getOriginalMethodFunc = typeof(InvokeContext).GetMethod(
                    "get_OriginalMethod",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    Array.Empty<Type>())!;
            }

            return s_getOriginalMethodFunc;
        }
    }

    public static MethodInfo InformationRecordFromPSObject
    {
        get
        {
            if (s_informationRecordFromPSObject == null)
            {
                s_informationRecordFromPSObject = typeof(InformationRecord).GetMethod(
                    "FromPSObjectForRemoting",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(PSObject) })!;
            }

            return s_informationRecordFromPSObject;
        }
    }

    public static MethodInfo RunServerModeFunc
    {
        get
        {
            if (s_runServerModeFunc == null)
            {
                s_runServerModeFunc = typeof(RemoteSessionNamedPipeServer).GetMethod(
                    "RunServerMode",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(string) })!;
            }

            return s_runServerModeFunc;
        }
    }

    public static MethodInfo SbkSetInvokeLastErrorFunc
    {
        get
        {
            if (s_sbkSetInvokeLastErrorFunc == null)
            {
                s_sbkSetInvokeLastErrorFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.SetInvokeLastError),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    new[] { typeof(int) })!;
            }

            return s_sbkSetInvokeLastErrorFunc;
        }
    }

    public static MethodInfo SbkWrapInvokeFunc
    {
        get
        {
            if (s_sbkWrapInvokeFunc == null)
            {
                s_sbkWrapInvokeFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.WrapInvoke),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlockInvokeContext), typeof(object[]) })!;
            }

            return s_sbkWrapInvokeFunc;
        }
    }

    public static MethodInfo SbkWrapInvokeVoidFunc
    {
        get
        {
            if (s_sbkWrapInvokeVoidFunc == null)
            {
                s_sbkWrapInvokeVoidFunc = typeof(ScriptBlockInvokeContext).GetMethod(
                    nameof(ScriptBlockInvokeContext.WrapInvokeVoid),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlockInvokeContext), typeof(object[]) })!;
            }

            return s_sbkWrapInvokeVoidFunc;
        }
    }

    public static MethodInfo SbkToPwshConverterType
    {
        get
        {
            if (s_sbkToPwshConverterType == null)
            {
                Type actualType = typeof(PSObject).Assembly.GetType(
                    "System.Management.Automation.ScriptBlockToPowerShellConverter", true)!;
                Type contextType = typeof(PSObject).Assembly.GetType(
                    "System.Management.Automation.ExecutionContext", true)!;

                s_sbkToPwshConverterType = actualType.GetMethod(
                    "GetUsingValuesAsDictionary",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    new[] { typeof(ScriptBlock), typeof(bool), contextType, typeof(Dictionary<string, object>) })!;
            }

            return s_sbkToPwshConverterType;
        }
    }

    public static MethodInfo GetLastSystemErrorFunc
    {
        get
        {
            if (s_getLastSystemErrorFunc == null)
            {
                s_getLastSystemErrorFunc = typeof(Marshal).GetMethod(
                    nameof(Marshal.GetLastSystemError),
                    BindingFlags.Static | BindingFlags.Public,
                    Array.Empty<Type>())!;
            }

            return s_getLastSystemErrorFunc;
        }
    }
}
