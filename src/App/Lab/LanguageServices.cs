using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;
using Microsoft.JSInterop;

namespace DotNetInternals.Lab;

internal sealed class LanguageServices(IJSRuntime jsRuntime)
{
    private string? currentModelUrl;
    private Task changingModel = Task.CompletedTask;
    private SourceText text = SourceText.From(string.Empty);
    private Document document = CreateDocument();

    public async Task RegisterAsync()
    {
        var cSharpLanguageSelector = new LanguageSelector("csharp");
        await CompletionItemProviderAsync.Register(jsRuntime, cSharpLanguageSelector, new()
        {
            ProvideCompletionItemsFunc = async (modelUri, position, context) =>
            {
                await changingModel;
                var t = text;
                int caretPosition = t.Lines.GetPosition(position.ToLinePosition());
                var completions = await CompletionService.GetService(document)!.GetCompletionsAsync(document, caretPosition);
                return completions.ToCompletionList(t.Lines);
            },
            ResolveCompletionItemFunc = (completionItem) => Task.FromResult(completionItem),
        });
    }

    public void OnDidChangeModel(ModelChangedEvent args)
    {
        currentModelUrl = args.NewModelUrl;
        changingModel = handle();

        async Task handle()
        {
            var model = await BlazorMonaco.Editor.Global.GetModel(jsRuntime, args.NewModelUrl);
            var str = await model.GetValue(EndOfLinePreference.TextDefined, preserveBOM: true);
            text = SourceText.From(str);
            OnTextUpdated();
        }
    }

    public void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        text = text.WithChanges(args.Changes.Select(change => new TextChange(new TextSpan(change.RangeOffset, change.RangeLength), change.Text)));
        OnTextUpdated();
    }

    private async void OnTextUpdated()
    {
        document = document.WithText(text);
        document.Project.Solution.Workspace.TryApplyChanges(document.Project.Solution);

        if (currentModelUrl == null)
        {
            return;
        }

        var comp = await document.Project.GetCompilationAsync();
        if (comp == null)
        {
            return;
        }

        var diagnostics = comp.GetDiagnostics().Where(d => d.Severity > DiagnosticSeverity.Hidden);
        var model = await BlazorMonaco.Editor.Global.GetModel(jsRuntime, currentModelUrl);
        var markers = diagnostics.Select(d => d.ToMarkerData()).ToList();
        await BlazorMonaco.Editor.Global.SetModelMarkers(jsRuntime, model, "LanguageServices", markers);
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
