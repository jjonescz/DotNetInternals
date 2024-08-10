using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Encodings.Web;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotNetInternals.Lab;

/// <summary>
/// Decides which DLLs are loaded (e.g., the built-in Roslyn DLLs
/// or the user-specified version downloaded from NuGet).
/// </summary>
/// <remarks>
/// This class does not do the actual loading.
/// Instead it's consulted by <see cref="CompilerProxy"/>
/// when it needs to load compiler DLLs or referenced DLLs.
/// </remarks>
internal sealed class DependencyRegistry
{
    private readonly Dictionary<string, Func<Task<ImmutableArray<LoadedAssembly>>>> assemblies = new();

    /// <summary>
    /// Can be used to detect changes.
    /// </summary>
    public int Iteration { get; private set; }

    public bool IsEmpty => assemblies.Count == 0;

    public async IAsyncEnumerable<LoadedAssembly> GetAssembliesAsync()
    {
        foreach (var assemblyGroup in assemblies.Values)
        {
            foreach (var assembly in await assemblyGroup())
            {
                yield return assembly;
            }
        }
    }

    public void SetAssemblies(string key, Func<Task<ImmutableArray<LoadedAssembly>>> assemblies)
    {
        this.assemblies[key] = assemblies;
        Iteration++;
    }

    public void RemoveAssemblies(string key)
    {
        this.assemblies.Remove(key);
        Iteration++;
    }
}

internal sealed class LoadedAssembly
{
    public required string Name { get; init; }
    public required Stream Data { get; init; }
}

internal static class NuGetUtil
{
    public static string GetPackageVersionListUrl(string packageId)
    {
        return $"https://dev.azure.com/dnceng/public/_artifacts/feed/dotnet-tools/NuGet/{packageId}/versions";
    }
}

internal sealed class NuGetDownloader
{
    private readonly SourceRepository repository;
    private readonly SourceCacheContext cacheContext;
    private readonly AsyncLazy<FindPackageByIdResource> findPackageById;

    public NuGetDownloader()
    {
        ImmutableArray<Lazy<INuGetResourceProvider>> providers =
        [
            new(() => new RegistrationResourceV3Provider()),
            new(() => new DependencyInfoResourceV3Provider()),
            new(() => new CustomHttpHandlerResourceV3Provider()),
            new(() => new HttpSourceResourceProvider()),
            new(() => new ServiceIndexResourceV3Provider()),
            new(() => new RemoteV3FindPackageByIdResourceProvider()),
        ];
        repository = Repository.CreateSource(
            providers,
            "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json");
        cacheContext = new SourceCacheContext();
        findPackageById = new(() => repository.GetResourceAsync<FindPackageByIdResource>());
    }

    public NuGetDownloadablePackage GetPackage(string packageId, string version, string folder)
    {
        return new NuGetDownloadablePackage(folder: folder, downloadAsync);

        async Task<MemoryStream> downloadAsync()
        {
            var stream = new MemoryStream();
            await (await findPackageById).CopyNupkgToStreamAsync(
                packageId,
                NuGetVersion.Parse(version),
                stream,
                cacheContext,
                NullLogger.Instance,
                CancellationToken.None);
            return stream;
        }
    }
}

internal sealed class NuGetDownloadablePackage
{
    private readonly string folder;
    private readonly AsyncLazy<MemoryStream> _stream;

    public NuGetDownloadablePackage(string folder, Func<Task<MemoryStream>> streamFactory)
    {
        this.folder = folder;
        _stream = new(streamFactory);
    }

    private async Task<Stream> GetStreamAsync()
    {
        var result = await _stream;
        result.Position = 0;
        return result;
    }

    private async Task<PackageArchiveReader> GetReaderAsync()
    {
        return new(await GetStreamAsync(), leaveStreamOpen: true);
    }

    public async Task<RepositoryMetadata> GetRepositoryMetadataAsync()
    {
        using var reader = await GetReaderAsync();
        return reader.NuspecReader.GetRepositoryMetadata();
    }

    public async Task<ImmutableArray<LoadedAssembly>> GetAssembliesAsync()
    {
        const string extension = ".dll";
        using var reader = await GetReaderAsync();
        return reader.GetFiles()
            .Where(file =>
            {
                // Get only DLL files directly in the specified folder
                // and starting with `Microsoft.`.
                return file.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
                    file.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                    file.LastIndexOf('/') is int lastSlashIndex &&
                    lastSlashIndex == folder.Length &&
                    file.AsSpan(lastSlashIndex + 1).StartsWith("Microsoft.", StringComparison.Ordinal);
            })
            .Select(file =>
            {
                ZipArchiveEntry entry = reader.GetEntry(file);
                using var entryStream = entry.Open();
                var memoryStream = new MemoryStream(new byte[entry.Length]);
                entryStream.CopyTo(memoryStream);
                memoryStream.Position = 0;
                return new LoadedAssembly()
                {
                    Name = entry.Name[..^extension.Length],
                    Data = memoryStream,
                };
            })
            .ToImmutableArray();
    }
}

internal sealed class CustomHttpHandlerResourceV3Provider : ResourceProvider
{
    public CustomHttpHandlerResourceV3Provider()
        : base(typeof(HttpHandlerResource), nameof(CustomHttpHandlerResourceV3Provider))
    {
    }

    public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository source, CancellationToken token)
    {
        return Task.FromResult(TryCreate(source));
    }

    private static Tuple<bool, INuGetResource?> TryCreate(SourceRepository source)
    {
        if (source.PackageSource.IsHttp)
        {
            var clientHandler = new CorsClientHandler();
            var messageHandler = new ServerWarningLogHandler(clientHandler);
            return new(true, new HttpHandlerResourceV3(clientHandler, messageHandler));
        }

        return new(false, null);
    }
}

internal sealed class CorsClientHandler : HttpClientHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) == true)
        {
            request.RequestUri = new Uri("https://cloudflare-cors-anywhere.knowpicker.workers.dev/?" +
                UrlEncoder.Default.Encode(request.RequestUri.ToString()));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
