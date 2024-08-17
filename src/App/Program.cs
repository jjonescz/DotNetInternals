using Blazored.LocalStorage;
using DotNetInternals;
using DotNetInternals.Lab;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<CompilerProxy>();
builder.Services.AddScoped<CompilerLoaderServices>();
builder.Services.AddScoped<Lazy<NuGetDownloader>>();
builder.Services.AddScoped<DependencyRegistry>();

builder.Logging.AddFilter("DotNetInternals.*",
    static (logLevel) => logLevel >= Logging.LogLevel);

if (builder.HostEnvironment.IsDevelopment())
{
    Logging.LogLevel = LogLevel.Debug;
}

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("Environment: {Environment}", builder.HostEnvironment.Environment);

await host.RunAsync();
