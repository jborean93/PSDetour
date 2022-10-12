using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;

namespace PSDetour.Commands;

[Cmdlet(VerbsCommon.New, "PSDetourHook")]
[OutputType(typeof(ScriptBlockHook))]
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

        TypeInformation[] parameterTypes = scriptAst.ParamBlock.Parameters.Select(p => ProcessParameterType(p)).ToArray();

        // TODO: Support the MarshalAs return attribute
        TypeInformation returnType = scriptAst.ParamBlock.Attributes
            .Where(a => a.TypeName.GetReflectionType() == typeof(OutputTypeAttribute) && a.PositionalArguments.Count == 1)
            .Select(a => a.PositionalArguments[0])
            .OfType<TypeExpressionAst>()
            .Select(a => new TypeInformation(a.TypeName.GetReflectionType()!))
            .Cast<TypeInformation>()
            .DefaultIfEmpty(new TypeInformation(typeof(void)))
            .First();

        WriteObject(new ScriptBlockHook(DllName, MethodName, Action, returnType, parameterTypes));
    }

    public static TypeInformation ProcessParameterType(ParameterAst parameter)
    {
        // TODO: Capture In, Out,
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

            if (
                ast.TypeName.GetReflectionType() == typeof(MarshalAsAttribute) &&
                ast.PositionalArguments.Count == 1 &&
                ast.PositionalArguments[0] is MemberExpressionAst unmanagedTypeAst &&
                unmanagedTypeAst.Member.SafeGetValue() is string rawUnmanagedType &&
                unmanagedTypeAst.Expression is TypeExpressionAst typeExpAst &&
                typeExpAst.TypeName.GetReflectionType() == typeof(UnmanagedType) &&
                Enum.TryParse<UnmanagedType>(rawUnmanagedType, true, out var unmanagedType)
            )
            {
                // TODO: Set named values like SizeConst
                marshalAs = new MarshalAsAttribute(unmanagedType);
            }
        }

        return new(paramType, marshalAs: marshalAs, isIn: isIn, isOut: isOut);
    }
}

public class ScriptBlockHook
{
    public string DllName { get; internal set; }
    public string MethodName { get; internal set; }
    public ScriptBlock Action { get; internal set; }
    public TypeInformation ReturnType { get; internal set; }
    public TypeInformation[] ParameterTypes { get; internal set; }

    public ScriptBlockHook(string dllName, string methodName, ScriptBlock action, TypeInformation returnType,
        TypeInformation[] parameterTypes)
    {
        DllName = dllName;
        MethodName = methodName;
        Action = action;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }
}
