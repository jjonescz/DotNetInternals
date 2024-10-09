using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetInternals;

public static class CodeAnalysisUtil
{
    public static DiagnosticData ToDiagnosticData(this Diagnostic d)
    {
        string? filePath = d.Location.SourceTree?.FilePath;
        FileLinePositionSpan lineSpan;

        if (string.IsNullOrEmpty(filePath) &&
            d.Location.GetMappedLineSpan() is { IsValid: true } mappedLineSpan)
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

internal static class RazorUtil
{
    public static IReadOnlyList<RazorDiagnostic> GetDiagnostics(this RazorCSharpDocument document)
    {
        // Different razor versions return IReadOnlyList vs ImmutableArray,
        // so we need to use reflection to avoid MissingMethodException.
        return (IReadOnlyList<RazorDiagnostic>)document.GetType()
            .GetProperty(nameof(document.Diagnostics))!
            .GetValue(document)!;
    }

    public static Diagnostic ToDiagnostic(this RazorDiagnostic d)
    {
        DiagnosticSeverity severity = d.Severity.ToDiagnosticSeverity();

        return Diagnostic.Create(
            id: d.Id,
            category: "Razor",
            message: d.GetMessage(),
            severity: severity,
            defaultSeverity: severity,
            isEnabledByDefault: true,
            warningLevel: 0,
            location: d.Span.ToLocation());
    }

    public static DiagnosticSeverity ToDiagnosticSeverity(this RazorDiagnosticSeverity severity)
    {
        return severity switch
        {
            RazorDiagnosticSeverity.Error => DiagnosticSeverity.Error,
            RazorDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Info,
        };
    }

    public static Location ToLocation(this SourceSpan span)
    {
        if (span == SourceSpan.Undefined)
        {
            return Location.None;
        }

        return Location.Create(
            filePath: span.FilePath,
            textSpan: span.ToTextSpan(),
            lineSpan: span.ToLinePositionSpan());
    }

    public static LinePositionSpan ToLinePositionSpan(this SourceSpan span)
    {
        var lineCount = span.LineCount < 1 ? 1 : span.LineCount;
        return new LinePositionSpan(
            start: new LinePosition(
                line: span.LineIndex,
                character: span.CharacterIndex),
            end: new LinePosition(
                line: span.LineIndex + lineCount - 1,
                character: span.CharacterIndex + span.Length));
    }

    public static TextSpan ToTextSpan(this SourceSpan span)
    {
        return new TextSpan(span.AbsoluteIndex, span.Length);
    }
}
