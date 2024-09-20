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
            TriggerCharacters = [" ", "(", "=", "#", ".", "<", "[", "{", "\"", "/", ":", ">", "~"],
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
            worker.OnDidChangeModel(code);
            OnTextUpdated();
        }
    }

    public void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        worker.OnDidChangeModelContent(args);
        OnTextUpdated();
    }

    private async void OnTextUpdated()
    {
        if (currentModelUrl == null)
        {
            return;
        }

        var markers = await worker.GetDiagnosticsAsync();
        var model = await BlazorMonaco.Editor.Global.GetModel(jsRuntime, currentModelUrl);
        await BlazorMonaco.Editor.Global.SetModelMarkers(jsRuntime, model, "LanguageServices", markers.ToList());
    }
}
