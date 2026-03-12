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

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });
builder.Services.AddWorkitApiClients();
builder.Services.AddScoped<IAccessTokenAccessor, BrowserAccessTokenAccessor>();
builder.Services.AddScoped<AuthSessionService>();

await builder.Build().RunAsync();
