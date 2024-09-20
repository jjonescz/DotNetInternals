using BlazorMonaco;
using BlazorMonaco.Editor;
using BlazorMonaco.Languages;
using KristofferStrube.Blazor.DOM;
using KristofferStrube.Blazor.WebWorkers;
using KristofferStrube.Blazor.Window;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Threading.Channels;

namespace DotNetInternals.Lab;

internal sealed class WorkerController
{
    private readonly ILogger<WorkerController> logger;
    private readonly IJSRuntime jsRuntime;
    private readonly IWebAssemblyHostEnvironment hostEnvironment;
    private readonly Lazy<Task<SlimWorker>> worker;
    private readonly Channel<WorkerOutputMessage> workerMessages = Channel.CreateUnbounded<WorkerOutputMessage>();
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
                await workerMessages.Writer.WriteAsync(message);
            }
        });
        await worker.AddOnMessageEventListenerAsync(listener);
        await workerReady.Task;
        return worker;
    }

    private async Task<WorkerOutputMessage> ReceiveWorkerMessageAsync(int id)
    {
        while (!workerMessages.Reader.TryPeek(out var result) || result.Id != id)
        {
            await Task.Yield();
            await workerMessages.Reader.WaitToReadAsync();
        }

        var again = await workerMessages.Reader.ReadAsync();
        Debug.Assert(again.Id == id);

        return again;
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

    private async void PostMessage<T>(T message)
        where T : WorkerInputMessage<NoOutput>
    {
        await PostMessageUnsafeAsync(message);
        var incoming = await ReceiveWorkerMessageAsync(message.Id);
        switch (incoming)
        {
            case WorkerOutputMessage.Empty:
                break;
            case WorkerOutputMessage.Failure failure:
                throw new InvalidOperationException(failure.Message);
            default:
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

    public Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
    {
        return PostAndReceiveMessageAsync(
            new WorkerInputMessage.Compile(inputs) { Id = messageId++ },
            CompiledAssembly.Fail);
    }

    /// <summary>
    /// Instructs the <see cref="DependencyRegistry"/> to use this package.
    /// </summary>
    public void UsePackage(string? version, string key, string packageId, string packageFolder)
    {
        PostMessage(new WorkerInputMessage.UsePackage(
            Version: version,
            Key: key,
            PackageId: packageId,
            PackageFolder: packageFolder)
        {
            Id = messageId++,
        });
    }

    public Task<NuGetPackageInfo?> GetPackageInfoAsync(string key)
    {
        return PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetPackageInfo(key) { Id = messageId++ },
            deserializeAs: default(NuGetPackageInfo));
    }

    public Task<SdkInfo> GetSdkInfoAsync(string versionToLoad)
    {
        return PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetSdkInfo(versionToLoad) { Id = messageId++ },
            deserializeAs: default(SdkInfo));
    }

    public Task<CompletionList> ProvideCompletionItemsAsync(string modelUri, Position position, CompletionContext context)
    {
        return PostAndReceiveMessageAsync(
            new WorkerInputMessage.ProvideCompletionItems(modelUri, position, context) { Id = messageId++ },
            deserializeAs: default(CompletionList));
    }

    public void OnDidChangeModel(string code)
    {
        PostMessage(
            new WorkerInputMessage.OnDidChangeModel(code) { Id = messageId++ });
    }

    public void OnDidChangeModelContent(ModelContentChangedEvent args)
    {
        PostMessage(
            new WorkerInputMessage.OnDidChangeModelContent(args) { Id = messageId++ });
    }

    public Task<ImmutableArray<MarkerData>> GetDiagnosticsAsync()
    {
        return PostAndReceiveMessageAsync(
            new WorkerInputMessage.GetDiagnostics() { Id = messageId++ },
            deserializeAs: default(ImmutableArray<MarkerData>));
    }
}
