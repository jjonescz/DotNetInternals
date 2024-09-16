using Microsoft.CodeAnalysis.LanguageServer;
using Microsoft.CodeAnalysis.LanguageServer.LanguageServer;
using Microsoft.CodeAnalysis.LanguageServer.Services;
using Microsoft.Extensions.Logging;

namespace DotNetInternals;

public static class RoslynLanguageServerAccessors
{
    public static async Task StartLanguageServerAsync(ILoggerFactory loggerFactory, Stream inputStream, Stream outputStream)
    {
        var serverConfiguration = new ServerConfiguration(
            LaunchDebugger: false,
            MinimumLogLevel: LogLevel.Debug,
            StarredCompletionsPath: null,
            TelemetryLevel: null,
            SessionId: null,
            ExtensionAssemblyPaths: [],
            DevKitDependencyPath: null,
            RazorSourceGenerator: null,
            RazorDesignTimePath: null,
            ExtensionLogDirectory: "/tmp/ExtensionLogDirectory");

        var extensionManager = ExtensionAssemblyManager.Create(serverConfiguration, loggerFactory);

        var assemblyLoader = new CustomExportAssemblyLoader(extensionManager, loggerFactory);

        var typeRefResolver = new ExtensionTypeRefResolver(assemblyLoader, loggerFactory);

        using var exportProvider = await ExportProviderBuilder.CreateExportProviderAsync(extensionManager, assemblyLoader, serverConfiguration.DevKitDependencyPath, loggerFactory);

        var server = new LanguageServerHost(inputStream, outputStream, exportProvider, loggerFactory.CreateLogger<LanguageServerHost>(), typeRefResolver);

        server.Start();

        await server.WaitForExitAsync();
    }
}
