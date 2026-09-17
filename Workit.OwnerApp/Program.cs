using Microsoft.FluentUI.AspNetCore.Components;
using Workit.OwnerApp.Components;
using Workit.OwnerApp.Services;
using Workit.Shared.Api;
using Workit.Shared.Payday;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddFluentUIComponents();

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "https://localhost:7200/";
builder.Services.AddHttpClient("WorkitApi", client => client.BaseAddress = new Uri(apiBaseUrl));
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("WorkitApi"));
builder.Services.AddWorkitApiClients();
builder.Services.AddScoped<IAccessTokenAccessor, BrowserAccessTokenAccessor>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<BusyService>();
// Payday is reached through the Workit API (/api/payday/*); this app never holds
// Payday credentials. The proxies implement the same IPayday*Api interfaces.
builder.Services.AddPaydayProxyClients();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();

app.UseAntiforgery();

// MapStaticAssets (not UseStaticFiles) is what makes @Assets["…"] in
// App.razor emit fingerprinted URLs — Workit.OwnerApp.<hash>.styles.css —
// with immutable caching. Cloudflare sits in front of the console and caches
// CSS for hours; with plain URLs every deploy that changed a stylesheet
// looked broken until the cache expired.
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
