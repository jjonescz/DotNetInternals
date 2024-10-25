using MetadataReferenceService.BlazorWasm;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
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
    private static readonly Func<Stream, bool, byte[]> convertFromWebcil = typeof(BlazorWasmMetadataReferenceService).Assembly
        .GetType("MetadataReferenceService.BlazorWasm.WasmWebcil.WebcilConverterUtil")!
        .GetMethod("ConvertFromWebcil", BindingFlags.Public | BindingFlags.Static)!
        .CreateDelegate<Func<Stream, bool, byte[]>>();
    private LoadedCompiler? loaded;
    private int iteration;

    public async Task<CompiledAssembly> CompileAsync(CompilationInput input)
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

            if (input.Configuration is not null && loaded.DllAssemblies is null)
            {
                var assemblies = loaded.Assemblies ?? await LoadAssembliesAsync();
                loaded.DllAssemblies = assemblies.ToImmutableDictionary(
                    p => p.Key,
                    p => p.Value.Format switch
                    {
                        AssemblyDataFormat.Dll => p.Value.Data,
                        AssemblyDataFormat.Webcil => WebcilToDll(p.Value.Data),
                        _ => throw new InvalidOperationException($"Unknown assembly format: {p.Value.Format}"),
                    });
            }

            using var _ = loaded.LoadContext.EnterContextualReflection();
            var result = loaded.Compiler.Compile(input, loaded.DllAssemblies);

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

    private static ImmutableArray<byte> WebcilToDll(ImmutableArray<byte> bytes)
    {
        var inputStream = new MemoryStream(ImmutableCollectionsMarshal.AsArray(bytes)!);
        return ImmutableCollectionsMarshal.AsImmutableArray(convertFromWebcil(inputStream, /* wrappedInWebAssembly */ true));
    }

    private async Task<ImmutableDictionary<string, LoadedAssembly>> LoadAssembliesAsync()
    {
        var assemblies = await dependencyRegistry.GetAssembliesAsync()
            .ToImmutableDictionaryAsync(a => a.Name, a => a, LoadAssemblyAsync,
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

        return assemblies;
    }

    private async Task<LoadedAssembly> LoadAssemblyAsync(string name)
    {
        return new()
        {
            Name = name,
            Data = await assemblyDownloader.DownloadAsync(name),
            Format = AssemblyDataFormat.Webcil,
        };
    }

    private async Task<LoadedCompiler> LoadCompilerAsync()
    {
        ImmutableDictionary<string, LoadedAssembly>? assemblies = null;

        AssemblyLoadContext alc;
        if (dependencyRegistry.IsEmpty)
        {
            // Load the built-in compiler.
            alc = AssemblyLoadContext.Default;
        }
        else
        {
            assemblies ??= await LoadAssembliesAsync();
            alc = new CompilerLoader(loaderServices, assemblies, dependencyRegistry.Iteration);
        }

        using var _ = alc.EnterContextualReflection();
        Assembly compilerAssembly = alc.LoadFromAssemblyName(new(CompilerConstants.CompilerAssemblyName));
        Type compilerType = compilerAssembly.GetType(CompilerConstants.CompilerAssemblyName)!;
        var compiler = (ICompiler)Activator.CreateInstance(compilerType)!;
        return new() { LoadContext = alc, Compiler = compiler, Assemblies = assemblies };
    }

    private sealed class LoadedCompiler
    {
        public required AssemblyLoadContext LoadContext { get; init; }
        public required ICompiler Compiler { get; init; }
        public ImmutableDictionary<string, LoadedAssembly>? Assemblies { get; init; }
        public ImmutableDictionary<string, ImmutableArray<byte>>? DllAssemblies { get; set; }
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

                var bytes = ImmutableCollectionsMarshal.AsArray(loadedAssembly.Data)!;
                loaded = LoadFromStream(new MemoryStream(bytes));
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
