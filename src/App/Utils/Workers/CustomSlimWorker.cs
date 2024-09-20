using KristofferStrube.Blazor.WebIDL;
using KristofferStrube.Blazor.WebWorkers;
using Microsoft.JSInterop;
using System.Text.Json;

namespace DotNetInternals;

/// <summary>
/// Similar to <see cref="SlimWorker"/> but with some customizations:
/// <list type="bullet">
/// <item>The script redirects <c>.wasm</c> requests to <c>.wasm.br</c> pre-compressed ones in production.</item>
/// </list>
/// </summary>
internal sealed class CustomSlimWorker : SlimWorker
{
    private CustomSlimWorker(
        IJSRuntime jSRuntime,
        IJSObjectReference jSReference,
        CreationOptions options)
        : base(jSRuntime, jSReference, options)
    {
    }

    public static async new Task<SlimWorker> CreateAsync(IJSRuntime jsRuntime, string assembly, string[]? args = null)
    {
        args ??= [];

        string scriptUrl = "js/worker.js"
            + $"?assembly={assembly}"
            + $"&serializedArgs={JsonSerializer.Serialize(args)}";

        await using IJSObjectReference helper = await jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "./_content/KristofferStrube.Blazor.WebWorkers/KristofferStrube.Blazor.WebWorkers.js");

        IJSObjectReference jsInstance = await helper.InvokeAsync<IJSObjectReference>(
            "constructWorker",
            scriptUrl,
            new WorkerOptions { Type = WorkerType.Module });

        return new CustomSlimWorker(jsRuntime, jsInstance, new() { DisposesJSReference = true });
    }
}
