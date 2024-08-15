using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace DotNetInternals.Lab;

internal sealed class CompilerProxy(
    ILogger<CompilerProxy> logger,
    DependencyRegistry dependencyRegistry,
    HttpClient client)
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
                        informationalVersion.IndexOf('+') is >= 0 and var plusIndex:
                    version = informationalVersion[..plusIndex];
                    hash = informationalVersion[(plusIndex + 1)..];
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
            return loaded.Compiler.Compile(inputs);
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
                .ToImmutableDictionaryAsync(a => a.Name, a => a, reloadAssemblyAsync,
                [
                    // All assemblies depending on Roslyn/Razor need to be reloaded
                    // to avoid type mismatches between assemblies from different contexts.
                    // If they are not loaded from the registry, we will reload the built-in ones.
                    // Preload all built-in ones that our Compiler project depends on here
                    // (we cannot do that inside the AssemblyLoadContext because of async).
                    CompilerAssemblyName,
                    RoslynAssemblyName,
                    RazorAssemblyName,
                    "Basic.Reference.Assemblies.AspNet80",
                    "Microsoft.CodeAnalysis",
                    "Microsoft.CodeAnalysis.CSharp.Test.Utilities",
                    "Microsoft.CodeAnalysis.Razor.Test",
                    "Microsoft.CodeAnalysis.Test.Utilities",
                ]);

            logger.LogDebug("Available assemblies ({Count}): {Assemblies}",
                assemblies.Count,
                assemblies.Keys.JoinToString(", "));

            alc = new CompilerLoader(logger, assemblies, dependencyRegistry.Iteration);
        }

        using var _ = alc.EnterContextualReflection();
        Assembly compilerAssembly = alc.LoadFromAssemblyName(new(CompilerAssemblyName));
        Type compilerType = compilerAssembly.GetType(CompilerAssemblyName)!;
        var compiler = (ICompiler)Activator.CreateInstance(compilerType)!;
        return new() { LoadContext = alc, Compiler = compiler };

        async Task<LoadedAssembly> reloadAssemblyAsync(string name)
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

internal sealed class CompilerLoader(
    ILogger logger,
    ImmutableDictionary<string, LoadedAssembly> knownAssemblies,
    int iteration)
    : AssemblyLoadContext(nameof(CompilerLoader) + iteration)
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name &&
            knownAssemblies.TryGetValue(name, out var loadedAssembly))
        {
            logger.LogDebug("▶️ {AssemblyName}", assemblyName);

            return LoadFromStream(loadedAssembly.Data);
        }

        logger.LogDebug("➖ {AssemblyName}", assemblyName);

        return Default.LoadFromAssemblyName(assemblyName);
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

    public static readonly InitialCode CSharp = new("Class.cs", """
        class Class
        {
            public void M()
            {
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
