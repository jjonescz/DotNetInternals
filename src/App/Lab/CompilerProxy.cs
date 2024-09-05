using System.Runtime.Loader;

namespace DotNetInternals.Lab;

internal sealed class CompilerProxy(
    ILogger<CompilerProxy> logger,
    DependencyRegistry dependencyRegistry,
    HttpClient client,
    CompilerLoaderServices loaderServices)
{
    public static readonly string RoslynPackageId = "Microsoft.Net.Compilers.Toolset";
    public static readonly string RoslynPackageFolder = "tasks/netcore/bincore";
    public static readonly string RazorPackageId = "Microsoft.Net.Compilers.Razor.Toolset";
    public static readonly string RazorPackageFolder = "source-generators";
    public static readonly string CompilerAssemblyName = "DotNetInternals.Compiler";
    public static readonly string RoslynAssemblyName = "Microsoft.CodeAnalysis.CSharp";
    public static readonly string RazorAssemblyName = "Microsoft.CodeAnalysis.Razor.Compiler";

    public static NuGetPackageInfo GetBuiltInInfo(string assemblyName)
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

        return NuGetPackageInfo.Create(
            version: version,
            commitHash: hash,
            repoUrl: repositoryUrl);
    }

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
            return new(
                BaseDirectory: "/",
                Files: ImmutableDictionary<string, CompiledFile>.Empty,
                Diagnostics: [],
                GlobalOutputs: [new(CompiledAssembly.DiagnosticsOutputType, ex.ToString())],
                NumErrors: 1,
                NumWarnings: 0);
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
                    CompilerAssemblyName,
                    RoslynAssemblyName,
                    RazorAssemblyName,
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
        Assembly compilerAssembly = alc.LoadFromAssemblyName(new(CompilerAssemblyName));
        Type compilerType = compilerAssembly.GetType(CompilerAssemblyName)!;
        var compiler = (ICompiler)Activator.CreateInstance(compilerType)!;
        return new() { LoadContext = alc, Compiler = compiler };

        async Task<LoadedAssembly> loadAssemblyAsync(string name)
        {
            return new()
            {
                Name = name,
                Data = await client.GetStreamAsync($"_framework/{name}.wasm"),
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

internal sealed record InitialCode(string SuggestedFileName, string TextTemplate)
{
    public static readonly InitialCode Razor = new("TestComponent.razor", """
        <TestComponent Param="1" />

        @code {
            [Parameter] public int Param { get; set; }
        }

        """);

    public static readonly InitialCode CSharp = new("Program.cs", """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Diagnostics;
        using System.Diagnostics.CodeAnalysis;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;

        class Program
        {
            static void Main()
            {
                Console.WriteLine("Hello.");
            }
        }

        """);

    public static readonly InitialCode Cshtml = new("TestPage.cshtml", """
        @page
        @using System.ComponentModel.DataAnnotations
        @model PageModel
        @addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers

        <form method="post">
            Name:
            <input asp-for="Customer.Name" />
            <input type="submit" />
        </form>

        @functions {
            public class PageModel
            {
                public Customer Customer { get; set; }
            }

            public class Customer
            {
                public int Id { get; set; }

                [Required, StringLength(10)]
                public string Name { get; set; }
            }
        }

        """);

    public string SuggestedFileNameWithoutExtension => Path.GetFileNameWithoutExtension(SuggestedFileName);
    public string SuggestedFileExtension => Path.GetExtension(SuggestedFileName);

    public string GetFinalFileName(string suffix)
    {
        return string.IsNullOrEmpty(suffix)
            ? SuggestedFileName
            : SuggestedFileNameWithoutExtension + suffix + SuggestedFileExtension;
    }

    public InputCode ToInputCode(string? finalFileName = null)
    {
        finalFileName ??= SuggestedFileName;

        return new()
        {
            FileName = finalFileName,
            Text = finalFileName == SuggestedFileName
                ? TextTemplate
                : TextTemplate.Replace(
                    SuggestedFileNameWithoutExtension,
                    Path.GetFileNameWithoutExtension(finalFileName),
                    StringComparison.Ordinal),
        };
    }
}
