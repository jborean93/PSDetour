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
    private readonly Delegate _nativeDelegate;

    public IntPtr OriginalMethod { get; internal set; }
    public IntPtr NativeAddr { get; internal set; }

    internal ScriptBlockDelegate(Delegate invokeDelegate, IntPtr originalMethod)
    {
        _nativeDelegate = invokeDelegate;
        OriginalMethod = originalMethod;
        NativeAddr = Marshal.GetFunctionPointerForDelegate(_nativeDelegate);
    }

    /*
    Create will generate a dynamic method and delegate based on the
    arg type and return types. The generated type looks something like this.

    public class NativeMethod
    {
        private ScriptBlock Action;
        private IntPtr ThisContext;

        public static T Invoke(...)
        {
            List<PSVariable> varSbkVars = new()
            {
                new PSVariable("this", ThisContext),
            };

            var varResult;
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

    public static delegate T InvokeDelegate(...);
    */

    public static ScriptBlockDelegate Create(string dllName, string methodName, Type returnType, Type[] parameters, ScriptBlock action)
    {
        string typeName = $"{dllName}.{methodName}";
        TypeBuilder tb = GlobalState.Builder.DefineType(
            typeName,
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        FieldBuilder actionField = tb.DefineField(
            "Action",
            typeof(ScriptBlock),
            FieldAttributes.Private | FieldAttributes.Static);

        // FIXME: This should be the $this object that can invoke the actual method
        FieldBuilder thisContextField = tb.DefineField(
            "ThisContext",
            typeof(IntPtr),
            FieldAttributes.Private | FieldAttributes.Static);

        MethodBuilder mb = tb.DefineMethod(
            "Invoke",
            System.Reflection.MethodAttributes.Static | System.Reflection.MethodAttributes.Public,
            CallingConventions.Standard,
            returnType,
            parameters);

        for (int i = 0; i < parameters.Length; i++)
        {
            mb.DefineParameter(i + 1, ParameterAttributes.None, $"arg{i}");
        }

        ILGenerator il = mb.GetILGenerator();

        LocalBuilder varSbkVars = il.DeclareLocal(typeof(List<PSVariable>));
        LocalBuilder varResult = il.DeclareLocal(typeof(Collection<PSObject>));
        LocalBuilder varCount = il.DeclareLocal(typeof(int));
        LocalBuilder varUncastedResult = il.DeclareLocal(typeof(object));
        LocalBuilder varReturn = il.DeclareLocal(returnType);

        // For all the ref types we need a local for the pinned and * types.
        // For easier referencing we use a null placeholder for other types
        // so it can be accessed by
        List<ReferenceLocal?> referenceTypes = new();
        foreach (Type p in parameters)
        {
            Type? elementType = p.GetElementType();
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
        il.Emit(OpCodes.Newobj, GlobalState.ListCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "this");
        il.Emit(OpCodes.Ldsfld, thisContextField);
        il.Emit(OpCodes.Box, typeof(IntPtr));
        il.Emit(OpCodes.Newobj, GlobalState.PSVarCtor);
        il.Emit(OpCodes.Callvirt, GlobalState.ListAdd);
        il.Emit(OpCodes.Nop);
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
                il.Emit(OpCodes.Box, parameters[i]);
            }
            else
            {
                ReferenceLocal referenceLocal = (ReferenceLocal)nullableInfo;
                il.Emit(OpCodes.Ldloc, referenceLocal.Pointer);
                il.Emit(OpCodes.Newobj, referenceLocal.RefConstructor);
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Callvirt, GlobalState.SbkInvokeWithContext);
        il.Emit(OpCodes.Stloc_S, varResult);

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

        /*
            int count = varResult.Count;
        */
        il.Emit(OpCodes.Ldloc, varResult);
        il.Emit(OpCodes.Callvirt, GlobalState.CollectionCount);
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
        il.Emit(OpCodes.Callvirt, GlobalState.CollectionGetItem);
        il.Emit(OpCodes.Callvirt, GlobalState.PSObjectBaseObject);
        il.Emit(OpCodes.Stloc, varUncastedResult);
        il.Emit(OpCodes.Ldloc, varUncastedResult);

        il.Emit(OpCodes.Isinst, returnType);
        il.Emit(OpCodes.Brfalse, defaultVal);

        il.Emit(OpCodes.Ldloc, varUncastedResult);
        il.Emit(OpCodes.Unbox_Any, returnType);
        il.Emit(OpCodes.Stloc, varReturn);
        il.Emit(OpCodes.Ldloc, varReturn);
        il.Emit(OpCodes.Ret);

        /*
            return default;
        */
        il.MarkLabel(defaultVal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        Type newType = tb.CreateType()!;
        Delegate invokeDelegate = CreateDelegateType(typeName, newType.GetMethod("Invoke")!);

        IntPtr originalMethod = GlobalState.GetProcAddress(dllName, methodName);
        newType.GetField("Action", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, action);
        newType.GetField("ThisContext", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, originalMethod);

        return new(invokeDelegate, originalMethod);
    }

    private static Delegate CreateDelegateType(string typeName, MethodInfo method)
    {
        TypeBuilder tb = GlobalState.Builder.DefineType(
            $"{typeName}Delegate",
            TypeAttributes.Sealed | TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder ctorBulder = tb.DefineConstructor(
            MethodAttributes.RTSpecialName | MethodAttributes.HideBySig | MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
        ctorBulder.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        ParameterInfo[] parameters = method.GetParameters();

        MethodBuilder invokeMethod = tb.DefineMethod(
            "Invoke",
            MethodAttributes.HideBySig | MethodAttributes.Virtual | MethodAttributes.Public,
            method.ReturnType,
            parameters.Select(p => p.ParameterType).ToArray());
        invokeMethod.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            invokeMethod.DefineParameter(i + 1, ParameterAttributes.None, parameter.Name);
        }

        return Delegate.CreateDelegate(tb.CreateType()!, null, method);
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

public class Ref<T> where T : unmanaged
{
    private unsafe T* _ptr;

    internal unsafe Ref(T* ptr) => _ptr = ptr;

    public unsafe T Value
    {
        get => *_ptr; // do null check and throw if now out of scope
        set => *_ptr = value;
    }
}
