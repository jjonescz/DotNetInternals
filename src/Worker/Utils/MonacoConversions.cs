﻿global using MonacoCompletionContext = BlazorMonaco.Languages.CompletionContext;
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

namespace DotNetLab;

public static class MonacoConversions
{
    public static TextSpan GetTextSpan(this ModelContentChange change)
    {
        return new TextSpan(change.RangeOffset, change.RangeLength);
    }

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
            Kind = getKind(completion.Tags),
            RangeAsObject = lines.GetLinePositionSpan(completion.Span).ToRange(),
            InsertText = completion.TryGetInsertionText(out var insertionText) ? insertionText : completion.DisplayText,
            FilterText = completion.FilterText,
            SortText = completion.SortText,
        };

        static CompletionItemKind getKind(ImmutableArray<string> tags)
        {
            foreach (var tag in tags)
            {
                switch (tag)
                {
                    case WellKnownTags.Public: return CompletionItemKind.Keyword;
                    case WellKnownTags.Protected: return CompletionItemKind.Keyword;
                    case WellKnownTags.Private: return CompletionItemKind.Keyword;
                    case WellKnownTags.Internal: return CompletionItemKind.Keyword;
                    case WellKnownTags.File: return CompletionItemKind.File;
                    case WellKnownTags.Project: return CompletionItemKind.File;
                    case WellKnownTags.Folder: return CompletionItemKind.Folder;
                    case WellKnownTags.Assembly: return CompletionItemKind.File;
                    case WellKnownTags.Class: return CompletionItemKind.Class;
                    case WellKnownTags.Constant: return CompletionItemKind.Constant;
                    case WellKnownTags.Delegate: return CompletionItemKind.Function;
                    case WellKnownTags.Enum: return CompletionItemKind.Enum;
                    case WellKnownTags.EnumMember: return CompletionItemKind.EnumMember;
                    case WellKnownTags.Event: return CompletionItemKind.Event;
                    case WellKnownTags.ExtensionMethod: return CompletionItemKind.Method;
                    case WellKnownTags.Field: return CompletionItemKind.Field;
                    case WellKnownTags.Interface: return CompletionItemKind.Interface;
                    case WellKnownTags.Intrinsic: return CompletionItemKind.Text;
                    case WellKnownTags.Keyword: return CompletionItemKind.Keyword;
                    case WellKnownTags.Label: return CompletionItemKind.Text;
                    case WellKnownTags.Local: return CompletionItemKind.Variable;
                    case WellKnownTags.Namespace: return CompletionItemKind.Module;
                    case WellKnownTags.Method: return CompletionItemKind.Method;
                    case WellKnownTags.Module: return CompletionItemKind.Module;
                    case WellKnownTags.Operator: return CompletionItemKind.Operator;
                    case WellKnownTags.Parameter: return CompletionItemKind.Value;
                    case WellKnownTags.Property: return CompletionItemKind.Property;
                    case WellKnownTags.RangeVariable: return CompletionItemKind.Variable;
                    case WellKnownTags.Reference: return CompletionItemKind.Reference;
                    case WellKnownTags.Structure: return CompletionItemKind.Struct;
                    case WellKnownTags.TypeParameter: return CompletionItemKind.TypeParameter;
                    case WellKnownTags.Snippet: return CompletionItemKind.Snippet;
                    case WellKnownTags.Error: return CompletionItemKind.Text;
                    case WellKnownTags.Warning: return CompletionItemKind.Text;
                }
            }

            return CompletionItemKind.Text;
        }
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

    public static IEnumerable<TextChange> ToTextChanges(this IEnumerable<ModelContentChange> changes)
    {
        return changes.Select(change => change.ToTextChange());
    }

    public static TextChange ToTextChange(this ModelContentChange change)
    {
        return new TextChange(change.GetTextSpan(), change.Text);
    }

    public static bool TryGetInsertionText(
        this RoslynCompletionItem completionItem,
        [NotNullWhen(returnValue: true)] out string? insertionText)
    {
        return completionItem.Properties.TryGetValue("InsertionText", out insertionText);
    }
}
