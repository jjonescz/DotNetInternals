using Microsoft.CodeAnalysis.CSharp;

namespace DotNetInternals;

public static class RoslynAccessors
{
    public static string Dump(this CSharpSyntaxNode node)
    {
        return node.Dump();
    }
}
