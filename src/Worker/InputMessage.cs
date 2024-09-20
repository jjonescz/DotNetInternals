using BlazorMonaco;
using BlazorMonaco.Editor;
using DotNetInternals.Lab;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;

namespace DotNetInternals;

[JsonDerivedType(typeof(Compile), nameof(Compile))]
[JsonDerivedType(typeof(UsePackage), nameof(UsePackage))]
[JsonDerivedType(typeof(GetPackageInfo), nameof(GetPackageInfo))]
[JsonDerivedType(typeof(GetSdkInfo), nameof(GetSdkInfo))]
[JsonDerivedType(typeof(ProvideCompletionItems), nameof(ProvideCompletionItems))]
[JsonDerivedType(typeof(OnDidChangeModel), nameof(OnDidChangeModel))]
[JsonDerivedType(typeof(OnDidChangeModelContent), nameof(OnDidChangeModelContent))]
[JsonDerivedType(typeof(GetDiagnostics), nameof(GetDiagnostics))]
public abstract record WorkerInputMessage
{
    public required int Id { get; init; }

    public abstract Task<object?> HandleNonGenericAsync(IServiceProvider services);

    public sealed record Compile(IEnumerable<InputCode> Inputs) : WorkerInputMessage<CompiledAssembly>
    {
        public override async Task<CompiledAssembly> HandleAsync(IServiceProvider services)
        {
            var compiler = services.GetRequiredService<CompilerProxy>();
            return await compiler.CompileAsync(Inputs);
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

    public sealed record OnDidChangeModel(string Code) : WorkerInputMessage<NoOutput>
    {
        public override Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            languageServices.OnDidChangeModel(Code);
            return NoOutput.AsyncInstance;
        }
    }

    public sealed record OnDidChangeModelContent(ModelContentChangedEvent Args) : WorkerInputMessage<NoOutput>
    {
        public override Task<NoOutput> HandleAsync(IServiceProvider services)
        {
            var languageServices = services.GetRequiredService<LanguageServices>();
            languageServices.OnDidChangeModelContent(Args);
            return NoOutput.AsyncInstance;
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
    public sealed override async Task<object?> HandleNonGenericAsync(IServiceProvider services)
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
