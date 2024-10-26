using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DotNetInternals.Lab;

internal sealed class AzDoDownloader
{
    public async Task<ImmutableArray<LoadedAssembly>> DownloadAsync(int pullRequestNumber)
    {
        var baseUrl = new Uri("https://dev.azure.com/dnceng-public");
        var project = "public";
        using var connection = new VssConnection(baseUrl, new VssCredentials());
        using var buildClient = connection.GetClient<BuildHttpClient>();
        var builds = await buildClient.GetBuildsAsync2(
            project: project,
            definitions: [95], // roslyn-CI
            branchName: $"refs/pull/{pullRequestNumber}/merge",
            top: 1);

        if (builds is not [{ } build, ..])
        {
            throw new InvalidOperationException($"No builds of PR {pullRequestNumber} found.");
        }

        throw new InvalidOperationException($"Found build {build.BuildNumber}.");
    }
}
