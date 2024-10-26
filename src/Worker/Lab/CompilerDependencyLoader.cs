namespace DotNetInternals.Lab;

internal sealed class CompilerDependencyLoader(
    DependencyRegistry dependencyRegistry,
    PackageRegistry packageRegistry,
    Lazy<NuGetDownloader> nuGetDownloader,
    AzDoDownloader azDoDownloader)
{
    public void Use(CompilerKind compilerKind, string? version)
    {
        (string key, string packageId, string packageFolder) = compilerKind switch
        {
            CompilerKind.Roslyn => ("roslyn", CompilerDependencyConstants.RoslynPackageId, CompilerDependencyConstants.RoslynPackageFolder),
            CompilerKind.Razor => ("razor", CompilerDependencyConstants.RazorPackageId, CompilerDependencyConstants.RazorPackageFolder),
            _ => throw new ArgumentException($"Unexpected value: {compilerKind}", paramName: nameof(compilerKind)),
        };

        // Null -> use the built-in compiler.
        if (string.IsNullOrWhiteSpace(version))
        {
            dependencyRegistry.RemoveAssemblies(key);
            packageRegistry.Remove(key);
        }

        // Single number -> an AzDo build number.
        else if (int.TryParse(version, out int number) && number > 0)
        {
            dependencyRegistry.SetAssemblies(key, () => azDoDownloader.DownloadAsync(pullRequestNumber: number, BuildConfiguration.Release));
            packageRegistry.Remove(key);
        }

        // Otherwise -> NuGet package version.
        else
        {
            var package = nuGetDownloader.Value.GetPackage(
                packageId: packageId,
                version: version,
                folder: packageFolder);

            dependencyRegistry.SetAssemblies(key, package.GetAssembliesAsync);
            packageRegistry.Set(key, package);
        }
    }
}

public enum CompilerKind
{
    Roslyn,
    Razor,
}
