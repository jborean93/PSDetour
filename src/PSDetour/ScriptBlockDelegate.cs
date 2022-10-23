using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;

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
    /// <param name="action">The scriptblock that will be run.</param>
    /// <param name="host">The PSHost to un the scriptblock with.</param>
    /// <param name="usingVars">Variables that are injected into $using:.</param>
    /// <param name="originalMethod">The pointer to the non-detoured method.</param>
    /// <returns>The context object.</returns>
    internal InvokeContext CreateInvokeContext(ScriptBlock action, PSHost? host, Dictionary<string, object> usingVars,
        GCHandle originalMethod)
    {
        return (InvokeContext)Activator.CreateInstance(ContextType, new object?[]
        {
            action,
            host,
            usingVars,
            originalMethod
        })!;
    }

    /// <summary>Creates a dotnet delegate for the hook.</summary>
    /// <param name="invokeContext">The context object for the hook.</param>
    /// <returns>The dotnet delegate to use with Detours.</returns>
    internal Delegate CreateNativeDelegate(InvokeContext invokeContext)
    {
        InvokeType
            .GetField("InvokeContext", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, invokeContext);

        return Delegate.CreateDelegate(DelegateType, null, InvokeType.GetMethod("Invoke")!);
    }

    /*
    Create will generate a dynamic method and delegate based on the
    arg type and return types. The generated type looks something like this.

    public static class InvokeType
    {
        private static InvokeContext InvokeContext;

        public static T Invoke(...)
        {
            fixed (T* ... = ...) // fixes all ref arguments
            {
                return InvokeContext.WrapInvoke(InvokeContext, new object[] { ... });
            }
        }
    }

    public class CustomInvokeContext : InvokeContext
    {
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

        TypeBuilder invokeContextBuilder = ReflectionInfo.Module.DefineType(
            $"{typeName}Invoke",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class,
            typeof(InvokeContext));

        ConstructorBuilder invokeContextCtor = invokeContextBuilder.DefineConstructor(
            System.Reflection.MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(ScriptBlock), typeof(PSHost), typeof(Dictionary<string, object>), typeof(GCHandle) });
        ILGenerator invokeContextCtorIL = invokeContextCtor.GetILGenerator();
        invokeContextCtorIL.Emit(OpCodes.Ldarg, 0);
        invokeContextCtorIL.Emit(OpCodes.Ldarg, 1);
        invokeContextCtorIL.Emit(OpCodes.Ldarg, 2);
        invokeContextCtorIL.Emit(OpCodes.Ldarg, 3);
        invokeContextCtorIL.Emit(OpCodes.Ldarg, 4);
        invokeContextCtorIL.Emit(OpCodes.Call, ReflectionInfo.InvokeContextCtor);
        invokeContextCtorIL.Emit(OpCodes.Ret);

        CreateThisContextInvokeMethod(invokeContextBuilder, returnType, parameters, delegateType,
            delegateType.GetMethod("Invoke")!);

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
            CreateThisContextInvokeMethod(invokeContextBuilder, returnType, detourParameters, delegatePtrType,
                delegatePtrType.GetMethod("Invoke")!);
        }

        Type thisContextType = invokeContextBuilder.CreateType()!;
        Type runnerType = CreateRunnerClass(typeName, returnType, parameters);

        return new(
            runnerType,
            thisContextType,
            delegateType
        );
    }

    private static Type CreateRunnerClass(string typeName, TypeInformation returnType, TypeInformation[] parameters)
    {
        TypeBuilder tb = ReflectionInfo.Module.DefineType(
            typeName,
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class);

        FieldBuilder invokeContextField = tb.DefineField(
            "InvokeContext",
            typeof(InvokeContext),
            FieldAttributes.Private | FieldAttributes.Static);

        MethodBuilder mb = tb.DefineMethod(
            "Invoke",
            System.Reflection.MethodAttributes.Static | System.Reflection.MethodAttributes.Public,
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

        LocalBuilder? varResult = null;
        if (returnType.Type != typeof(void))
        {
            varResult = il.DeclareLocal(returnType.Type);
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
                    .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, new[] { pointerType })!;

                referenceTypes.Add(new(
                    il.DeclareLocal(pointerType),
                    il.DeclareLocal(elementType, true),
                    elementType,
                    refCtor
                ));
            }
        }

        /*
            // Fixes all the reference types to a pointer
            fixed (...)
        */
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
            return WrapInvoke(ThisContext, new object[] { ... });
        */

        il.Emit(OpCodes.Ldsfld, invokeContextField);

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

        if (varResult != null)
        {
            il.Emit(OpCodes.Call, ReflectionInfo.WrapInvokeFunc.MakeGenericMethod(returnType.Type));
            il.Emit(OpCodes.Stloc_S, varResult);
        }
        else
        {
            il.Emit(OpCodes.Call, ReflectionInfo.WrapInvokeVoidFunc);
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
            il.Emit(OpCodes.Ldloc, varResult);
        }

        il.Emit(OpCodes.Ret);

        return tb.CreateType()!;
    }

    private static Type CreateDelegateClass(string typeName, TypeInformation returnType,
        TypeInformation[] parameters)
    {
        TypeBuilder tb = ReflectionInfo.Module.DefineType(
            typeName,
            System.Reflection.TypeAttributes.Sealed | System.Reflection.TypeAttributes.Public,
            typeof(MulticastDelegate));

        ConstructorBuilder ctorBulder = tb.DefineConstructor(
            System.Reflection.MethodAttributes.RTSpecialName | System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Public,
            CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
        ctorBulder.SetImplementationFlags(MethodImplAttributes.CodeTypeMask);

        MethodBuilder mb = tb.DefineMethod(
            "Invoke",
            System.Reflection.MethodAttributes.HideBySig | System.Reflection.MethodAttributes.Virtual | System.Reflection.MethodAttributes.Public,
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

    private static MethodBuilder CreateThisContextInvokeMethod(TypeBuilder classType, TypeInformation returnType,
        TypeInformation[] parameters, Type delegateType, MethodInfo delegateMethod)
    {
        MethodBuilder mb = classType.DefineMethod(
            "Invoke",
            System.Reflection.MethodAttributes.Public,
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
        LocalBuilder originalMethodVar = il.DeclareLocal(typeof(GCHandle));
        LocalBuilder? retVar = null;
        if (returnType.Type != typeof(void))
        {
            retVar = il.DeclareLocal(returnType.Type);
        }

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, ReflectionInfo.GetOriginalMethodFunc);
        il.Emit(OpCodes.Stloc, originalMethodVar);
        il.Emit(OpCodes.Ldloca, originalMethodVar);
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

        if (retVar != null)
        {
            il.Emit(OpCodes.Stloc, retVar);
        }

        il.Emit(OpCodes.Call, ReflectionInfo.GetLastErrorFunc);
        il.Emit(OpCodes.Call, ReflectionInfo.SetLastPInvokeErrorFunc);

        if (retVar != null)
        {
            il.Emit(OpCodes.Ldloc, retVar);
        }
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

public class InvokeContext
{
    private static ThreadLocal<bool> ThreadData = new(() => false);

    internal ScriptBlock Action { get; }
    internal PSHost? Host { get; }
    internal Dictionary<string, object> UsingVariables { get; }
    internal GCHandle OriginalMethod { get; }

    internal InvokeContext(ScriptBlock action, PSHost? host, Dictionary<string, object> usingVariables,
        GCHandle originalMethod)
    {
        Action = action;
        Host = host;
        UsingVariables = usingVariables;
        OriginalMethod = originalMethod;
    }

    // The Invoke method is generated dynamically on the subclasses that inherit this
    // public T Invoke(...)

    [DllImport("Kernel32.dll")]
    internal static extern int GetLastError();

    internal static void WrapInvokeVoid(InvokeContext invokeContext, object[] args)
    {
        MethodInfo invokeMethod = invokeContext.GetType().GetMethod("Invoke",
            args.Select(a => a.GetType()).ToArray())!;

        if (ThreadData.Value)
        {
            invokeMethod.Invoke(invokeContext, args);
        }
        else
        {
            InvokeInRunspace(invokeContext, args);
        }
    }

    internal static T? WrapInvoke<T>(InvokeContext invokeContext, object[] args)
    {
        MethodInfo invokeMethod = invokeContext.GetType().GetMethod("Invoke",
            args.Select(a => a.GetType()).ToArray())!;

        if (ThreadData.Value)
        {
            return (T?)invokeMethod.Invoke(invokeContext, args);
        }

        Collection<PSObject> res = InvokeInRunspace(invokeContext, args);
        if (res.Count > 0)
        {
            if (res[res.Count - 1].BaseObject is T castedRes)
            {
                return castedRes;
            }
        }

        return default;
    }

    internal static Collection<PSObject> InvokeInRunspace(InvokeContext invokeContext, object[] args)
    {
        try
        {
            ThreadData.Value = true;

            InitialSessionState initialSessionState = InitialSessionState.CreateDefault2();
            initialSessionState.Variables.Add(
                new SessionStateVariableEntry("this", invokeContext, "invoke context info"));

            using Runspace rs = RunspaceFactory.CreateRunspace(invokeContext.Host, initialSessionState);
            rs.ThreadOptions = PSThreadOptions.UseCurrentThread;
            rs.Open();

            using PowerShell ps = PowerShell.Create();
            ps.Runspace = rs;
            ps.AddScript(invokeContext.Action.ToString());
            foreach (object arg in args)
            {
                ps.AddArgument(arg);
            }
            ps.AddParameter("--%", invokeContext.UsingVariables);

            Collection<PSObject> vars = ps.Invoke();
            foreach (ErrorRecord err in ps.Streams.Error)
            {
                string errMsg = string.Format("Hook error: {0}\n{1}\n{2}", err.ToString(),
                    err.ScriptStackTrace, err.Exception.ToString());
                invokeContext.Host?.UI?.WriteErrorLine(errMsg);
            }

            return vars;
        }
        finally
        {
            ThreadData.Value = false;
        }
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

public sealed class ScriptBlockHook : DetourHook
{
    private Dictionary<string, object>? _usingVars;
    private PSHost? _host;
    private ScriptBlockAst _scriptAst;
    private ThreadLocal<bool> _threadData = new(() => false);

    public ScriptBlock Action { get; }

    public ScriptBlockHook(string dllName, string methodName, ScriptBlock action)
        : base(dllName, methodName)
    {
        Action = action;
        _scriptAst = (ScriptBlockAst)action.Ast;
    }

    public void SetHostContext(PSCmdlet cmdlet, Dictionary<string, object>? vars = null)
    {
        _host = cmdlet.Host;

        // FUTURE: Should look at using own mechanism for this instead of
        // relying on reflection.
        object? context = ReflectionInfo.CmdletGetContext.Invoke(cmdlet, Array.Empty<object>());
        _usingVars = (Dictionary<string, object>?)ReflectionInfo.SbkToPwshConverterType.Invoke(null, new object?[]
        {
            Action, true, context, vars
        });
    }

    internal override RunningHook CreateRunningHook(GCHandle originalMethod)
    {
        ScriptBlockDelegate sbkDelegate = ScriptBlockDelegate.Create(
            DllName,
            MethodName,
            GetReturnType(),
            GetParameterTypes());
        InvokeContext invokeContext = sbkDelegate.CreateInvokeContext(Action, _host, _usingVars ?? new(),
            originalMethod);
        Delegate invokeDelegate = sbkDelegate.CreateNativeDelegate(invokeContext);

        return new(invokeDelegate, invokeContext, originalMethod);
    }

    private TypeInformation GetReturnType()
    {
        return ProcessOutputType(
            (IEnumerable<AttributeAst>?)_scriptAst.ParamBlock?.Attributes ?? Array.Empty<AttributeAst>());
    }

    private TypeInformation[] GetParameterTypes()
    {
        return _scriptAst.ParamBlock?.Parameters
            ?.Select(p => ProcessParameterType(p))
            ?.ToArray() ?? Array.Empty<TypeInformation>();
    }

    private static TypeInformation ProcessOutputType(IEnumerable<AttributeAst> paramAttributes)
    {
        MarshalAsAttribute? marshalAs = null;

        Type? outputType = null;
        foreach (AttributeBaseAst attr in paramAttributes)
        {
            if (attr is not AttributeAst ast)
            {
                continue;
            }

            if (
                ast.TypeName.GetReflectionType() == typeof(OutputTypeAttribute) &&
                ast.PositionalArguments.Count == 1 &&
                ast.PositionalArguments[0] is TypeExpressionAst outputTypeAst
            )
            {
                outputType = outputTypeAst.TypeName.GetReflectionType();
            }
            else if (marshalAs == null)
            {
                marshalAs = GetMarshalAs(ast);
            }
        }

        return new(outputType ?? typeof(void), marshalAs: marshalAs);
    }

    private static TypeInformation ProcessParameterType(ParameterAst parameter)
    {

        MarshalAsAttribute? marshalAs = null;
        bool isIn = false;
        bool isOut = false;

        Type paramType;
        if (parameter.StaticType.IsGenericType && parameter.StaticType.GetGenericTypeDefinition() == typeof(Ref<>))
        {
            paramType = parameter.StaticType.GetGenericArguments().FirstOrDefault(typeof(object)).MakeByRefType();
        }
        else
        {
            paramType = parameter.StaticType;
        }

        foreach (AttributeBaseAst attr in parameter.Attributes)
        {
            if (attr is not AttributeAst ast)
            {
                continue;
            }

            isIn = isIn || ast.TypeName.GetReflectionType() == typeof(InAttribute);
            isOut = isOut || ast.TypeName.GetReflectionType() == typeof(OutAttribute);
            if (marshalAs == null)
            {
                marshalAs = GetMarshalAs(ast);
            }
        }

        return new(paramType, name: parameter.Name.VariablePath.UserPath, marshalAs: marshalAs,
            isIn: isIn, isOut: isOut);
    }

    private static MarshalAsAttribute? GetMarshalAs(AttributeAst attribute)
    {
        if (
            attribute.TypeName.GetReflectionType() == typeof(MarshalAsAttribute) &&
            attribute.PositionalArguments.Count == 1 &&
            attribute.PositionalArguments[0] is MemberExpressionAst unmanagedTypeAst &&
            unmanagedTypeAst.Member.SafeGetValue() is string rawUnmanagedType &&
            unmanagedTypeAst.Expression is TypeExpressionAst typeExpAst &&
            typeExpAst.TypeName.GetReflectionType() == typeof(UnmanagedType) &&
            Enum.TryParse<UnmanagedType>(rawUnmanagedType, true, out var unmanagedType)
        )
        {
            // TODO: Set named values like SizeConst
            return new MarshalAsAttribute(unmanagedType);
        }

        return null;
    }
}

public sealed class TypeInformation
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
