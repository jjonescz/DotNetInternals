using ProtoBuf;

namespace DotNetInternals;

public interface ICompiler
{
    CompiledAssembly Compile(IEnumerable<InputCode> inputs);
}

[ProtoContract]
public sealed record InputCode
{
    [ProtoMember(1)]
    public required string FileName { get; init; }
    [ProtoMember(2)]
    public required string Text { get; init; }

    public string FileExtension => Path.GetExtension(FileName);
}

public enum DiagnosticDataSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record DiagnosticData(
    string? FilePath,
    DiagnosticDataSeverity Severity,
    string Id,
    string? HelpLinkUri,
    string Message,
    int StartLineNumber,
    int StartColumn,
    int EndLineNumber,
    int EndColumn
);

public sealed record CompiledAssembly(
    ImmutableDictionary<string, CompiledFile> Files,
    ImmutableArray<CompiledFileOutput> GlobalOutputs,
    int NumWarnings,
    int NumErrors,
    ImmutableArray<DiagnosticData> Diagnostics,
    string BaseDirectory)
{
    public static readonly string DiagnosticsOutputType = "Error List";

    public CompiledFileOutput? GetGlobalOutput(string type)
    {
        return GlobalOutputs.FirstOrDefault(o => o.Type == type);
    }
}

public sealed record CompiledFile(ImmutableArray<CompiledFileOutput> Outputs)
{
    public CompiledFileOutput? GetOutput(string type)
    {
        return Outputs.FirstOrDefault(o => o.Type == type);
    }
}

public sealed class CompiledFileOutput
{
    private object text;

    public CompiledFileOutput(string type, string eagerText, string? designTimeText = null)
    {
        Type = type;
        text = eagerText;
        DesignTimeText = designTimeText;
    }

    public CompiledFileOutput(string type, Func<ValueTask<string>> lazyText)
    {
        Type = type;
        text = lazyText;
    }

    public CompiledFileOutput(string type, Func<string> lazyTextSync)
    {
        Type = type;
        text = lazyTextSync;
    }

    public string Type { get; }
    public int Priority { get; init; }

    public string? DesignTimeText { get; }

    public bool IsLazy => !TryGetEagerText(out _);

    public string GetEagerTextOrThrow()
    {
        return TryGetEagerText(out var eagerText)
            ? eagerText
            : throw new InvalidOperationException("The text is not available eagerly.");
    }

    public bool TryGetEagerText([NotNullWhen(returnValue: true)] out string? result)
    {
        if (text is string eagerText)
        {
            result = eagerText;
            return true;
        }

        if (text is ValueTask<string> { IsCompletedSuccessfully: true, Result: var taskResult })
        {
            text = taskResult;
            result = taskResult;
            return true;
        }

        result = null;
        return false;
    }

    public ValueTask<string> GetTextAsync()
    {
        if (TryGetEagerText(out var eagerText))
        {
            return new(eagerText);
        }

        if (text is ValueTask<string> existingTask)
        {
            return existingTask;
        }

        if (text is Func<ValueTask<string>> lazyText)
        {
            var task = lazyText();
            text = task;
            return task;
        }

        if (text is Func<string> lazyTextSync)
        {
            var result = lazyTextSync();
            text = result;
            return new(result);
        }

        throw new InvalidOperationException();
    }
}
