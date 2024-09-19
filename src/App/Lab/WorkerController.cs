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
    private readonly ILogger<WorkerController> logger;
    private readonly IJSRuntime jsRuntime;
    private readonly IWebAssemblyHostEnvironment hostEnvironment;
    private readonly Lazy<Task<SlimWorker>> worker;
    private readonly Dictionary<int, WorkerOutputMessage> workerMessages = new();
    private TaskCompletionSource workerMessageArrived = new();
    private int messageId;

    public WorkerController(ILogger<WorkerController> logger, IJSRuntime jsRuntime, IWebAssemblyHostEnvironment hostEnvironment)
    {
        this.logger = logger;
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
            var data = await e.Data.GetValueAsync() as string ?? string.Empty;
            var message = JsonSerializer.Deserialize<WorkerOutputMessage>(data)!;
            logger.LogDebug("📩 {Id}: {Type} ({Size})",
                message.Id,
                message.GetType().Name, 
                data.Length.SeparateThousands());
            if (message is WorkerOutputMessage.Ready)
            {
                workerReady.SetResult();
            }
            else if (message.Id < 0)
            {
                logger.LogError("Unpaired message {Message}", message);
            }
            else
            {
                workerMessages.Add(message.Id, message);
                workerMessageArrived.TrySetResult();
            }
        });
        await worker.AddOnMessageEventListenerAsync(listener);
        await workerReady.Task;
        return worker;
    }

    private async Task<WorkerOutputMessage> ReceiveWorkerMessageAsync(int id)
    {
        WorkerOutputMessage? result;
        while (!workerMessages.TryGetValue(id, out result))
        {
            await workerMessageArrived.Task;
            workerMessageArrived = new();
        }

        return result;
    }

    private async Task PostMessageUnsafeAsync(WorkerInputMessage message)
    {
        SlimWorker worker = await Worker;
        // TODO: Use ProtoBuf.
        var serialized = JsonSerializer.Serialize(message);
        logger.LogDebug("📨 {Id}: {Type} ({Size})",
            message.Id,
            message.GetType().Name,
            serialized.Length.SeparateThousands());
        await worker.PostMessageAsync(serialized);
    }

    private async Task PostMessageAsync<T>(T message)
        where T : WorkerInputMessage<NoOutput>
    {
        await PostMessageUnsafeAsync(message);
        var incoming = await ReceiveWorkerMessageAsync(message.Id);
        if (incoming is not WorkerOutputMessage.Empty)
        {
            throw new InvalidOperationException($"Unexpected non-empty message type: {incoming}");
        }
    }

    private async Task<TIn> PostAndReceiveMessageAsync<TOut, TIn>(
        TOut message,
        Func<string, TIn>? fallback = null,
        TIn? deserializeAs = default)
        where TOut : WorkerInputMessage<TIn>
    {
        await PostMessageUnsafeAsync(message);
        var incoming = await ReceiveWorkerMessageAsync(message.Id);
        return incoming switch
        {
            WorkerOutputMessage.Success success => ((JsonElement)success.Result!).Deserialize<TIn>()!,
            WorkerOutputMessage.Failure failure => fallback switch
            {
                null => throw new InvalidOperationException(failure.Message),
                _ => fallback(failure.Message),
            },
            _ => throw new InvalidOperationException($"Unexpected message type: {incoming}"),
        };
    }

    public async Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.Compile(inputs) { Id = messageId++ },
            CompiledAssembly.Fail);
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
            PackageFolder: packageFolder)
        {
            Id = messageId++,
        });
    }

    public async Task<NuGetPackageInfo?> GetPackageInfoAsync(string key)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetPackageInfo(key) { Id = messageId++ },
            deserializeAs: default(NuGetPackageInfo));
    }

    public async Task<SdkInfo> GetSdkInfoAsync(string versionToLoad)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetSdkInfo(versionToLoad) { Id = messageId++ },
            deserializeAs: default(SdkInfo));
    }

    public async Task<CompletionList> ProvideCompletionItemsAsync(string modelUri, Position position, CompletionContext context)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.ProvideCompletionItems(modelUri, position, context) { Id = messageId++ },
            deserializeAs: default(CompletionList));
    }

    public async Task<ImmutableArray<MarkerData>> OnDidChangeModelAsync(string code)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.OnDidChangeModel(code) { Id = messageId++ },
            deserializeAs: default(ImmutableArray<MarkerData>));
    }

    public async Task<ImmutableArray<MarkerData>> OnDidChangeModelContentAsync(ModelContentChangedEvent args)
    {
        return await PostAndReceiveMessageAsync(
            new WorkerInputMessage.OnDidChangeModelContent(args) { Id = messageId++ },
            deserializeAs: default(ImmutableArray<MarkerData>));
    }
}
