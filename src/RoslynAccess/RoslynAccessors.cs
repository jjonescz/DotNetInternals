using Microsoft.CodeAnalysis.CSharp;

namespace DotNetInternals.RoslynAccess;

public static class RoslynAccessors
{
    public static string Dump(this CSharpSyntaxNode node)
    {
        return node.Dump();
    }
}
