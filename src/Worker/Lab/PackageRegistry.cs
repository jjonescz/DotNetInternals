namespace DotNetInternals.Lab;

internal sealed class PackageRegistry
{
    private readonly Dictionary<string, NuGetDownloadablePackage> map = new();

    public bool Remove(string key)
    {
        return map.Remove(key);
    }

    public void Set(string key, NuGetDownloadablePackage package)
    {
        map[key] = package;
    }

    public bool TryGetValue(
        string key,
        [NotNullWhen(returnValue: true)] out NuGetDownloadablePackage? package)
    {
        return map.TryGetValue(key, out package);
    }
}
