namespace DotNetInternals;

public static class Util
{
    public static void CaptureConsoleOutput(Action action, out string stdout, out string stderr)
    {
        using var stdoutWriter = new StringWriter();
        using var stderrWriter = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(stdoutWriter);
        Console.SetError(stderrWriter);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        stdout = stdoutWriter.ToString();
        stderr = stderrWriter.ToString();
    }

    public static IEnumerable<T> TryConcat<T>(this IEnumerable<T>? a, IEnumerable<T>? b)
    {
        return [.. (a ?? []), .. (b ?? [])];
    }
}
