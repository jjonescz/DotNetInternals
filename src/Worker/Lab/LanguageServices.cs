using BlazorMonaco;
using BlazorMonaco.Editor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace DotNetInternals.Lab;

internal sealed class LanguageServices
{
    private SourceText text = SourceText.From(string.Empty);
    private Document document = CreateDocument();

    public async Task<MonacoCompletionList> ProvideCompletionItemsAsync(string modelUri, Position position, MonacoCompletionContext context)
    {
        var t = text;
        int caretPosition = t.Lines.GetPosition(position.ToLinePosition());
        var completions = await CompletionService.GetService(document)!.GetCompletionsAsync(document, caretPosition);
        return completions.ToCompletionList(t.Lines, limit: 10);
    }

    public Task<ImmutableArray<MarkerData>> OnDidChangeModel(string code)
    {
        text = SourceText.From(code);
        return OnTextUpdatedAsync();
    }

    public Task<ImmutableArray<MarkerData>> OnDidChangeModelContentAsync(ModelContentChangedEvent args)
    {
        text = text.WithChanges(args.Changes.Select(change => new TextChange(new TextSpan(change.RangeOffset, change.RangeLength), change.Text)));
        return OnTextUpdatedAsync();
    }

    private async Task<ImmutableArray<MarkerData>> OnTextUpdatedAsync()
    {
        document = document.WithText(text);
        document.Project.Solution.Workspace.TryApplyChanges(document.Project.Solution);

        var comp = await document.Project.GetCompilationAsync();
        if (comp == null)
        {
            return [];
        }

        var diagnostics = comp.GetDiagnostics().Where(d => d.Severity > DiagnosticSeverity.Hidden);
        return diagnostics.Select(d => d.ToMarkerData()).ToImmutableArray();
    }

    private static Document CreateDocument()
    {
        var workspace = new AdhocWorkspace();
        var project = workspace
            .AddProject("TestProject", LanguageNames.CSharp)
            .AddMetadataReferences(Basic.Reference.Assemblies.AspNet90.References.All);
        var document = project.AddDocument("TestDocument.cs", string.Empty);
        workspace.TryApplyChanges(document.Project.Solution);
        return document;
    }
}
