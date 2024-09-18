using Microsoft.CodeAnalysis;
using ProtoBuf;

namespace DotNetInternals;

public interface ICompiler
{
    Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs);
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
)
{
    public static DiagnosticData From(Diagnostic d)
    {
        string? filePath = d.Location.SourceTree?.FilePath;
        FileLinePositionSpan lineSpan;

        if (string.IsNullOrEmpty(filePath) &&
            d.Location.GetMappedLineSpan() is { IsValid: true, HasMappedPath: true } mappedLineSpan)
        {
            filePath = mappedLineSpan.Path;
            lineSpan = mappedLineSpan;
        }
        else
        {
            lineSpan = d.Location.GetLineSpan();
        }

        return new DiagnosticData(
            FilePath: filePath,
            Severity: d.Severity switch
            {
                DiagnosticSeverity.Error => DiagnosticDataSeverity.Error,
                DiagnosticSeverity.Warning => DiagnosticDataSeverity.Warning,
                _ => DiagnosticDataSeverity.Info,
            },
            Id: d.Id,
            HelpLinkUri: d.Descriptor.HelpLinkUri,
            Message: d.GetMessage(),
            StartLineNumber: lineSpan.StartLinePosition.Line + 1,
            StartColumn: lineSpan.StartLinePosition.Character + 1,
            EndLineNumber: lineSpan.EndLinePosition.Line + 1,
            EndColumn: lineSpan.EndLinePosition.Character + 1
        );
    }
}

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
            GlobalOutputs: [new() { Type = DiagnosticsOutputType, Text = output }],
            NumErrors: 1,
            NumWarnings: 0);
    }

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
    public required string Type { get; init; }
    public int Priority { get; init; }
    public string? Text { get; set; }
}
