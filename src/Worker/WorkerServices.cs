using DotNetInternals.Lab;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotNetInternals;

public static class WorkerServices
{
    public static IServiceProvider Create(string baseUrl, bool debugLogs)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (debugLogs)
            {
                builder.AddFilter("DotNetInternals.*", LogLevel.Debug);
            }

            builder.AddProvider(new SimpleConsoleLoggerProvider());
        });
        services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(baseUrl) });
        services.AddScoped<CompilerLoaderServices>();
        services.AddScoped<AssemblyDownloader>();
        services.AddScoped<CompilerProxy>();
        services.AddScoped<DependencyRegistry>();
        services.AddScoped<Lazy<NuGetDownloader>>();
        services.AddScoped<SdkDownloader>();
        services.AddScoped<CompilerDependencyProvider>();
        services.AddScoped<BuiltInCompilerProvider>();
        services.AddScoped<ICompilerDependencyProviderPlugin, NuGetDownloaderPlugin>();
        services.AddScoped<ICompilerDependencyProviderPlugin, AzDoDownloader>();
        services.AddScoped<ICompilerDependencyProviderPlugin, BuiltInCompilerProvider>(sp => sp.GetRequiredService<BuiltInCompilerProvider>());
        services.AddScoped<LanguageServices>();
        return services.BuildServiceProvider();
    }
}
