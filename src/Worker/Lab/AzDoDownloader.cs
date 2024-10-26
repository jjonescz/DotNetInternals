using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using System.ComponentModel;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetInternals.Lab;

internal sealed class AzDoDownloader
{
    private static readonly string baseAddress = "https://dev.azure.com/dnceng-public/public";
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(),
            new TypeConverterJsonConverterFactory(),
        },
    };

    private readonly HttpClient client = new();

    public async Task<ImmutableArray<LoadedAssembly>> DownloadAsync(int pullRequestNumber, BuildConfiguration buildConfiguration)
    {
        var build = await GetLatestPrBuildAsync(
            definitionId: 95, // roslyn-CI
            pullRequestNumber: pullRequestNumber);

        var artifact = await GetArtifactAsync(
            buildId: build.Id,
            artifactName: $"Transport_Artifacts_Windows_{buildConfiguration}");

        var files = await GetArtifactFilesAsync(
            buildId: build.Id,
            artifact: artifact);

        var rehydrates = files.Items.Where(f => f.Path.EndsWith("/rehydrate.cmd", StringComparison.Ordinal));

        var fileName = "Microsoft.CodeAnalysis.dll";

        foreach (var rehydrate in rehydrates)
        {
            if (rehydrate.Blob is null)
            {
                continue;
            }

            var rehydrateContent = await GetFileAsStringAsync(
                buildId: build.Id,
                artifactName: artifact.Name,
                fileId: rehydrate.Blob.Id);

            foreach (var match in AzDoPatterns.RehydrateCommand.Matches(rehydrateContent).Cast<Match>())
            {
                if (match.Groups[1].ValueSpan.Equals(fileName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Found file '{fileName}' in '{match.Groups[2].ValueSpan}'.");
                }
            }
        }

        throw new InvalidOperationException($"No file '{fileName}' in artifact '{artifact.Name}' of build {build.Id}.");
    }

    private async Task<Build> GetLatestPrBuildAsync(int definitionId, int pullRequestNumber)
    {
        var builds = await GetBuildsAsync(
            definitionId: definitionId,
            branchName: $"refs/pull/{pullRequestNumber}/merge",
            top: 1);

        if (builds is not { Count: > 0, Value: [{ } build, ..] })
        {
            throw new InvalidOperationException($"No builds of PR {pullRequestNumber} found.");
        }

        return build;
    }

    private async Task<AzDoCollection<Build>?> GetBuildsAsync(int definitionId, string branchName, int top)
    {
        var uri = new UriBuilder(baseAddress);
        uri.AppendPathSegments("_apis", "build", "builds");
        uri.AppendQuery("definitions", definitionId.ToString());
        uri.AppendQuery("branchName", branchName);
        uri.AppendQuery("$top", top.ToString());
        uri.AppendQuery("api-version", "7.1");

        return await client.GetFromJsonAsync<AzDoCollection<Build>>(uri.ToString(), options);
    }

    private async Task<BuildArtifact> GetArtifactAsync(int buildId, string artifactName)
    {
        var uri = new UriBuilder(baseAddress);
        uri.AppendPathSegments("_apis", "build", "builds", buildId.ToString(), "artifacts");
        uri.AppendQuery("artifactName", artifactName);
        uri.AppendQuery("api-version", "7.1");

        return await client.GetFromJsonAsync<BuildArtifact>(uri.ToString(), options)
            ?? throw new InvalidOperationException($"No artifact '{artifactName}' found in build {buildId}.");
    }

    private async Task<ArtifactFiles> GetArtifactFilesAsync(int buildId, BuildArtifact artifact)
    {
        return await GetFilesAsync(
            buildId: buildId,
            artifactName: artifact.Name,
            fileId: artifact.Resource.Data);
    }

    private async Task<ArtifactFiles> GetFilesAsync(int buildId, string artifactName, string fileId)
    {
        var uri = GetFileUri(
            buildId: buildId,
            artifactName: artifactName,
            fileId: fileId);

        return await client.GetFromJsonAsync<ArtifactFiles>(uri, options)
            ?? throw new InvalidOperationException($"No files found in artifact '{artifactName}' of build {buildId}.");
    }

    private async Task<string> GetFileAsStringAsync(int buildId, string artifactName, string fileId)
    {
        return await client.GetStringAsync(GetFileUri(
            buildId: buildId,
            artifactName: artifactName,
            fileId: fileId));
    }

    private static string GetFileUri(int buildId, string artifactName, string fileId)
    {
        var uri = new UriBuilder(baseAddress);
        uri.AppendPathSegments("_apis", "build", "builds", buildId.ToString(), "artifacts");
        uri.AppendQuery("artifactName", artifactName);
        uri.AppendQuery("fileId", fileId);
        uri.AppendQuery("fileName", string.Empty);
        uri.AppendQuery("api-version", "7.1");
        return uri.ToString();
    }
}

internal enum BuildConfiguration
{
    Debug,
    Release,
}

internal static partial class AzDoPatterns
{
    [GeneratedRegex("""^mklink /h %~dp0\\(.*) %HELIX_CORRELATION_PAYLOAD%\\(.*) > nul\r?$""", RegexOptions.Multiline)]
    public static partial Regex RehydrateCommand { get; }
}

internal sealed class AzDoCollection<T>
{
    public required int Count { get; init; }
    public required ImmutableArray<T> Value { get; init; }
}

/// <summary>
/// Can convert types that have a <see cref="TypeConverterAttribute"/>.
/// </summary>
internal sealed class TypeConverterJsonConverterFactory : JsonConverterFactory
{
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var typeConverter = TypeDescriptor.GetConverter(typeToConvert);
        var jsonConverter = (JsonConverter?)Activator.CreateInstance(
            typeof(TypeConverterJsonConverter<>).MakeGenericType([typeToConvert]),
            [typeConverter]);
        return jsonConverter;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.GetCustomAttribute<TypeConverterAttribute>() != null;
    }
}

/// <summary>
/// Created by <see cref="TypeConverterJsonConverterFactory"/>.
/// </summary>
internal sealed class TypeConverterJsonConverter<T>(TypeConverter typeConverter) : JsonConverter<T>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeConverter.CanConvertFrom(typeof(string)) || typeConverter.CanConvertTo(typeof(string));
    }

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetString() is { } s ? (T?)typeConverter.ConvertFromInvariantString(s) : default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        if (value is not null)
        {
            writer.WriteStringValue(typeConverter.ConvertToInvariantString(value));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

internal sealed class ArtifactFiles
{
    public required ImmutableArray<ArtifactFile> Items { get; init; }
}

internal sealed class ArtifactFile
{
    public required string Path { get; init; }
    public ArtifactFileBlob? Blob { get; init; }
}

internal sealed class ArtifactFileBlob
{
    public required string Id { get; init; }
    public required long Size { get; init; }
}
