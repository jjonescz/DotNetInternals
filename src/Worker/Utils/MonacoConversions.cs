global using MonacoCompletionContext = BlazorMonaco.Languages.CompletionContext;
global using MonacoCompletionItem = BlazorMonaco.Languages.CompletionItem;
global using MonacoCompletionList = BlazorMonaco.Languages.CompletionList;
global using MonacoRange = BlazorMonaco.Range;
global using RoslynCompletionItem = Microsoft.CodeAnalysis.Completion.CompletionItem;
global using RoslynCompletionList = Microsoft.CodeAnalysis.Completion.CompletionList;

using BlazorMonaco;
using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Tags;
using Microsoft.CodeAnalysis.Text;

namespace DotNetInternals;

public static class MonacoConversions
{
    public static MonacoCompletionList ToCompletionList(this RoslynCompletionList completions, TextLineCollection lines)
    {
        return new MonacoCompletionList
        {
            Suggestions = completions.ItemsList.Select(c => c.ToCompletionItem(lines)).ToList(),
        };
    }

    public static MonacoCompletionItem ToCompletionItem(this RoslynCompletionItem completion, TextLineCollection lines)
    {
        return new MonacoCompletionItem
        {
            LabelAsString = completion.DisplayText,
            Kind = completion.Tags.Contains(WellKnownTags.Method) ? CompletionItemKind.Method : CompletionItemKind.Variable,
            RangeAsObject = lines.GetLinePositionSpan(completion.Span).ToRange(),
            InsertText = completion.DisplayText,
            FilterText = completion.FilterText,
            SortText = completion.SortText,
        };
    }

    public static LinePosition ToLinePosition(this Position position)
    {
        return new LinePosition(position.LineNumber - 1, position.Column - 1);
    }

    public static MarkerData ToMarkerData(this DiagnosticData d)
    {
        return new MarkerData
        {
            CodeAsObject = new()
            {
                Value = d.Id,
                TargetUri = d.HelpLinkUri,
            },
            Message = d.Message,
            StartLineNumber = d.StartLineNumber,
            StartColumn = d.StartColumn,
            EndLineNumber = d.EndLineNumber,
            EndColumn = d.EndColumn,
            Severity = d.Severity switch
            {
                DiagnosticDataSeverity.Error => MarkerSeverity.Error,
                DiagnosticDataSeverity.Warning => MarkerSeverity.Warning,
                _ => MarkerSeverity.Info,
            },
        };
    }

    public static MarkerData ToMarkerData(this Diagnostic d)
    {
        return ToMarkerData(d.ToDiagnosticData());
    }

    public static MonacoRange ToRange(this LinePositionSpan span)
    {
        return new MonacoRange
        {
            StartLineNumber = span.Start.Line + 1,
            StartColumn = span.Start.Character + 1,
            EndLineNumber = span.End.Line + 1,
            EndColumn = span.End.Character + 1,
        };
    }
}
