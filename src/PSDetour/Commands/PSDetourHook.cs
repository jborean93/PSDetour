using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Host;
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

        TypeInformation returnType = ProcessOutputType(
            (IEnumerable<AttributeAst>?)scriptAst.ParamBlock?.Attributes ?? Array.Empty<AttributeAst>());
        TypeInformation[] parameterTypes = scriptAst.ParamBlock?.Parameters
            ?.Select(p => ProcessParameterType(p))
            ?.ToArray() ?? Array.Empty<TypeInformation>();

        Dictionary<string, object> usingVars = ScriptBlockToPowerShellConverter.GetUsingValuesAsDictionary(
            Action, true, Context, null);

        WriteObject(new ScriptBlockHook(DllName, MethodName, Action, returnType, parameterTypes, Host, usingVars));
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

public class ScriptBlockHook
{
    public string DllName { get; internal set; }
    public string MethodName { get; internal set; }
    public ScriptBlock Action { get; internal set; }
    public TypeInformation ReturnType { get; internal set; }
    public TypeInformation[] ParameterTypes { get; internal set; }
    public PSHost? Host { get; internal set; }
    internal Dictionary<string, object> UsingVars { get; set; }

    public ScriptBlockHook(string dllName, string methodName, ScriptBlock action, TypeInformation returnType,
        TypeInformation[] parameterTypes, PSHost? host, Dictionary<string, object> usingVars)
    {
        DllName = dllName;
        MethodName = methodName;
        Action = action;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
        Host = host;
        UsingVars = usingVars;
    }
}
