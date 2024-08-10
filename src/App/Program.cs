using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DotNetInternals;
using DotNetInternals.Lab;
using Blazored.LocalStorage;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddScoped<ICompiler, CompilerProxy>();
builder.Services.AddScoped<Lazy<NuGetDownloader>>();
builder.Services.AddScoped<DependencyRegistry>();

if (builder.HostEnvironment.IsDevelopment())
{
    builder.Logging.AddFilter("DotNetInternals.*", LogLevel.Debug);
}

var host = builder.Build();

host.Services.GetRequiredService<ILogger<Program>>()
    .LogInformation("Environment: {Environment}", builder.HostEnvironment.Environment);

await host.RunAsync();
