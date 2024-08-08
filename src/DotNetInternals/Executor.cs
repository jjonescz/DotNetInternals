using System.Runtime.Loader;

namespace DotNetInternals;

public static class Executor
{
    public static string Execute(MemoryStream emitStream)
    {
        var alc = new AssemblyLoadContext(nameof(Executor));
        try
        {
            var assembly = alc.LoadFromStream(emitStream);
            var entryPoint = assembly.EntryPoint ??
                throw new ArgumentException("No entry point found in the assembly.");
            int exitCode = 0;
            Util.CaptureConsoleOutput(
                () =>
                {
                    var parameters = entryPoint.GetParameters().Length == 0
                        ? null
                        : new object[] { Array.Empty<string>() };
                    exitCode = entryPoint.Invoke(null, parameters) is int e ? e : 0;
                },
                out string stdout, out string stderr);
            return $"Exit code: {exitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}";
        }
        catch (Exception ex)
        {
            return ex.ToString();
        }
    }
}
