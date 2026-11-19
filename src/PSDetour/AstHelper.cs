using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PSDetour;

internal static class AstHelper
{
    private static readonly ScriptExtent s_blankExtent = new(new(null, 0, 0, null), new(null, 0, 0, null));

    public static HashtableAst CreateHashtableAst(IDictionary<string, object?> value)
    {
        return new(
            s_blankExtent,
            value.Select(kvp => CreateKeyValuePairAst(kvp))
        );
    }

    public static Tuple<ExpressionAst, StatementAst> CreateKeyValuePairAst(KeyValuePair<string, object?> kvp)
    {
        return new(
            new StringConstantExpressionAst(s_blankExtent, kvp.Key, StringConstantType.BareWord),
            CreateSingleExpressionPipeline(kvp.Value)
        );
    }

    public static PipelineAst CreateSingleExpressionPipeline(object? value)
    {
        ExpressionAst exp;
        if (value == null)
        {
            exp = new VariableExpressionAst(s_blankExtent, "null", false);
        }
        else if (value is int)
        {
            exp = new ConstantExpressionAst(s_blankExtent, value);
        }
        else if (value is IDictionary<string, object?> dict)
        {
            exp = CreateHashtableAst(dict);
        }
        else
        {
            exp = new StringConstantExpressionAst(
                s_blankExtent,
                value.ToString() ?? "",
                StringConstantType.SingleQuoted);
        }

        return new(
            s_blankExtent,
            new CommandBaseAst[]
            {
                new CommandExpressionAst(
                    s_blankExtent,
                    exp,
                    null
                )
            }
        );
    }

    public static ScriptBlockAst CreateScriptBlockWithInjectedFunctions(
        string mainFunctionName,
        string paramBlock,
        IEnumerable<Dictionary<string, object?>> functions
    )
    {
        /*
        # Defines the functions needed for the hook
        @(
            ... # All functions to define
            @{
                Name = "$DllName!$MethodName'
                Value = 'hook script'
                Path = $null  # Or the path to the hook script
                Line = 0
            }
        )
        */
        ArrayExpressionAst functionsArray = new(
            s_blankExtent,
            new(
                s_blankExtent,
                functions.Select(f => AstHelper.CreateSingleExpressionPipeline(f)),
                null
            )
        );

        /*
        # Enumerates the functions and defines them inline with the correct
        # script and script line positions.
        @(...) | ForEach-Object {
            . ([System.Management.Automation.Language.Parser]::ParseInput(
                "$([string]::new("`n", $_.Line))Function $($_.Name) {$($_.Content)}",
                $_.File,
                [ref]$null,
                [ref]$null
            )).GetScriptBlock()
        }
        */

        InvokeMemberExpressionAst parseInput = new(
            s_blankExtent,
            new TypeExpressionAst(
                s_blankExtent,
                new TypeName(s_blankExtent, typeof(Parser).FullName)
            ),
            new StringConstantExpressionAst(s_blankExtent, "ParseInput", StringConstantType.BareWord),
            new ExpressionAst[]
            {
                new ExpandableStringExpressionAst(
                    s_blankExtent,
                    "$([string]::new(\"`n\", $_.Line))Function $($_.Name) {$($_.Value)}",
                    StringConstantType.DoubleQuoted
                ),
                new MemberExpressionAst(
                    s_blankExtent,
                    new VariableExpressionAst(s_blankExtent, "_", false),
                    new StringConstantExpressionAst(s_blankExtent, "Path", StringConstantType.BareWord),
                    false
                ),
                new ConvertExpressionAst(
                    s_blankExtent,
                    new TypeConstraintAst(
                        s_blankExtent,
                        new TypeName(s_blankExtent, "ref")
                    ),
                    new VariableExpressionAst(s_blankExtent, "null", false)
                ),
                new ConvertExpressionAst(
                    s_blankExtent,
                    new TypeConstraintAst(
                        s_blankExtent,
                        new TypeName(s_blankExtent, "ref")
                    ),
                    new VariableExpressionAst(s_blankExtent, "null", false)
                )
            },
            true,
            false
        );
        InvokeMemberExpressionAst getScriptBlock = new(
            s_blankExtent,
            new ParenExpressionAst(
                s_blankExtent,
                new PipelineAst(
                    s_blankExtent,
                    new CommandBaseAst[]
                    {
                        new CommandExpressionAst(
                            s_blankExtent,
                            parseInput,
                            null
                        )
                    },
                    false
                )
            ),
            new StringConstantExpressionAst(s_blankExtent, "GetScriptBlock", StringConstantType.BareWord),
            null,
            false,
            false
        );

        ScriptBlockAst foreachProcess = new(
            s_blankExtent,
            null,
            null,
            null,
            new StatementBlockAst(
                s_blankExtent,
                new StatementAst[]
                {
                    new PipelineAst(
                        s_blankExtent,
                        new CommandAst(
                            s_blankExtent,
                            new CommandElementAst[]
                            {
                                getScriptBlock
                            },
                            TokenKind.Dot,
                            null
                        ),
                        false
                    )
                },
                null
            ),
            false,
            false
        );

        PipelineAst funcDef = new(
            s_blankExtent,
            new CommandBaseAst[]
            {
                new CommandExpressionAst(
                    s_blankExtent,
                    functionsArray,
                    null
                ),
                new CommandAst(
                    s_blankExtent,
                    new CommandElementAst[]
                    {
                        new StringConstantExpressionAst(s_blankExtent, "ForEach-Object", StringConstantType.BareWord),
                        new ScriptBlockExpressionAst(s_blankExtent, foreachProcess)
                    },
                    TokenKind.Unknown,
                    null
                )
            },
            false
        );

        /*
        MainFunction @PSBoundParameters
        */
        PipelineAst invokeMain = new(
            s_blankExtent,
            new CommandBaseAst[]
            {
                new CommandAst(
                    s_blankExtent,
                    new CommandElementAst[]
                    {
                        new StringConstantExpressionAst(s_blankExtent, mainFunctionName, StringConstantType.BareWord),
                        new VariableExpressionAst(s_blankExtent, "PSBoundParameters", true)
                    },
                    TokenKind.Unknown,
                    null
                )
            },
            false
        );

        // We need to re-parse the param block so the stub scriptblock
        // signature matches the hooks version.
        ScriptBlockAst paramAst = Parser.ParseInput(paramBlock, out var _1, out var _2);

        ScriptBlockAst sbkAst = new(
            s_blankExtent,
            null, // using statements
            (ParamBlockAst?)paramAst.ParamBlock?.Copy(),
            new StatementBlockAst(
                s_blankExtent,
                new StatementAst[]
                {
                    funcDef,
                    invokeMain
                },
                null
            ),
            false
        );
        return sbkAst;
    }
}
