using BlazorWorker.BackgroundServiceFactory;
using BlazorWorker.Core;
using BlazorWorker.WorkerBackgroundService;

namespace DotNetInternals.Lab;

internal sealed class LanguageServerController(
    ILogger<LanguageServerController> logger,
    IWorkerFactory workerFactory)
{
    public async Task StartInWorkerAsync()
    {
        logger.LogInformation("Starting language server...");
        IWorker worker = await workerFactory.CreateAsync();
        IWorkerBackgroundService<LanguageServerWorker> service = await worker.CreateBackgroundServiceAsync<LanguageServerWorker>();
        logger.LogInformation("Background service created.");
        var exitCode = await service.RunAsync(s => s.StartAsync());
        logger.LogInformation("Language server exited with code {ExitCode}.", exitCode);
    }
}

internal sealed class LanguageServerWorker
{
    public async Task<int> StartAsync()
    {
        Console.WriteLine("Language server worker started.");

        var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new LoggerProvider()));

        await RoslynLanguageServerAccessors.StartLanguageServerAsync(loggerFactory, new MemoryStream(), new MemoryStream());

        return 0;
    }

    /// <summary>
    /// A simle console logger provider.
    /// </summary>
    /// <remarks>
    /// The default console logger provider creates threads so it's unsupported on Blazor WebAssembly.
    /// The Blazor WebAssembly's built-in console logger provider can be obtained from WebAssemblyHostBuilder
    /// but fails because of missing JS imports.
    /// </remarks>
    class LoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Logger();

        public void Dispose() { }

        class Logger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                Console.WriteLine(formatter(state, exception));
            }
        }
    }
}
