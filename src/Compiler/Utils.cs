using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotNetInternals;

internal static class RazorUtil
{
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
        return new LinePositionSpan(
            start: new LinePosition(
                line: span.LineIndex,
                character: span.CharacterIndex),
            end: new LinePosition(
                line: span.LineIndex + span.LineCount - 1,
                character: span.CharacterIndex + span.Length));
    }

    public static TextSpan ToTextSpan(this SourceSpan span)
    {
        return new TextSpan(span.AbsoluteIndex, span.Length);
    }
}
