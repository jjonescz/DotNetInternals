using DotNetInternals;
using KristofferStrube.Blazor.WebWorkers;
using System.Runtime.Versioning;
using System.Text.Json;

Console.WriteLine("Worker started.");

var serviceProvider = WorkerServices.Create(
    baseUrl: args[0],
    debugLogs: args[1] == bool.TrueString);

Imports.RegisterOnMessage(async e =>
{
    try
    {
        var data = e.GetPropertyAsString("data") ?? string.Empty;
        var incoming = JsonSerializer.Deserialize<WorkerInputMessage>(data);
        PostMessage(await incoming!.HandleAndGetOutputAsync(serviceProvider));
    }
    catch (Exception ex)
    {
        PostMessage(new WorkerOutputMessage.Failure(ex) { Id = -1 });
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
