using DotNetInternals;
using DotNetInternals.Lab;
using KristofferStrube.Blazor.WebWorkers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;
using System.Text.Json;

Console.WriteLine("Worker started.");

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddProvider(new SimpleConsoleLoggerProvider());
});
services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(args[0]) });
services.AddScoped<CompilerLoaderServices>();
services.AddScoped<CompilerProxy>();
services.AddScoped<DependencyRegistry>();
services.AddScoped<PackageRegistry>();
services.AddScoped<Lazy<NuGetDownloader>>();
services.AddScoped<SdkDownloader>();
services.AddScoped<LanguageServices>();
var serviceProvider = services.BuildServiceProvider();

Imports.RegisterOnMessage(async e =>
{
    var data = e.GetPropertyAsString("data") ?? string.Empty;
    var incoming = JsonSerializer.Deserialize<WorkerInputMessage>(data);
    var outgoing = await incoming!.HandleNonGenericAsync(serviceProvider);
    if (!ReferenceEquals(outgoing, NoOutput.Instance))
    {
        Imports.PostMessage(JsonSerializer.Serialize(outgoing));
    }
});

Imports.PostMessage("ready");

// Keep running.
while (true)
{
    await Task.Delay(100);
}

[SupportedOSPlatform("browser")]
partial class Program;
