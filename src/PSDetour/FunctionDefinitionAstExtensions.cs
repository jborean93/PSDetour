using System.Management.Automation;
using System.Management.Automation.Language;

namespace PSDetour;

public static class FunctionDefinitionAstExtension
{
    // GetScriptBlock isn't defined on this type like ScriptBlockAst.
    // This uses reflection to achieve the same thing that pwsh does internally.
    public static ScriptBlock GetScriptBlock(this FunctionDefinitionAst funcAst)
        => (ScriptBlock)ReflectionInfo.SbkAstCtor.Invoke(new object[] { funcAst, funcAst.IsFilter });
}
