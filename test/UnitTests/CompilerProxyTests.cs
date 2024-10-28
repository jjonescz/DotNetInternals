using DotNetInternals.Lab;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace DotNetInternals;

public class CompilerProxyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task SpecifiedNuGetRoslynVersion()
    {
        var services = WorkerServices.CreateTest(new MockHttpMessageHandler());

        var version = "4.12.0-2.24409.2";
        var commit = "2158b591";

        await services.GetRequiredService<CompilerDependencyProvider>()
            .UseAsync(CompilerKind.Roslyn, version, BuildConfiguration.Release);

        var compiled = await services.GetRequiredService<CompilerProxy>()
            .CompileAsync(new(new([new() { FileName = "Input.cs", Text = "#error version" }])));

        var diagnosticsText = compiled.GetGlobalOutput(CompiledAssembly.DiagnosticsOutputType)!.EagerText!;
        output.WriteLine(diagnosticsText);
        Assert.Contains($"{version} ({commit})", diagnosticsText);
    }

    [Fact]
    public async Task SpecifiedNuGetRazorVersion()
    {
        var services = WorkerServices.CreateTest(new MockHttpMessageHandler());

        var version = "9.0.0-preview.24413.5";

        await services.GetRequiredService<CompilerDependencyProvider>()
            .UseAsync(CompilerKind.Razor, version, BuildConfiguration.Release);

        var compiled = await services.GetRequiredService<CompilerProxy>()
            .CompileAsync(new(new([new() { FileName = "TestComponent.razor", Text = "test" }])));

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
