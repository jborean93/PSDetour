using System;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

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

        Type[] parameterTypes = scriptAst.ParamBlock.Parameters.Select(p => ProcessParameterType(p)).ToArray();
        Type returnType = scriptAst.ParamBlock.Attributes
            .Where(a => a.TypeName.GetReflectionType() == typeof(OutputTypeAttribute) && a.PositionalArguments.Count == 1)
            .Select(a => a.PositionalArguments[0])
            .OfType<TypeExpressionAst>()
            .Select(a => a.TypeName.GetReflectionType())
            .Cast<Type>()
            .DefaultIfEmpty(typeof(void))
            .First();

        WriteObject(new ScriptBlockHook(DllName, MethodName, Action, returnType, parameterTypes));
    }

    public static Type ProcessParameterType(ParameterAst parameter)
    {
        if (parameter.StaticType.IsGenericType && parameter.StaticType.GetGenericTypeDefinition() == typeof(Ref<>))
        {
            return parameter.StaticType.GetGenericArguments().FirstOrDefault(typeof(object)).MakeByRefType();
        }
        else
        {
            return parameter.StaticType;
        }
    }
}

public class ScriptBlockHook
{
    public string DllName { get; internal set; }
    public string MethodName { get; internal set; }
    public ScriptBlock Action { get; internal set; }
    public Type ReturnType { get; internal set; }
    public Type[] ParameterTypes { get; internal set; }

    public ScriptBlockHook(string dllName, string methodName, ScriptBlock action, Type returnType,
        Type[] parameterTypes)
    {
        DllName = dllName;
        MethodName = methodName;
        Action = action;
        ReturnType = returnType;
        ParameterTypes = parameterTypes;
    }
}
