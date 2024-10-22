using DotNetInternals.Lab;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace DotNetInternals;

public class CompilerProxyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SpecifiedNuGetRoslynVersion()
    {
        var nuget = new NuGetDownloader();
        var version = "4.12.0-2.24409.2";
        var commit = "2158b591";
        var package = nuget.GetPackage(CompilerDependencyConstants.RoslynPackageId, version, CompilerDependencyConstants.RoslynPackageFolder);

        var deps = new DependencyRegistry();
        deps.SetAssemblies("roslyn", package.GetAssembliesAsync);

        using var client = new HttpClient(new MockHttpMessageHandler()) { BaseAddress = new Uri("http://localhost") };
        var assemblyDownloader = new AssemblyDownloader(client);
        var compiler = new CompilerProxy(
            NullLogger<CompilerProxy>.Instance,
            deps,
            assemblyDownloader,
            new(NullLogger<CompilerLoader>.Instance));
        var compiled = await compiler.CompileAsync([new() { FileName = "Input.cs", Text = "#error version" }]);

        var diagnosticsText = compiled.GetGlobalOutput(CompiledAssembly.DiagnosticsOutputType)!.EagerText!;
        output.WriteLine(diagnosticsText);
        Assert.Contains($"{version} ({commit})", diagnosticsText);
    }

    [Fact]
    public async Task SpecifiedNuGetRazorVersion()
    {
        var nuget = new NuGetDownloader();
        var version = "9.0.0-preview.24413.5";
        var package = nuget.GetPackage(CompilerDependencyConstants.RazorPackageId, version, CompilerDependencyConstants.RazorPackageFolder);

        var deps = new DependencyRegistry();
        deps.SetAssemblies("razor", package.GetAssembliesAsync);

        using var client = new HttpClient(new MockHttpMessageHandler()) { BaseAddress = new Uri("http://localhost") };
        var assemblyDownloader = new AssemblyDownloader(client);
        var compiler = new CompilerProxy(
            NullLogger<CompilerProxy>.Instance,
            deps,
            assemblyDownloader,
            new(NullLogger<CompilerLoader>.Instance));
        var compiled = await compiler.CompileAsync([new() { FileName = "TestComponent.razor", Text = "test" }]);

        var cSharpText = compiled.Files.Single().Value.GetOutput("C#")!.EagerText!;
        output.WriteLine(cSharpText);
        Assert.Contains("class TestComponent", cSharpText);
    }
}

internal sealed partial class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly string directory;

    public MockHttpMessageHandler()
    {
        directory = Path.GetDirectoryName(GetType().Assembly.Location)!;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (UrlRegex.Match(request.RequestUri?.ToString() ?? "") is
            {
                Success: true,
                Groups: [_, { ValueSpan: var fileName }],
            })
        {
            if (fileName.EndsWith(".wasm", StringComparison.Ordinal))
            {
                var assemblyName = fileName[..^5];
                var assemblyPath = Path.Join(directory, assemblyName) + ".dll";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(File.OpenRead(assemblyPath)),
                });
            }

            if (fileName.Equals("blazor.boot.json", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        { "resources": { "assembly": {}, "fingerprinting": {} } }
                        """),
                });
            }
        }

        throw new NotImplementedException(request.RequestUri?.ToString());
    }

    [GeneratedRegex("""^https?://localhost/_framework/(.*)$""")]
    private static partial Regex UrlRegex { get; }
}
