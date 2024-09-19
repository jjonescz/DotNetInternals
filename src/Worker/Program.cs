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
    WorkerInputMessage? incoming = null;
    try
    {
        var data = e.GetPropertyAsString("data") ?? string.Empty;
        incoming = JsonSerializer.Deserialize<WorkerInputMessage>(data);
        var outgoing = await incoming!.HandleNonGenericAsync(serviceProvider);
        if (ReferenceEquals(outgoing, NoOutput.Instance))
        {
            PostMessage(new WorkerOutputMessage.Empty { Id = incoming.Id });
        }
        else
        {
            PostMessage(new WorkerOutputMessage.Success(outgoing) { Id = incoming.Id });
        }
    }
    catch (Exception ex)
    {
        PostMessage(new WorkerOutputMessage.Failure(ex.ToString()) { Id = incoming?.Id ?? -1 });
    }
});

PostMessage(new WorkerOutputMessage.Ready { Id = -1 });

// Keep running.
while (true)
{
    await Task.Delay(100);
}

static void PostMessage(WorkerOutputMessage message)
{
    Imports.PostMessage(JsonSerializer.Serialize(message));
}

[SupportedOSPlatform("browser")]
partial class Program;
