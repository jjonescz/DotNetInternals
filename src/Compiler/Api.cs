using Microsoft.CodeAnalysis.CSharp;

namespace DotNetInternals;

public static class Config
{
    internal static CSharpParseOptions CurrentCSharpParseOptions { get; set; } = Microsoft.CodeAnalysis.CSharp.CSharpParseOptions.Default;

    public static void CSharpParseOptions(Func<CSharpParseOptions, CSharpParseOptions> configure)
    {
        CurrentCSharpParseOptions = configure(CurrentCSharpParseOptions);
    }
}
