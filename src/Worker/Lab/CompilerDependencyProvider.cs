using NuGet.Versioning;

namespace DotNetInternals.Lab;

/// <summary>
/// Provides compiler dependencies into the <see cref="DependencyRegistry"/>.
/// </summary>
internal sealed class CompilerDependencyProvider(
    DependencyRegistry dependencyRegistry,
    BuiltInCompilerProvider builtInProvider,
    IEnumerable<ICompilerDependencyProviderPlugin> plugins)
{
    private readonly Dictionary<CompilerKind, CompilerDependency> loaded = new();

    public async Task<CompilerDependencyInfo> GetLoadedInfoAsync(CompilerKind compilerKind)
    {
        var dependency = loaded.TryGetValue(compilerKind, out var result)
            ? result
            : builtInProvider.GetBuiltInDependency(compilerKind);
        return await dependency.Info();
    }

    public async Task UseAsync(CompilerKind compilerKind, string? version, BuildConfiguration configuration)
    {
        var info = CompilerInfo.For(compilerKind);

        bool any = false;
        List<string>? errors = null;
        CompilerDependency? found = await findAsync();

        if (!any)
        {
            throw new InvalidOperationException($"Nothing could be parsed out of the specified version '{version}'.");
        }

        if (found is null)
        {
            throw new InvalidOperationException($"Specified version was not found.\n{errors?.JoinToString("\n")}");
        }

        dependencyRegistry.Set(info, found.Assemblies);
        loaded[compilerKind] = found;

        async Task<CompilerDependency?> findAsync()
        {
            foreach (var specifier in CompilerVersionSpecifier.Parse(version))
            {
                any = true;
                foreach (var plugin in plugins)
                {
                    try
                    {
                        if (await plugin.FindCompilerAsync(info, specifier, configuration) is { } dependency)
                        {
                            return dependency;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors ??= new();
                        errors.Add($"{plugin.GetType().Name}: {ex.Message}");
                    }
                }
            }

            return null;
        }
    }
}

internal interface ICompilerDependencyProviderPlugin
{
    Task<CompilerDependency?> FindCompilerAsync(CompilerInfo info, CompilerVersionSpecifier specifier, BuildConfiguration configuration);
}

internal sealed class BuiltInCompilerProvider : ICompilerDependencyProviderPlugin
{
    private readonly ImmutableDictionary<CompilerKind, CompilerDependency> builtIn = LoadBuiltIn();

    private static ImmutableDictionary<CompilerKind, CompilerDependency> LoadBuiltIn()
    {
        return ImmutableDictionary.CreateRange(Enum.GetValues<CompilerKind>()
            .Select(kind => KeyValuePair.Create(kind, createOne(kind))));

        static CompilerDependency createOne(CompilerKind compilerKind)
        {
            var specifier = new CompilerVersionSpecifier.BuiltIn();
            var info = CompilerInfo.For(compilerKind);
            return new()
            {
                Info = () => Task.FromResult(new CompilerDependencyInfo(assemblyName: info.AssemblyNames[0])
                {
                    VersionSpecifier = specifier,
                    Configuration = BuildConfiguration.Release,
                }),
                Assemblies = () => Task.FromResult(ImmutableArray<LoadedAssembly>.Empty),
            };
        }
    }

    public CompilerDependency GetBuiltInDependency(CompilerKind compilerKind)
    {
        return builtIn.TryGetValue(compilerKind, out var result)
            ? result
            : throw new InvalidOperationException($"Built-in compiler {compilerKind} was not found.");
    }

    public Task<CompilerDependency?> FindCompilerAsync(CompilerInfo info, CompilerVersionSpecifier specifier, BuildConfiguration configuration)
    {
        if (specifier is CompilerVersionSpecifier.BuiltIn)
        {
            return Task.FromResult<CompilerDependency?>(GetBuiltInDependency(info.CompilerKind));
        }

        return Task.FromResult<CompilerDependency?>(null);
    }
}

public enum BuildConfiguration
{
    Debug,
    Release,
}

public enum CompilerKind
{
    Roslyn,
    Razor,
}

internal sealed record CompilerInfo(
    CompilerKind CompilerKind,
    string RepositoryUrl,
    string PackageId,
    string PackageFolder,
    int BuildDefinitionId,
    ImmutableArray<string> AssemblyNames)
{
    public static readonly CompilerInfo Roslyn = new(
        CompilerKind: CompilerKind.Roslyn,
        RepositoryUrl: "https://github.com/dotnet/roslyn",
        PackageId: "Microsoft.Net.Compilers.Toolset",
        PackageFolder: "tasks/netcore/bincore",
        BuildDefinitionId: 95, // roslyn-CI
        AssemblyNames: ["Microsoft.CodeAnalysis.CSharp", "Microsoft.CodeAnalysis"]);

    public static readonly CompilerInfo Razor = new(
        CompilerKind: CompilerKind.Razor,
        RepositoryUrl: "https://github.com/dotnet/razor",
        PackageId: "Microsoft.Net.Compilers.Razor.Toolset",
        PackageFolder: "source-generators",
        BuildDefinitionId: 103, // razor-tooling-ci
        AssemblyNames: ["Microsoft.CodeAnalysis.Razor.Compiler", ..Roslyn.AssemblyNames]);

    public static CompilerInfo For(CompilerKind kind)
    {
        return kind switch
        {
            CompilerKind.Roslyn => Roslyn,
            CompilerKind.Razor => Razor,
            _ => throw Util.Unexpected(kind),
        };
    }
}

public abstract record CompilerVersionSpecifier
{
    public static IEnumerable<CompilerVersionSpecifier> Parse(string? specifier)
    {
        // Null -> use the built-in compiler.
        if (string.IsNullOrWhiteSpace(specifier))
        {
            yield return new BuiltIn();
            yield break;
        }

        if (specifier == "latest")
        {
            yield return new NuGetLatest();
            yield break;
        }

        // Single number -> an AzDo build number or PR number.
        if (int.TryParse(specifier, out int number) && number > 0)
        {
            yield return new Build(number);
            yield return new PullRequest(number);
            yield break;
        }

        if (NuGetVersion.TryParse(specifier, out var nuGetVersion))
        {
            yield return new NuGet(nuGetVersion);
        }

        yield return new Branch(specifier);
    }

    public sealed record BuiltIn : CompilerVersionSpecifier;
    public sealed record NuGet(NuGetVersion Version) : CompilerVersionSpecifier;
    public sealed record NuGetLatest : CompilerVersionSpecifier;
    public sealed record Build(int BuildId) : CompilerVersionSpecifier;
    public sealed record PullRequest(int PullRequestNumber) : CompilerVersionSpecifier;
    public sealed record Branch(string BranchName) : CompilerVersionSpecifier;
}

internal sealed class CompilerDependency
{
    public required Func<Task<CompilerDependencyInfo>> Info { get; init; }
    public required Func<Task<ImmutableArray<LoadedAssembly>>> Assemblies { get; init; }
}

public sealed record CompilerDependencyInfo
{
    private CompilerDependencyInfo((string Version, string CommitHash, string RepoUrl) arg)
    {
        Version = arg.Version;
        Commit = new() { Hash = arg.CommitHash, RepoUrl = arg.RepoUrl };
    }

    public CompilerDependencyInfo(string version, string commitHash, string repoUrl)
        : this((Version: version, CommitHash: commitHash, RepoUrl: repoUrl))
    {
    }

    public CompilerDependencyInfo(string assemblyName)
        : this(FromAssembly(assemblyName))
    {
    }

    public required CompilerVersionSpecifier VersionSpecifier { get; init; }
    public string Version { get; }
    public CommitLink Commit { get; }
    public required BuildConfiguration Configuration { get; init; }
    public bool CanChangeBuildConfiguration { get; init; }

    private static (string Version, string CommitHash, string RepoUrl) FromAssembly(string assemblyName)
    {
        string version = "";
        string hash = "";
        string repositoryUrl = "";
        foreach (var attribute in Assembly.Load(assemblyName).CustomAttributes)
        {
            switch (attribute.AttributeType.FullName)
            {
                case "System.Reflection.AssemblyInformationalVersionAttribute"
                    when attribute.ConstructorArguments is [{ Value: string informationalVersion }] &&
                        VersionUtil.TryParseInformationalVersion(informationalVersion, out var parsedVersion, out var parsedHash):
                    version = parsedVersion;
                    hash = parsedHash;
                    break;

                case "System.Reflection.AssemblyMetadataAttribute"
                    when attribute.ConstructorArguments is [{ Value: "RepositoryUrl" }, { Value: string repoUrl }]:
                    repositoryUrl = repoUrl;
                    break;
            }
        }

        return (Version: version, CommitHash: hash, RepoUrl: repositoryUrl);
    }
}

public sealed record CommitLink
{
    public required string RepoUrl { get; init; }
    public required string Hash { get; init; }
    public string ShortHash => VersionUtil.GetShortCommitHash(Hash);
    public string Url => string.IsNullOrEmpty(Hash) ? "" : VersionUtil.GetCommitUrl(RepoUrl, Hash);
}
