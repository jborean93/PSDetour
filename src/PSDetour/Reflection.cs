using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PSDetour;

internal static class ReflectionInfo
{
    private static ModuleBuilder? _builder = null;
    private static ConstructorInfo? _marshalAsCtor = null;
    private static ConstructorInfo? _listPSVarCtor = null;
    private static ConstructorInfo? _psvarCtor = null;
    private static MethodInfo? _addrOfPinnedObjFunc = null;
    private static MethodInfo? _collectionPSObjCountFunc = null;
    private static MethodInfo? _collectionPSObjGetItemFunc = null;
    private static MethodInfo? _getDelegateForFunc = null;
    private static MethodInfo? _listPSVarAddFunc = null;
    private static MethodInfo? _psobjBaseObjectFunc = null;
    private static MethodInfo? _sbkInvokeWithContextFunc = null;

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

    public static ConstructorInfo ListPSVarCtor
    {
        get
        {
            if (_listPSVarCtor == null)
            {
                _listPSVarCtor = typeof(List<PSVariable>).GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance,
                    new Type[0])!;
            }

            return _listPSVarCtor;
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

    public static MethodInfo AddrOfPinnedObjFunc
    {
        get
        {
            if (_addrOfPinnedObjFunc == null)
            {
                _addrOfPinnedObjFunc = typeof(GCHandle).GetMethod(
                    "AddrOfPinnedObject",
                    new Type[0])!;

            }

            return _addrOfPinnedObjFunc;
        }
    }

    public static MethodInfo CollectionPSObjCountFunc
    {
        get
        {
            if (_collectionPSObjCountFunc == null)
            {
                _collectionPSObjCountFunc = typeof(Collection<PSObject>).GetMethod(
                    "get_Count")!;
            }

            return _collectionPSObjCountFunc;
        }

    }

    public static MethodInfo CollectionPSObjGetItemFunc
    {
        get
        {
            if (_collectionPSObjGetItemFunc == null)
            {
                _collectionPSObjGetItemFunc = typeof(Collection<PSObject>).GetMethod(
                    "get_Item")!;
            }

            return _collectionPSObjGetItemFunc;
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

    public static MethodInfo ListPSVarAddFunc
    {
        get
        {
            if (_listPSVarAddFunc == null)
            {
                _listPSVarAddFunc = typeof(List<PSVariable>).GetMethod(
                    "Add",
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(PSVariable) })!;
            }

            return _listPSVarAddFunc;
        }
    }

    public static MethodInfo PSObjectBaseObjectFunc
    {
        get
        {
            if (_psobjBaseObjectFunc == null)
            {
                _psobjBaseObjectFunc = typeof(PSObject).GetMethod(
                    "get_BaseObject")!;
            }

            return _psobjBaseObjectFunc;
        }
    }

    public static MethodInfo SbkInvokeWithContextFunc
    {
        get
        {
            if (_sbkInvokeWithContextFunc == null)
            {
                _sbkInvokeWithContextFunc = typeof(ScriptBlock).GetMethod(
                    "InvokeWithContext",
                    BindingFlags.Public | BindingFlags.Instance,
                    new[] { typeof(IDictionary), typeof(List<PSVariable>), typeof(object[]) })!;
            }

            return _sbkInvokeWithContextFunc;
        }
    }
}
