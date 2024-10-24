﻿using BlazorMonaco;
using BlazorMonaco.Editor;
using DotNetInternals.Lab;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;

namespace DotNetInternals;

[JsonDerivedType(typeof(Compile), nameof(Compile))]
[JsonDerivedType(typeof(GetOutput), nameof(GetOutput))]
[JsonDerivedType(typeof(UsePackage), nameof(UsePackage))]
[JsonDerivedType(typeof(GetPackageInfo), nameof(GetPackageInfo))]
[JsonDerivedType(typeof(GetSdkInfo), nameof(GetSdkInfo))]
[JsonDerivedType(typeof(ProvideCompletionItems), nameof(ProvideCompletionItems))]
[JsonDerivedType(typeof(OnDidChangeWorkspace), nameof(OnDidChangeWorkspace))]
[JsonDerivedType(typeof(OnDidChangeModel), nameof(OnDidChangeModel))]
[JsonDerivedType(typeof(OnDidChangeModelContent), nameof(OnDidChangeModelContent))]
[JsonDerivedType(typeof(GetDiagnostics), nameof(GetDiagnostics))]
public abstract record WorkerInputMessage
{
    public required int Id { get; init; }

    protected abstract Task<object?> HandleNonGenericAsync(IServiceProvider services);

    public async Task<WorkerOutputMessage> HandleAndGetOutputAsync(IServiceProvider services)
    {
        try
        {
            var outgoing = await HandleNonGenericAsync(services);
            if (ReferenceEquals(outgoing, NoOutput.Instance))
            {
                return new WorkerOutputMessage.Empty { Id = Id };
            }
            else
            {
                return new WorkerOutputMessage.Success(outgoing) { Id = Id };
            }
        }
        catch (Exception ex)
        {
            return new WorkerOutputMessage.Failure(ex.ToString()) { Id = Id };
        }
    }

    public sealed record Compile(IEnumerable<InputCode> Inputs) : WorkerInputMessage<CompiledAssembly>
    {
        public override async Task<CompiledAssembly> HandleAsync(IServiceProvider services)
        {
            var compiler = services.GetRequiredService<CompilerProxy>();
            return await compiler.CompileAsync(Inputs);
        }
    }

    public sealed record GetOutput(IEnumerable<InputCode> Inputs, string? File, string OutputType) : WorkerInputMessage<string>
    {
        public override async Task<string> HandleAsync(IServiceProvider services)
        {
            var compiler = services.GetRequiredService<CompilerProxy>();
            var result = await compiler.CompileAsync(Inputs);
            if (File is null)
            {
                return await result.GetRequiredGlobalOutput(OutputType).GetTextAsync(outputFactory: null);
            }
            else
            {
                return result.Files.TryGetValue(File, out var file)
                    ? await file.GetRequiredOutput(OutputType).GetTextAsync(outputFactory: null)
                    : throw new InvalidOperationException($"File '{File}' not found.");
            }
        }
    }

    public sealed record UsePackage(string? Version, string Key, string PackageId, string PackageFolder) : WorkerInputMessage<NoOutput>
    {
        public override Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var dependencyRegistry = services.GetRequiredService<DependencyRegistry>();
            var packageRegistry = services.GetRequiredService<PackageRegistry>();
            var nuGetDownloader = services.GetRequiredService<Lazy<NuGetDownloader>>();
            if (string.IsNullOrWhiteSpace(Version))
            {
                dependencyRegistry.RemoveAssemblies(Key);
                packageRegistry.Remove(Key);
            }
            else
            {
                var package = nuGetDownloader.Value.GetPackage(
                packageId: PackageId,
                version: Version,
                folder: PackageFolder);

                dependencyRegistry.SetAssemblies(Key, package.GetAssembliesAsync);
                packageRegistry.Set(Key, package);
            }

            return NoOutput.AsyncInstance;
        }
    }

    public sealed record GetPackageInfo(string Key) : WorkerInputMessage<NuGetPackageInfo?>
    {
        public override async Task<NuGetPackageInfo?> HandleAsync(IServiceProvider services)
        {
            var packageRegistry = services.GetRequiredService<PackageRegistry>();
            if (packageRegistry.TryGetValue(Key, out var package))
            {
                return await package.GetInfoAsync();
            }
            else
            {
                return null;
            }
        }
    }

    public sealed record GetSdkInfo(string VersionToLoad) : WorkerInputMessage<SdkInfo>
    {
        public override async Task<SdkInfo> HandleAsync(IServiceProvider services)
        {
            var sdkDownloader = services.GetRequiredService<SdkDownloader>();
            return await sdkDownloader.GetInfoAsync(VersionToLoad);
        }
    }

    public sealed record ProvideCompletionItems(string ModelUri, Position Position, MonacoCompletionContext Context) : WorkerInputMessage<MonacoCompletionList>
    {
        public override Task<MonacoCompletionList> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            return languageServices.ProvideCompletionItemsAsync(ModelUri, Position, Context);
        }
    }

    public sealed record OnDidChangeWorkspace(ImmutableArray<ModelInfo> Models) : WorkerInputMessage<NoOutput>
    {
        public override Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            languageServices.OnDidChangeWorkspace(Models);
            return NoOutput.AsyncInstance;
        }
    }

    public sealed record OnDidChangeModel(string ModelUri) : WorkerInputMessage<NoOutput>
    {
        public override Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            languageServices.OnDidChangeModel(modelUri: ModelUri);
            return NoOutput.AsyncInstance;
        }
    }

    public sealed record OnDidChangeModelContent(ModelContentChangedEvent Args) : WorkerInputMessage<NoOutput>
    {
        public override async Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            await languageServices.OnDidChangeModelContentAsync(Args);
            return NoOutput.Instance;
        }
    }

    public sealed record GetDiagnostics() : WorkerInputMessage<ImmutableArray<MarkerData>>
    {
        public override Task<ImmutableArray<MarkerData>> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            return languageServices.GetDiagnosticsAsync();
        }
    }
}

public abstract record WorkerInputMessage<TOutput> : WorkerInputMessage
{
    protected sealed override async Task<object?> HandleNonGenericAsync(IServiceProvider services)
    {
        return await HandleAsync(services);
    }

    public abstract Task<TOutput> HandleAsync(IServiceProvider services);
}

public sealed record NoOutput
{
    private NoOutput() { }

    public static NoOutput Instance { get; } = new();
    public static Task<NoOutput> AsyncInstance { get; } = Task.FromResult(Instance);
}
