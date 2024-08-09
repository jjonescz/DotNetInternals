using System.Collections.Immutable;
using System.IO.Compression;
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
    private readonly Dictionary<string, Func<Task<ImmutableArray<byte[]>>>> dlls = new();

    /// <summary>
    /// Can be used to detect changes.
    /// </summary>
    public int Iteration { get; private set; }

    public async IAsyncEnumerable<ImmutableArray<byte[]>> GetDllsAsync()
    {
        foreach (var dll in dlls.Values)
        {
            yield return await dll();
        }
    }

    public void SetDlls(string key, Func<Task<ImmutableArray<byte[]>>> dlls)
    {
        this.dlls[key] = dlls;
        Iteration++;
    }
}

internal sealed class NuGetDownloader
{
    private readonly SourceRepository repository;
    private readonly SourceCacheContext cacheContext;
    private readonly AsyncLazy<FindPackageByIdResource> findPackageById;

    public NuGetDownloader()
    {
        repository = Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");
        cacheContext = new SourceCacheContext();
        findPackageById = new(() => repository.GetResourceAsync<FindPackageByIdResource>());
    }

    public async Task<ImmutableArray<NuGetVersion>> GetVersionsAsync(string packageId)
    {
        var versions = await (await findPackageById).GetAllVersionsAsync(
            packageId,
            cacheContext,
            NullLogger.Instance,
            CancellationToken.None);
        return versions.ToImmutableArray();
    }

    public NuGetDownloadablePackage GetPackage(string packageId, NuGetVersion version, string folder)
    {
        return new NuGetDownloadablePackage(folder: folder, downloadAsync);

        async Task<MemoryStream> downloadAsync()
        {
            var stream = new MemoryStream();
            await (await findPackageById).CopyNupkgToStreamAsync(
                packageId,
                version,
                stream,
                cacheContext,
                NullLogger.Instance,
                CancellationToken.None);
            return stream;
        }
    }
}

public sealed class NuGetDownloadablePackage
{
    private readonly string folder;
    private readonly AsyncLazy<MemoryStream> _stream;

    public NuGetDownloadablePackage(string folder, Func<Task<MemoryStream>> streamFactory)
    {
        this.folder = folder;
        _stream = new(streamFactory);
    }

    public async Task<Stream> GetStreamAsync()
    {
        var result = await _stream;
        result.Position = 0;
        return result;
    }

    public async Task<RepositoryMetadata> GetRepositoryMetadataAsync()
    {
        using var reader = new PackageArchiveReader(await GetStreamAsync());
        return reader.NuspecReader.GetRepositoryMetadata();
    }

    public async Task<ImmutableArray<byte[]>> GetDllsAsync()
    {
        using var reader = new PackageArchiveReader(await GetStreamAsync());
        return reader.GetFiles(folder)
            .Where(static file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(file =>
            {
                ZipArchiveEntry entry = reader.GetEntry(file);
                using var entryStream = entry.Open();
                var buffer = new byte[entry.Length];
                entryStream.ReadExactly(buffer, 0, buffer.Length);
                return buffer;
            })
            .ToImmutableArray();
    }
}
