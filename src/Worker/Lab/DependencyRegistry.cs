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

internal enum AssemblyDataFormat
{
    Dll,
    Webcil,
}

internal sealed class LoadedAssembly
{
    public required string Name { get; init; }
    public required ImmutableArray<byte> Data { get; init; }
    public required AssemblyDataFormat Format { get; init; }
}
