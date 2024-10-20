using Microsoft.Extensions.Logging;
using System.Runtime.Loader;

namespace DotNetInternals.Lab;

public static class CompilerConstants
{
    public static readonly string RoslynPackageId = "Microsoft.Net.Compilers.Toolset";
    public static readonly string RoslynPackageFolder = "tasks/netcore/bincore";
    public static readonly string RazorPackageId = "Microsoft.Net.Compilers.Razor.Toolset";
    public static readonly string RazorPackageFolder = "source-generators";
    public static readonly string CompilerAssemblyName = "DotNetInternals.Compiler";
    public static readonly string RoslynAssemblyName = "Microsoft.CodeAnalysis.CSharp";
    public static readonly string RazorAssemblyName = "Microsoft.CodeAnalysis.Razor.Compiler";
}

/// <summary>
/// Can load our compiler project with any given Roslyn/Razor compiler version as dependency.
/// </summary>
internal sealed class CompilerProxy(
    ILogger<CompilerProxy> logger,
    DependencyRegistry dependencyRegistry,
    AssemblyDownloader assemblyDownloader,
    CompilerLoaderServices loaderServices)
{
    private LoadedCompiler? loaded;
    private int iteration;

    public async Task<CompiledAssembly> CompileAsync(IEnumerable<InputCode> inputs)
    {
        try
        {
            if (loaded is null || dependencyRegistry.Iteration != iteration)
            {
                var previousIteration = dependencyRegistry.Iteration;
                var currentlyLoaded = await LoadCompilerAsync();

                if (dependencyRegistry.Iteration == previousIteration)
                {
                    loaded = currentlyLoaded;
                    iteration = dependencyRegistry.Iteration;
                }
                else
                {
                    Debug.Assert(loaded is not null);
                }
            }

            using var _ = loaded.LoadContext.EnterContextualReflection();
            var result = loaded.Compiler.Compile(inputs);

            if (loaded.LoadContext is CompilerLoader { LastFailure: { } failure })
            {
                loaded = null;
                throw new InvalidOperationException(
                    $"Failed to load '{failure.AssemblyName}'.", failure.Exception);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compile.");
            return CompiledAssembly.Fail(ex.ToString());
        }
    }

    private async Task<LoadedCompiler> LoadCompilerAsync()
    {
        AssemblyLoadContext alc;
        if (dependencyRegistry.IsEmpty)
        {
            // Load the built-in compiler.
            alc = AssemblyLoadContext.Default;
        }
        else
        {
            var assemblies = await dependencyRegistry.GetAssembliesAsync()
                .ToImmutableDictionaryAsync(a => a.Name, a => a, loadAssemblyAsync,
                [
                    // All assemblies depending on Roslyn/Razor need to be reloaded
                    // to avoid type mismatches between assemblies from different contexts.
                    // If they are not loaded from the registry, we will reload the built-in ones.
                    // Preload all built-in ones that our Compiler project depends on here
                    // (we cannot do that inside the AssemblyLoadContext because of async).
                    CompilerConstants.CompilerAssemblyName,
                    CompilerConstants.RoslynAssemblyName,
                    CompilerConstants.RazorAssemblyName,
                    "Basic.Reference.Assemblies.AspNet90",
                    "Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.CSharp.Test.Utilities",
                    "Microsoft.CodeAnalysis.Razor.Test",
                ]);

            logger.LogDebug("Available assemblies ({Count}): {Assemblies}",
                assemblies.Count,
                assemblies.Keys.JoinToString(", "));

            alc = new CompilerLoader(loaderServices, assemblies, dependencyRegistry.Iteration);
        }

        using var _ = alc.EnterContextualReflection();
        Assembly compilerAssembly = alc.LoadFromAssemblyName(new(CompilerConstants.CompilerAssemblyName));
        Type compilerType = compilerAssembly.GetType(CompilerConstants.CompilerAssemblyName)!;
        var compiler = (ICompiler)Activator.CreateInstance(compilerType)!;
        return new() { LoadContext = alc, Compiler = compiler };

        async Task<LoadedAssembly> loadAssemblyAsync(string name)
        {
            return new()
            {
                Name = name,
                Data = await assemblyDownloader.DownloadAsync(name),
            };
        }
    }

    private sealed class LoadedCompiler
    {
        public required AssemblyLoadContext LoadContext { get; init; }
        public required ICompiler Compiler { get; init; }
    }
}

internal readonly record struct AssemblyLoadFailure
{
    public required AssemblyName AssemblyName { get; init; }
    public required Exception Exception { get; init; }
}

internal sealed record CompilerLoaderServices(
    ILogger<CompilerLoader> Logger);

internal sealed class CompilerLoader(
    CompilerLoaderServices services,
    IReadOnlyDictionary<string, LoadedAssembly> knownAssemblies,
    int iteration)
    : AssemblyLoadContext(nameof(CompilerLoader) + iteration)
{
    private readonly Dictionary<string, Assembly> loadedAssemblies = new();

    /// <summary>
    /// In production in WebAssembly, the loader exceptions aren't propagated to the caller.
    /// Hence this is used to fail the compilation when assembly loading fails.
    /// </summary>
    public AssemblyLoadFailure? LastFailure { get; set; }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        try
        {
            return LoadCore(assemblyName);
        }
        catch (Exception ex)
        {
            services.Logger.LogError(ex, "Failed to load {AssemblyName}.", assemblyName);
            LastFailure = new() { AssemblyName = assemblyName, Exception = ex };
            throw;
        }
    }

    private Assembly? LoadCore(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name)
        {
            if (loadedAssemblies.TryGetValue(name, out var loaded))
            {
                services.Logger.LogDebug("✔️ {AssemblyName}", assemblyName);

                return loaded;
            }

            if (knownAssemblies.TryGetValue(name, out var loadedAssembly))
            {
                services.Logger.LogDebug("▶️ {AssemblyName}", assemblyName);

                loaded = LoadFromStream(loadedAssembly.Data);
                loadedAssemblies.Add(name, loaded);
                return loaded;
            }

            services.Logger.LogDebug("➖ {AssemblyName}", assemblyName);

            loaded = Default.LoadFromAssemblyName(assemblyName);
            loadedAssemblies.Add(name, loaded);
            return loaded;
        }

        return null;
    }
}
