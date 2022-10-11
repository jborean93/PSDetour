using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Reflection.Emit;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourHook")]
[OutputType(typeof(Hook))]
public class NewPSDetourHook : PSCmdlet
{
    [Parameter(
        Mandatory = true,
        Position = 1
    )]
    public string DllName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 2
    )]
    public string MethodName { get; set; } = "";

    [Parameter(
        Mandatory = true,
        Position = 3
    )]
    [Alias("ScriptBlock")]
    public ScriptBlock Action { get; set; } = ScriptBlock.EmptyScriptBlock;

    protected override void EndProcessing()
    {
        if (!(Action.Ast is ScriptBlockAst scriptAst))
        {
            WriteError(
                new ErrorRecord(
                    new ArgumentException($"Expecting ScriptBlockAst for Action but got {Action.Ast.GetType().FullName}"),
                    "NewPSDetourHook.InvalidArgument",
                    ErrorCategory.InvalidArgument,
                    Action)
            );
            return;
        }

        Type[] methodTypes = scriptAst.ParamBlock.Parameters.Select(p => p.StaticType).ToArray();
        Type returnType = scriptAst.ParamBlock.Attributes
            .Where(a => a.TypeName.GetReflectionType() == typeof(OutputTypeAttribute) && a.PositionalArguments.Count == 1)
            .Select(a => a.PositionalArguments[0])
            .OfType<TypeExpressionAst>()
            .Select(a => a.TypeName.GetReflectionType())
            .Cast<Type>()
            .DefaultIfEmpty(typeof(void))
            .First();

        Type customType = GenerateMethod($"{DllName}.{MethodName}", returnType, methodTypes, Action);
        Type delegateType = CreateDelegateType(customType.GetMethod("Invoke")!);

        WriteObject(new Hook(DllName, MethodName, Action, customType, delegateType));
    }


    public static Type GenerateMethod(string name, Type returnType, Type[] parameters, ScriptBlock action)
    {
        ConstructorInfo listCtor = typeof(List<PSVariable>).GetConstructor(BindingFlags.Public | BindingFlags.Instance, new Type[0])
            ?? throw new Exception("Failed to find constructor for List");
        MethodInfo listAdd = typeof(List<PSVariable>).GetMethod("Add")
            ?? throw new Exception("Failed to find Add method for List");
        ConstructorInfo psvarCtor = typeof(PSVariable).GetConstructor(BindingFlags.Public | BindingFlags.Instance, new[] { typeof(string), typeof(object) })
            ?? throw new Exception("Failed to find constructor for PSVariable");
        MethodInfo sbkInvokeWithContext = typeof(ScriptBlock).GetMethod("InvokeWithContext", BindingFlags.Public | BindingFlags.Instance, new[] { typeof(IDictionary), typeof(List<PSVariable>), typeof(object[]) })
            ?? throw new Exception("Failed to find ScriptBlock InvokeWithContext");
        MethodInfo collectionCount = typeof(Collection<PSObject>).GetMethod("get_Count")
            ?? throw new Exception("Failed to find Collection Count");
        MethodInfo getBaseObject = typeof(PSObject).GetMethod("get_BaseObject")
            ?? throw new Exception("Failed to get PSObject BaseObject");
        MethodInfo getItem = typeof(Collection<PSObject>).GetMethod("get_Item")
            ?? throw new Exception("Failed to find Collection get_Item");

        AppDomain domain = AppDomain.CurrentDomain;
        AssemblyName assName = new("CustomIL");
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(assName, AssemblyBuilderAccess.Run);
        ModuleBuilder modBuilder = builder.DefineDynamicModule("CustomIL");
        TypeBuilder tb = modBuilder.DefineType(name, System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        FieldBuilder actionField = tb.DefineField(
            "Action",
            typeof(ScriptBlock),
            FieldAttributes.Public | FieldAttributes.Static);

        FieldBuilder methodField = tb.DefineField(
            "OriginalMethod",
            typeof(IntPtr),
            FieldAttributes.Public | FieldAttributes.Static);

        MethodBuilder mb = tb.DefineMethod("Invoke",
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

        /*
            List<PSVariable> varSbkVars = new()
            {
                new PSVariable("this", Action),
            };
        */
        il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Newobj, listCtor);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldstr, "this");
        il.Emit(OpCodes.Ldsfld, methodField);
        il.Emit(OpCodes.Box, typeof(IntPtr));
        il.Emit(OpCodes.Newobj, psvarCtor);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Stloc_S, varSbkVars);

        /*
            // Uses variable args based on the defined parameters
            Collection<PSObject> varResult = Action.InvokeWithContext(null, varSbkVars, new object[] { ... });
        */
        il.Emit(OpCodes.Ldsfld, actionField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldloc_S, varSbkVars);

        il.Emit(OpCodes.Ldc_I4, parameters.Length);
        il.Emit(OpCodes.Newarr, typeof(object));

        for (int i = 0; i < parameters.Length; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Box, parameters[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Callvirt, sbkInvokeWithContext);
        il.Emit(OpCodes.Stloc_S, varResult);
        il.Emit(OpCodes.Ldloc_S, varResult);

        /*
            int count = varResult.Count;
        */
        il.Emit(OpCodes.Callvirt, collectionCount);
        il.Emit(OpCodes.Stloc_S, varCount);
        il.Emit(OpCodes.Ldloc_S, varCount);

        /*
            if (count > 0)
        */
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Cgt);

        Label defaultVal = il.DefineLabel();
        Label ret = il.DefineLabel();

        il.Emit(OpCodes.Brfalse_S, defaultVal);

        /*
            if (varResult[count - 1].BaseObject is T ret)
            {
                return ret;
            }
        */
        il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ldloc_S, varResult);
        il.Emit(OpCodes.Ldloc_S, varCount);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, getItem);
        il.Emit(OpCodes.Callvirt, getBaseObject);
        il.Emit(OpCodes.Stloc_S, varUncastedResult);
        il.Emit(OpCodes.Ldloc_S, varUncastedResult);
        il.Emit(OpCodes.Isinst, returnType);

        il.Emit(OpCodes.Brfalse_S, defaultVal);

        il.Emit(OpCodes.Ldloc_S, varUncastedResult);
        il.Emit(OpCodes.Unbox_Any, returnType);
        il.Emit(OpCodes.Stloc_S, varReturn);
        il.Emit(OpCodes.Br_S, ret);

        /*
            return default;
        */
        il.MarkLabel(defaultVal);
        il.Emit(OpCodes.Nop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Stloc_S, varReturn);
        il.Emit(OpCodes.Br_S, ret);

        il.MarkLabel(ret);
        il.Emit(OpCodes.Ldloc_S, varReturn);
        il.Emit(OpCodes.Ret);

        Type newType = tb.CreateType()!;
        newType.GetField("Action", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, action);

        return newType;
    }

    /* Above is based on
    public static T OpenDelegate(...)
    {
        List<PSVariable> varSbkVars = new()
        {
            new PSVariable("this", OriginalMethod),
        };
        var varResult = Hook.Action.InvokeWithContext(null, varSbkVars, new object[] { ... });
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
    */

    public static Type CreateDelegateType(MethodInfo method)
    {
        AppDomain domain = AppDomain.CurrentDomain;
        AssemblyName assName = new("CustomILDele");
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(assName, AssemblyBuilderAccess.Run);
        ModuleBuilder modBuilder = builder.DefineDynamicModule("CustomILDele");

        TypeBuilder tb = modBuilder.DefineType(
            $"{method.Name}Delegate",
            System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder ctorBulder = tb.DefineConstructor(
            System.Reflection.MethodAttributes.RTSpecialName | System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
        ctorBulder.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        ParameterInfo[] parameters = method.GetParameters();

        MethodBuilder invokeMethod = tb.DefineMethod(
            "Invoke", System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.Public,
            method.ReturnType, parameters.Select(p => p.ParameterType).ToArray());
        invokeMethod.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        for (int i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            invokeMethod.DefineParameter(i + 1, ParameterAttributes.None, parameter.Name);
        }

        return tb.CreateType()!;
    }
}

public class Hook
{
    public string DllName { get; internal set; }
    public string MethodName { get; internal set; }
    public ScriptBlock Action { get; internal set; }
    public Type CustomType { get; internal set; }
    public Type DelegateType { get; internal set; }

    public Hook(string dllName, string methodName, ScriptBlock action, Type customType, Type delegateType)
    {
        DllName = dllName;
        MethodName = methodName;
        Action = action;
        CustomType = customType;
        DelegateType = delegateType;
    }
}
