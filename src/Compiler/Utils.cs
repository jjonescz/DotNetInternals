using Microsoft.CodeAnalysis;

namespace DotNetInternals;

public static class CodeAnalysisUtil
{
    public static DiagnosticData ToDiagnosticData(this Diagnostic d)
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
