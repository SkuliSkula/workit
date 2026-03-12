using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.FluentUI.AspNetCore.Components;
using Workit.EmployeeApp;
using Workit.EmployeeApp.Services;
using Workit.Shared.Api;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddFluentUIComponents();

var apiBaseUrl = ResolveApiBaseUrl(builder.Configuration["ApiBaseUrl"], builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddWorkitApiClients();
builder.Services.AddScoped<IAccessTokenAccessor, BrowserAccessTokenAccessor>();
builder.Services.AddScoped<AuthSessionService>();

await builder.Build().RunAsync();

static string ResolveApiBaseUrl(string? configuredApiBaseUrl, string hostBaseAddress)
{
    if (Uri.TryCreate(configuredApiBaseUrl, UriKind.Absolute, out var configuredUri))
    {
        return configuredUri.ToString();
    }

    var hostUri = new Uri(hostBaseAddress);
    var apiPort = hostUri.Scheme == Uri.UriSchemeHttps ? 7200 : 5200;

    return new UriBuilder(hostUri)
    {
        Port = apiPort,
        Path = "/"
    }.Uri.ToString();
}
