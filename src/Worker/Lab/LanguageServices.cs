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
        var service = CompletionService.GetService(document)!;
        var completions = await service.GetCompletionsAsync(document, caretPosition);
        return completions.ToCompletionList(t.Lines);
    }

    public void OnDidChangeModel(string code)
    {
        text = SourceText.From(code);
        OnTextUpdated();
    }

    public void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        text = text.WithChanges(args.Changes.ToTextChanges());
        OnTextUpdated();
    }

    private void OnTextUpdated()
    {
        document = document.WithText(text);
        document.Project.Solution.Workspace.TryApplyChanges(document.Project.Solution);
    }

    public async Task<ImmutableArray<MarkerData>> GetDiagnosticsAsync()
    {
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
