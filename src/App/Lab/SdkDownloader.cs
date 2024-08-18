using System.Net.Http.Json;

namespace DotNetInternals.Lab;

internal sealed class SdkDownloader(
    HttpClient client)
{
    public async Task<CommitLink> GetCommitAsync(string version)
    {
        var url = $"https://dotnetcli.azureedge.net/dotnet/Sdk/{version}/productCommit-win-x64.json";
        var response = await client.GetAsync(url.WithCorsProxy());
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProductCommit>();
        return CommitLink.Create(result?.Sdk.Commit, "https://github.com/dotnet/sdk");
    }

    private sealed class ProductCommit
    {
        public required Entry Sdk { get; init; }

        public sealed class Entry
        {
            public required string Commit { get; init; }
        }
    }
}
