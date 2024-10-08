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
    string HelpLinkUri,
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

    public static CompiledAssembly Fail(string output)
    {
        return new(
            BaseDirectory: "/",
            Files: ImmutableDictionary<string, CompiledFile>.Empty,
            Diagnostics: [],
            GlobalOutputs: [new() { Type = DiagnosticsOutputType, EagerText = output }],
            NumErrors: 1,
            NumWarnings: 0);
    }

    public CompiledFileOutput? GetGlobalOutput(string type)
    {
        return GlobalOutputs.FirstOrDefault(o => o.Type == type);
    }

    public CompiledFileOutput GetRequiredGlobalOutput(string type)
    {
        return GetGlobalOutput(type)
            ?? throw new InvalidOperationException($"Global output of type '{type}' not found.");
    }
}

public sealed record CompiledFile(ImmutableArray<CompiledFileOutput> Outputs)
{
    public CompiledFileOutput? GetOutput(string type)
    {
        return Outputs.FirstOrDefault(o => o.Type == type);
    }

    public CompiledFileOutput GetRequiredOutput(string type)
    {
        return GetOutput(type)
            ?? throw new InvalidOperationException($"Output of type '{type}' not found.");
    }
}

public sealed class CompiledFileOutput
{
    private object? text;

    public required string Type { get; init; }
    public int Priority { get; init; }
    public string? DesignTimeText { get; init; }

    public string? EagerText
    {
        get
        {
            if (text is string eagerText)
            {
                return eagerText;
            }

            if (text is ValueTask<string> { IsCompletedSuccessfully: true, Result: var taskResult })
            {
                text = taskResult;
                return taskResult;
            }

            return null;
        }
        init
        {
            text = value;
        }
    }

    public Func<ValueTask<string>> LazyText
    {
        init
        {
            text = value;
        }
    }

    public ValueTask<string> GetTextAsync(Func<ValueTask<string>>? outputFactory)
    {
        if (EagerText is { } eagerText)
        {
            return new(eagerText);
        }

        if (text is null)
        {
            if (outputFactory is null)
            {
                throw new InvalidOperationException($"For lazy outputs, {nameof(outputFactory)} must be provided.");
            }

            var output = outputFactory();
            text = output;
            return output;
        }

        if (text is ValueTask<string> valueTask)
        {
            return valueTask;
        }

        if (text is Func<ValueTask<string>> factory)
        {
            var result = factory();
            text = result;
            return result;
        }

        throw new InvalidOperationException($"Unrecognized {nameof(text)}: {text?.GetType().FullName ?? "null"}");
    }
}
