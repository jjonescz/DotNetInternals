using BlazorMonaco;
using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using KristofferStrube.Blazor.DOM;
using KristofferStrube.Blazor.WebWorkers;
using KristofferStrube.Blazor.Window;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using System.Text.Json;

namespace DotNetInternals.Lab;

internal sealed class WorkerController
{
    private readonly IJSRuntime jsRuntime;
    private readonly IWebAssemblyHostEnvironment hostEnvironment;
    private readonly Lazy<Task<SlimWorker>> worker;
    private readonly Queue<string> workerMessages = new();
    private TaskCompletionSource workerMessageArrived = new();

    public WorkerController(IJSRuntime jsRuntime, IWebAssemblyHostEnvironment hostEnvironment)
    {
        this.jsRuntime = jsRuntime;
        this.hostEnvironment = hostEnvironment;
        worker = new(CreateWorker);
    }

    private Task<SlimWorker> Worker => worker.Value;

    private async Task<SlimWorker> CreateWorker()
    {
        var workerReady = new TaskCompletionSource();
        var worker = await SlimWorker.CreateAsync(
            jsRuntime,
            assembly: "DotNetInternals.Worker",
            args: [hostEnvironment.BaseAddress]);
        var listener = await EventListener<MessageEvent>.CreateAsync(jsRuntime, async e =>
        {
            var data = await e.Data.GetValueAsync() as string;
            if (data == "ready")
            {
                workerReady.SetResult();
            }
            else
            {
                workerMessages.Enqueue(data ?? string.Empty);
                workerMessageArrived.TrySetResult();
            }
        });
        await worker.AddOnMessageEventListenerAsync(listener);
        await workerReady.Task;
        return worker;
    }

    private async Task<string> ReceiveWorkerMessageAsync()
    {
        if (workerMessages.TryDequeue(out var result))
        {
            return result;
        }

        await workerMessageArrived.Task;
        if (workerMessages.Count == 0)
        {
            workerMessageArrived = new();
            await workerMessageArrived.Task;
        }

        return workerMessages.Dequeue();
    }

    private async Task PostMessageUnsafeAsync(WorkerInputMessage message)
    {
        SlimWorker worker = await Worker;
        // TODO: Use ProtoBuf.
        var serialized = JsonSerializer.Serialize(message);
        await worker.PostMessageAsync(serialized);
    }

    private Task PostMessageAsync<T>(T message)
        where T : WorkerInputMessage<NoOutput>
    {
        return PostMessageUnsafeAsync(message);
    }

    private async Task<TIn> PostAndReceiveMessageAsync<TOut, TIn>(
        TOut message,
        Func<string, TIn>? fallback = null,
        TIn? deserializeAs = default)
        where TOut : WorkerInputMessage<TIn>
    {
        await PostMessageUnsafeAsync(message);
        var incoming = await ReceiveWorkerMessageAsync();
        fallback ??= static incoming => throw new InvalidOperationException("Failed to deserialize: " + incoming);
        return JsonSerializer.Deserialize<TIn>(incoming)
            ?? fallback(incoming);
    }

    public async Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.Compile(inputs),
            static incoming => CompiledAssembly.Fail("Failed to deserialize: " + incoming));
    }

    /// <summary>
    /// Instructs the <see cref="DependencyRegistry"/> to use this package.
    /// </summary>
    public async void UsePackage(string? version, string key, string packageId, string packageFolder)
    {
        await PostMessageAsync(new WorkerInputMessage.UsePackage(
            Version: version,
            Key: key,
            PackageId: packageId,
            PackageFolder: packageFolder));
    }

    public async Task<NuGetPackageInfo?> GetPackageInfoAsync(string key)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetPackageInfo(key),
            deserializeAs: default(NuGetPackageInfo));
    }

    public async Task<SdkInfo> GetSdkInfoAsync(string versionToLoad)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetSdkInfo(versionToLoad),
            deserializeAs: default(SdkInfo));
    }

    public async Task<CompletionList> ProvideCompletionItemsAsync(string modelUri, Position position, CompletionContext context)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.ProvideCompletionItems(modelUri, position, context),
            deserializeAs: default(CompletionList));
    }

    public async Task<ImmutableArray<MarkerData>> OnDidChangeModelAsync(string code)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.OnDidChangeModel(code),
            deserializeAs: default(ImmutableArray<MarkerData>));
    }

    public async Task<ImmutableArray<MarkerData>> OnDidChangeModelContentAsync(ModelContentChangedEvent args)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.OnDidChangeModelContent(args),
            deserializeAs: default(ImmutableArray<MarkerData>));
    }
}
