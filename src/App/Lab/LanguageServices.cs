using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using Microsoft.JSInterop;

namespace DotNetInternals.Lab;

internal sealed class LanguageServices(IJSRuntime jsRuntime, WorkerController worker)
{
    private string? currentModelUrl;
    private Task changingModel = Task.CompletedTask;

    public async Task RegisterAsync()
    {
        var cSharpLanguageSelector = new LanguageSelector("csharp");
        await CompletionItemProviderAsync.Register(jsRuntime, cSharpLanguageSelector, new()
        {
            ProvideCompletionItemsFunc = async (modelUri, position, context) =>
            {
                await changingModel;
                return await worker.ProvideCompletionItemsAsync(modelUri, position, context);
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
            var code = await model.GetValue(EndOfLinePreference.TextDefined, preserveBOM: true);
            OnTextUpdated(await worker.OnDidChangeModelAsync(code));
        }
    }

    public async void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        OnTextUpdated(await worker.OnDidChangeModelContentAsync(args));
    }

    private async void OnTextUpdated(ImmutableArray<MarkerData> markers)
    {
        if (currentModelUrl == null)
        {
            return;
        }

        var model = await BlazorMonaco.Editor.Global.GetModel(jsRuntime, currentModelUrl);
        await BlazorMonaco.Editor.Global.SetModelMarkers(jsRuntime, model, "LanguageServices", markers.ToList());
    }
}
