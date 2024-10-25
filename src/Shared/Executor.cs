using System.Runtime.Loader;

namespace DotNetInternals;

public static class Executor
{
    public static string Execute(MemoryStream emitStream)
    {
        return Execute(emitStream,
            resultHandler: static (assembly) =>
            {
                var entryPoint = assembly.EntryPoint
                    ?? throw new ArgumentException("No entry point found in the assembly.");
                int exitCode = 0;
                Util.CaptureConsoleOutput(
                    () =>
                    {
                        exitCode = InvokeEntryPoint(entryPoint);
                    },
                    out string stdout, out string stderr);
                return $"Exit code: {exitCode}\nStdout:\n{stdout}\nStderr:\n{stderr}";
            },
            errorHandler: static (ex) => ex.ToString());
    }

    public static void Execute(
        MemoryStream emitStream,
        Action<Assembly> resultHandler)
    {
        Execute<int>(emitStream, assembly =>
        {
            resultHandler(assembly);
            return 0;
        });
    }

    public static T Execute<T>(
        MemoryStream emitStream,
        Func<Assembly, T> resultHandler,
        Func<Exception, T>? errorHandler = null)
    {
        var alc = new AssemblyLoadContext(nameof(Executor));
        try
        {
            var assembly = alc.LoadFromStream(emitStream);
            return resultHandler(assembly);
        }
        catch (Exception ex)
        {
            if (errorHandler is null)
            {
                throw;
            }

            return errorHandler(ex);
        }
    }

    public static int InvokeEntryPoint(MethodInfo entryPoint)
    {
        var parameters = entryPoint.GetParameters().Length == 0
            ? null
            : new object[] { Array.Empty<string>() };
        return entryPoint.Invoke(null, parameters) is int e ? e : 0;
    }
}
