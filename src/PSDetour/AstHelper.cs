using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation.Language;

namespace PSDetour;

internal static class AstHelper
{
    private static ScriptExtent _blankExtent = new(new(null, 0, 0, null), new(null, 0, 0, null));

    public static HashtableAst CreateHashtableAst(IDictionary<string, object?> value)
    {
        return new(
            _blankExtent,
            value.Select(kvp => CreateKeyValuePairAst(kvp))
        );
    }

    public static Tuple<ExpressionAst, StatementAst> CreateKeyValuePairAst(KeyValuePair<string, object?> kvp)
    {
        return new(
            new StringConstantExpressionAst(_blankExtent, kvp.Key, StringConstantType.BareWord),
            CreateSingleExpressionPipeline(kvp.Value)
        );
    }

    public static PipelineAst CreateSingleExpressionPipeline(object? value)
    {
        ExpressionAst exp;
        if (value == null)
        {
            exp = new VariableExpressionAst(_blankExtent, "null", false);
        }
        else if (value is int)
        {
            exp = new ConstantExpressionAst(_blankExtent, value);
        }
        else if (value is IDictionary<string, object?> dict)
        {
            exp = CreateHashtableAst(dict);
        }
        else
        {
            exp = new StringConstantExpressionAst(
                _blankExtent,
                value.ToString() ?? "",
                StringConstantType.SingleQuoted);
        }

        return new(
            _blankExtent,
            new CommandBaseAst[]
            {
                new CommandExpressionAst(
                    _blankExtent,
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
            _blankExtent,
            new(
                _blankExtent,
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
            _blankExtent,
            new TypeExpressionAst(
                _blankExtent,
                new TypeName(_blankExtent, typeof(Parser).FullName)
            ),
            new StringConstantExpressionAst(_blankExtent, "ParseInput", StringConstantType.BareWord),
            new ExpressionAst[]
            {
                new ExpandableStringExpressionAst(
                    _blankExtent,
                    "$([string]::new(\"`n\", $_.Line))Function $($_.Name) {$($_.Value)}",
                    StringConstantType.DoubleQuoted
                ),
                new MemberExpressionAst(
                    _blankExtent,
                    new VariableExpressionAst(_blankExtent, "_", false),
                    new StringConstantExpressionAst(_blankExtent, "Path", StringConstantType.BareWord),
                    false
                ),
                new ConvertExpressionAst(
                    _blankExtent,
                    new TypeConstraintAst(
                        _blankExtent,
                        new TypeName(_blankExtent, "ref")
                    ),
                    new VariableExpressionAst(_blankExtent, "null", false)
                ),
                new ConvertExpressionAst(
                    _blankExtent,
                    new TypeConstraintAst(
                        _blankExtent,
                        new TypeName(_blankExtent, "ref")
                    ),
                    new VariableExpressionAst(_blankExtent, "null", false)
                )
            },
            true,
            false
        );
        InvokeMemberExpressionAst getScriptBlock = new(
            _blankExtent,
            new ParenExpressionAst(
                _blankExtent,
                new PipelineAst(
                    _blankExtent,
                    new CommandBaseAst[]
                    {
                        new CommandExpressionAst(
                            _blankExtent,
                            parseInput,
                            null
                        )
                    },
                    false
                )
            ),
            new StringConstantExpressionAst(_blankExtent, "GetScriptBlock", StringConstantType.BareWord),
            null,
            false,
            false
        );

        ScriptBlockAst foreachProcess = new(
            _blankExtent,
            null,
            null,
            null,
            new StatementBlockAst(
                _blankExtent,
                new StatementAst[]
                {
                    new PipelineAst(
                        _blankExtent,
                        new CommandAst(
                            _blankExtent,
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
            _blankExtent,
            new CommandBaseAst[]
            {
                new CommandExpressionAst(
                    _blankExtent,
                    functionsArray,
                    null
                ),
                new CommandAst(
                    _blankExtent,
                    new CommandElementAst[]
                    {
                        new StringConstantExpressionAst(_blankExtent, "ForEach-Object", StringConstantType.BareWord),
                        new ScriptBlockExpressionAst(_blankExtent, foreachProcess)
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
            _blankExtent,
            new CommandBaseAst[]
            {
                new CommandAst(
                    _blankExtent,
                    new CommandElementAst[]
                    {
                        new StringConstantExpressionAst(_blankExtent, mainFunctionName, StringConstantType.BareWord),
                        new VariableExpressionAst(_blankExtent, "PSBoundParameters", true)
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
            _blankExtent,
            null, // using statements
            (ParamBlockAst?)paramAst.ParamBlock?.Copy(),
            new StatementBlockAst(
                _blankExtent,
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
