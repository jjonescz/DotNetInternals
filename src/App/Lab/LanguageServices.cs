using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using Microsoft.JSInterop;

namespace DotNetInternals.Lab;

internal sealed class LanguageServices(IJSRuntime jsRuntime, WorkerController worker)
{
    private string? currentModelUrl;
    private CancellationTokenSource completionCts = new();
    private CancellationTokenSource diagnosticsCts = new();

    private static Task<TOut> DebounceAsync<TIn, TOut>(ref CancellationTokenSource cts, TIn args, TOut fallback, Func<TIn, Task<TOut>> handler)
    {
        cts.Cancel();
        cts = new();

        return debounceAsync(cts.Token, args, fallback, handler);

        static async Task<TOut> debounceAsync(CancellationToken token, TIn args, TOut fallback, Func<TIn, Task<TOut>> handler)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), token);

                token.ThrowIfCancellationRequested();

                return await handler(args);
            }
            catch (OperationCanceledException)
            {
                return fallback;
            }
        }
    }

    private static void Debounce<T>(ref CancellationTokenSource cts, T args, Func<T, Task> handler)
    {
        DebounceAsync(ref cts, args, 0, async args =>
        {
            await handler(args);
            return 0;
        });
    }

    public async Task RegisterAsync()
    {
        var cSharpLanguageSelector = new LanguageSelector("csharp");
        await CompletionItemProviderAsync.Register(jsRuntime, cSharpLanguageSelector, new()
        {
            TriggerCharacters = [" ", "(", "=", "#", ".", "<", "[", "{", "\"", "/", ":", ">", "~"],
            ProvideCompletionItemsFunc = (modelUri, position, context) =>
            {
                return DebounceAsync(
                    ref completionCts,
                    (modelUri, position, context),
                    new() { Suggestions = [], Incomplete = true },
                    args => worker.ProvideCompletionItemsAsync(args.modelUri, args.position, args.context));
            },
            ResolveCompletionItemFunc = (completionItem) => Task.FromResult(completionItem),
        });
    }

    public void OnDidChangeWorkspace(ImmutableArray<ModelInfo> models)
    {
        worker.OnDidChangeWorkspace(models);
        UpdateDiagnostics();
    }

    public void OnDidChangeModel(ModelChangedEvent args)
    {
        currentModelUrl = args.NewModelUrl;
        worker.OnDidChangeModel(modelUri: currentModelUrl);
        UpdateDiagnostics();
    }

    public void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        worker.OnDidChangeModelContent(args);
        UpdateDiagnostics();
    }

    private void UpdateDiagnostics()
    {
        if (currentModelUrl == null)
        {
            return;
        }

        Debounce(ref diagnosticsCts, (worker, jsRuntime, currentModelUrl), static async args =>
        {
            var (worker, jsRuntime, currentModelUrl) = args;
            var markers = await worker.GetDiagnosticsAsync();
            var model = await BlazorMonaco.Editor.Global.GetModel(jsRuntime, currentModelUrl);
            await BlazorMonaco.Editor.Global.SetModelMarkers(jsRuntime, model, "LanguageServices", markers.ToList());
        });
    }
}
