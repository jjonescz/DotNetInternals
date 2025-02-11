using Microsoft.Extensions.Logging;

namespace DotNetLab;

/// <summary>
/// A simle console logger provider.
/// </summary>
/// <remarks>
/// The default console logger provider creates threads so it's unsupported on Blazor WebAssembly.
/// The Blazor WebAssembly's built-in console logger provider can be obtained from WebAssemblyHostBuilder
/// but fails because of missing JS imports.
/// </remarks>
internal sealed class SimpleConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger();

    public void Dispose() { }

    sealed class Logger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Console.WriteLine("Worker: {0}", formatter(state, exception));
        }
    }
}
