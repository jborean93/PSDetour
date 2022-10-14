using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace PSDetour;

internal class ScriptBlockDelegate
{
    /// <summary>
    /// The static type that contains the Invoke method that is used to create
    /// the delegate for dotnet to use with Detours. This is created
    /// dynamically based on the scriptblock's parameters and output type. It
    /// also contains some static fields for the hook manager to store further
    /// state about the hook itself.
    /// </summary>
    private Type InvokeType;

    /// <summary>
    /// The type that is used for the $this variable during the hook
    /// invocation. It currently only exposes the Invoke method that can be
    /// used by the hook to call the actual method it is hooking.
    /// </summary>
    private Type ContextType;

    /// <summary>
    /// The type that represents a dotnet delegate that is used by dotnet to
    /// marshal the data crossing the boundaries between managed and unmanaged
    /// code.
    /// </summary>
    private Type DelegateType;

    internal ScriptBlockDelegate(Type invokeType, Type contextType, Type delegateType)
    {
        InvokeType = invokeType;
        ContextType = contextType;
        DelegateType = delegateType;
    }

    /// <summary>Creates a context object to use for the delegate.</summary>
    /// <param name="originalMethod">
    /// The pointer to the original method that is being hook.
    /// </param>
    /// <returns>The context object.</returns>
    internal object CreateInvokeContext(GCHandle originalMethod)
    {
        object invokeContext = Activator.CreateInstance(ContextType)!;
        ContextType
            .GetField("OriginalMethod", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(invokeContext, originalMethod);

        return invokeContext;
    }

    /// <summary>Creates a dotnet delegate for the hook.</summary>
    /// <param name="action">
    /// The scriptblock that should be run for the hook.
    /// </param>
    /// <param name="invokeContext">The context object for the hook.</param>
    /// <returns>The dotnet delegate to use with Detours.</returns>
    internal Delegate CreateNativeDelegate(ScriptBlock action, object invokeContext)
    {
        InvokeType
            .GetField("Action", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, action);
        InvokeType
            .GetField("ThisContext", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, invokeContext);

        return Delegate.CreateDelegate(DelegateType, null, InvokeType.GetMethod("Invoke")!);
    }

    /*
    Create will generate a dynamic method and delegate based on the
    arg type and return types. The generated type looks something like this.

    public static class InvokeType
    {
        private static ScriptBlock Action;
        private static NativeContext ThisContext;

        public static T Invoke(...)
        {
            List<PSVariable> varSbkVars = new()
            {
                new PSVariable("this", ThisContext),
            };

            Collection<PSObject> varResult;
            fixed (T* ... = ...) // fixes all ref arguments
            {
                varResult = Action.InvokeWithContext(null, varSbkVars, new object[] { ... });
            }
            int count = varResult.Count;
            if (count > 0)
            {
                if (varResult[count - 1].BaseObject is T ret)
                {
                    return ret;
                }
            }

            return default;
        }
    }

    public class ContextType
    {
        private GCHandle OriginalMethod;

        private NativeContext() { }

        public T Invoke(...)
        {
            return Marshal.GetDelegateForFunctionPointer<InvokeDelegate>(
                OriginalMethod.AddrOfPinnedObject()
            )(...);
        }
    }

    public delegate T DelegateType(...);
    */

    public static ScriptBlockDelegate Create(string dllName, string methodName, TypeInformation returnType,
        TypeInformation[] parameters)
    {
        string typeName = $"{dllName}.{methodName}-{Guid.NewGuid()}";
        string delegateName = $"{typeName}Delegate";

        Type delegateType = CreateDelegateClass($"{typeName}Delegate", returnType, parameters);

        TypeBuilder thisContextBuilder = ReflectionInfo.Module.DefineType(
            $"{typeName}Invoke",
            TypeAttributes.Public | TypeAttributes.Class);

        FieldBuilder originalMethodField = thisContextBuilder.DefineField(
            "OriginalMethod",
            typeof(GCHandle),
            FieldAttributes.Private);

        CreateThisContextInvokeMethod(thisContextBuilder, originalMethodField, returnType, parameters,
           delegateType, delegateType.GetMethod("Invoke")!);

        // If any of the parameters are ByRef types then a second delegate that
        // accepts the Ref<T> overload should be created. This allows the
        // caller of $this.Invoke() to either pass in the Ref<T> straight
        // or use their own [ref]$val call.
        if (parameters.Where(p => p.Type.IsByRef).Count() > 0)
        {
            TypeInformation[] ptrParameters = parameters
                .Select(p => p.CreatePtrInformation())
                .ToArray();
            Type delegatePtrType = CreateDelegateClass($"{typeName}DelegatePtr", returnType, ptrParameters);

            TypeInformation[] detourParameters = parameters
                .Select(p => p.CreateDetourRefInformation())
                .ToArray();
            CreateThisContextInvokeMethod(thisContextBuilder, originalMethodField, returnType, detourParameters,
                delegatePtrType, delegatePtrType.GetMethod("Invoke")!);
        }

        Type thisContextType = thisContextBuilder.CreateType()!;
        Type runnerType = CreateRunnerClass(typeName, returnType, parameters, thisContextType);

        return new(
            runnerType,
            thisContextType,
            delegateType
        );
    }

    private static Type CreateRunnerClass(string typeName, TypeInformation returnType, TypeInformation[] parameters,
        Type contextType)
    {
        TypeBuilder tb = ReflectionInfo.Module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class);

        FieldBuilder actionField = tb.DefineField(
            "Action",
            typeof(ScriptBlock),
            FieldAttributes.Private | FieldAttributes.Static);

        FieldBuilder thisContextField = tb.DefineField(
            "ThisContext",
            contextType,
            FieldAttributes.Private | FieldAttributes.Static);

        MethodBuilder mb = tb.DefineMethod(
            "Invoke",
            MethodAttributes.Static | MethodAttributes.Public,
            CallingConventions.Standard,
            returnType.Type,
            parameters.Select(p => p.Type).ToArray());

        if (returnType.MarshalAs != null)
        {
            CreateParameter(mb, 0, returnType);
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            CreateParameter(mb, i + 1, parameters[i]);
        }

        ILGenerator il = mb.GetILGenerator();

        LocalBuilder varSbkVars = il.DeclareLocal(typeof(List<PSVariable>));
        LocalBuilder? varResult = null;
        if (returnType.Type != typeof(void))
        {
            varResult = il.DeclareLocal(typeof(Collection<PSObject>));
        }

        // For all the ref types we need a local for the pinned and * types.
        // For easier referencing we use a null placeholder for other types
        // so it can be accessed by
        List<ReferenceLocal?> referenceTypes = new();
        foreach (TypeInformation p in parameters)
        {
            Type? elementType = p.Type.GetElementType();
            if (elementType == null)
            {
                referenceTypes.Add(null);
            }
            else
            {
                Type pointerType = elementType.MakePointerType();

                ConstructorInfo refCtor = typeof(Ref<>)
                    .MakeGenericType(elementType)
                    .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new[] { pointerType })
                    ?? throw new Exception("Failed to find reference constructor for arg");

                referenceTypes.Add(new(
                    il.DeclareLocal(pointerType),
                    il.DeclareLocal(elementType, true),
                    elementType,
                    refCtor
                ));
            }
        }

        /*
            List<PSVariable> varSbkVars = new()
            {
                new PSVariable("this", Action),
            };
        */
        il.Emit(OpCodes.Newobj, ReflectionInfo.ListPSVarCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "this");
        il.Emit(OpCodes.Ldsfld, thisContextField);
        il.Emit(OpCodes.Newobj, ReflectionInfo.PSVarCtor);
        il.Emit(OpCodes.Callvirt, ReflectionInfo.ListPSVarAddFunc);
        il.Emit(OpCodes.Stloc, varSbkVars);

        /*
            // Fixes all the reference types to a pointer
            fixed (...)
        */
        // TODO: move this to above the new list init to share a loop
        for (int i = 0; i < parameters.Length; i++)
        {
            ReferenceLocal? nullableInfo = referenceTypes[i];
            if (nullableInfo == null)
            {
                continue;
            }

            ReferenceLocal referenceInfo = (ReferenceLocal)nullableInfo;

            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Stloc, referenceInfo.Pinned);
            il.Emit(OpCodes.Ldloc, referenceInfo.Pinned);
            il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Stloc, referenceInfo.Pointer);
        }

        /*
            // Uses variable args based on the defined parameters
            Collection<PSObject> varResult = Action.InvokeWithContext(null, varSbkVars, new object[] { ... });
        */
        il.Emit(OpCodes.Ldsfld, actionField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc, varSbkVars);

        il.Emit(OpCodes.Ldc_I4, parameters.Length);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);

            ReferenceLocal? nullableInfo = referenceTypes[i];
            if (nullableInfo == null)
            {
                il.Emit(OpCodes.Ldarg, i);
                il.Emit(OpCodes.Box, parameters[i].Type);
            }
            else
            {
                ReferenceLocal referenceLocal = (ReferenceLocal)nullableInfo;
                il.Emit(OpCodes.Ldloc, referenceLocal.Pointer);
                il.Emit(OpCodes.Newobj, referenceLocal.RefConstructor);
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Callvirt, ReflectionInfo.SbkInvokeWithContextFunc);

        if (varResult != null)
        {
            il.Emit(OpCodes.Stloc_S, varResult);
        }

        // Need to unpin the fixed vars by setting them to 0
        foreach (ReferenceLocal? refArg in referenceTypes)
        {
            if (refArg == null)
            {
                continue;
            }

            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Conv_U);
            il.Emit(OpCodes.Stloc, ((ReferenceLocal)refArg).Pinned);
        }

        if (varResult != null)
        {
            LocalBuilder varCount = il.DeclareLocal(typeof(int));
            LocalBuilder varUncastedResult = il.DeclareLocal(typeof(object));
            LocalBuilder varReturn = il.DeclareLocal(returnType.Type);

            /*
                int count = varResult.Count;
            */
            il.Emit(OpCodes.Ldloc, varResult);
            il.Emit(OpCodes.Callvirt, ReflectionInfo.CollectionPSObjCountFunc);
            il.Emit(OpCodes.Stloc, varCount);

            /*
                if (count > 0)
            */
            Label defaultVal = il.DefineLabel();

            il.Emit(OpCodes.Ldloc, varCount);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ble, defaultVal);

            /*
                if (varResult[count - 1].BaseObject is T ret)
                {
                    return ret;
                }
            */
            il.Emit(OpCodes.Ldloc, varResult);
            il.Emit(OpCodes.Ldloc, varCount);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, ReflectionInfo.CollectionPSObjGetItemFunc);
            il.Emit(OpCodes.Callvirt, ReflectionInfo.PSObjectBaseObjectFunc);
            il.Emit(OpCodes.Stloc, varUncastedResult);
            il.Emit(OpCodes.Ldloc, varUncastedResult);

            il.Emit(OpCodes.Isinst, returnType.Type);
            il.Emit(OpCodes.Brfalse, defaultVal);

            il.Emit(OpCodes.Ldloc, varUncastedResult);
            il.Emit(OpCodes.Unbox_Any, returnType.Type);
            il.Emit(OpCodes.Stloc, varReturn);
            il.Emit(OpCodes.Ldloc, varReturn);
            il.Emit(OpCodes.Ret);

            /*
                return default;
            */
            il.MarkLabel(defaultVal);
            il.Emit(OpCodes.Ldc_I4_0);
        }
        else
        {
            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ret);

        return tb.CreateType()!;
    }

    private static Type CreateDelegateClass(string typeName, TypeInformation returnType,
        TypeInformation[] parameters)
    {
        TypeBuilder tb = ReflectionInfo.Module.DefineType(
            typeName,
            TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder ctorBulder = tb.DefineConstructor(
            MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
        ctorBulder.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        MethodBuilder mb = tb.DefineMethod(
            "Invoke",
            MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Public,
            returnType.Type,
            parameters.Select(p => p.Type).ToArray());
        mb.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        if (returnType.MarshalAs != null)
        {
            CreateParameter(mb, 0, returnType);
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            CreateParameter(mb, i + 1, parameters[i]);
        }

        return tb.CreateType()!;
    }

    private static MethodBuilder CreateThisContextInvokeMethod(TypeBuilder classType, FieldBuilder originalMethodField,
        TypeInformation returnType, TypeInformation[] parameters, Type delegateType, MethodInfo delegateMethod)
    {
        MethodBuilder mb = classType.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            CallingConventions.Standard,
            returnType.Type,
            parameters.Select(p => p.Type).ToArray());

        if (returnType.MarshalAs != null)
        {
            CreateParameter(mb, 0, returnType);
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            CreateParameter(mb, i + 1, parameters[i]);
        }

        ILGenerator il = mb.GetILGenerator();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, originalMethodField);
        il.Emit(OpCodes.Call, ReflectionInfo.AddrOfPinnedObjFunc);
        il.Emit(OpCodes.Ldind_I);
        il.Emit(OpCodes.Call, ReflectionInfo.GetDelegateForFunc.MakeGenericMethod(new[] { delegateType }));
        for (int i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Ldarg, i + 1);
            if (parameters[i].Type.IsGenericType && parameters[i].Type.GetGenericTypeDefinition() == typeof(Ref<>))
            {
                FieldInfo ptrField = parameters[i].Type.GetField("_ptr", BindingFlags.Instance | BindingFlags.NonPublic)!;
                il.Emit(OpCodes.Ldfld, ptrField);
            }
        }
        il.Emit(OpCodes.Callvirt, delegateMethod);
        il.Emit(OpCodes.Ret);

        return mb;
    }

    private static ParameterBuilder CreateParameter(MethodBuilder method, int position, TypeInformation typeInfo)
    {
        ParameterAttributes attrs = ParameterAttributes.None;
        List<CustomAttributeBuilder> customAttrs = new();
        if (typeInfo.MarshalAs is MarshalAsAttribute marshalAs)
        {
            attrs |= ParameterAttributes.HasFieldMarshal;
            customAttrs.Add(new CustomAttributeBuilder(
                ReflectionInfo.MarshalAsCtor,
                new object[] { marshalAs.Value }));
        }
        if (typeInfo.IsIn)
        {
            attrs |= ParameterAttributes.In;
        }
        if (typeInfo.IsOut)
        {
            attrs |= ParameterAttributes.Out;
        }

        ParameterBuilder pb = method.DefineParameter(position, attrs, typeInfo.Name ?? $"arg{position}");
        foreach (CustomAttributeBuilder custom in customAttrs)
        {
            pb.SetCustomAttribute(custom);
        }

        return pb;
    }
}

internal class ReferenceLocal
{
    public LocalBuilder Pointer { get; }
    public LocalBuilder Pinned { get; }
    public Type ActualType { get; }
    public ConstructorInfo RefConstructor { get; }

    public ReferenceLocal(LocalBuilder pointer, LocalBuilder pinned, Type actualType, ConstructorInfo refCstor)
    {
        Pointer = pointer;
        Pinned = pinned;
        ActualType = actualType;
        RefConstructor = refCstor;
    }
}

public sealed class Ref<T> where T : unmanaged
{
    internal unsafe T* _ptr;

    internal unsafe Ref(T* ptr) => _ptr = ptr;

    public unsafe T Value
    {
        get => *_ptr; // do null check and throw if now out of scope
        set => *_ptr = value;
    }
}

public class TypeInformation
{
    public Type Type { get; }
    public string? Name { get; }
    public bool IsIn { get; }
    public bool IsOut { get; }
    public MarshalAsAttribute? MarshalAs { get; }

    public TypeInformation(Type type, string? name = null, MarshalAsAttribute? marshalAs = null, bool isIn = false,
        bool isOut = false)
    {
        Type = type;
        Name = name;
        MarshalAs = marshalAs;
        IsIn = isIn;
        IsOut = isOut;
    }

    internal TypeInformation CreatePtrInformation()
    {
        if (Type.IsByRef)
        {
            return new(Type.GetElementType()!.MakePointerType(), name: Name);
        }
        else
        {
            return this;
        }
    }

    internal TypeInformation CreateDetourRefInformation()
    {
        if (Type.IsByRef)
        {
            return new(typeof(Ref<>).MakeGenericType(Type.GetElementType()!), name: Name);
        }
        else
        {
            return this;
        }
    }
}
