using Microsoft.AspNetCore.Razor.Language;

namespace RazorInternals;

public static class RazorCompiler
{
    public static string Compile(string input)
    {
        var fileSystem = new VirtualRazorProjectFileSystem();
        var config = RazorConfiguration.Default;
        RazorProjectEngine.Create(config, fileSystem);

        return fileSystem.GetType().ToString();;
    }
}
