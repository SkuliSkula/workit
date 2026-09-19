using Microsoft.Extensions.DependencyInjection;

namespace Workit.Shared.Payday;

public static class PaydayServiceCollectionExtensions
{
    public const string ProductionBaseUrl = "https://api.payday.is/";
    public const string SandboxBaseUrl    = "https://api.test.payday.is/";

    /// <param name="baseUrl">
    /// Payday API root. Production by default; local runs pass the sandbox
    /// (<see cref="SandboxBaseUrl"/>) via <c>Payday:BaseUrl</c> so nothing is
    /// ever verified against a real company's books.
    /// </param>
    public static IServiceCollection AddPaydayApiClients(this IServiceCollection services, string? baseUrl = null)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? ProductionBaseUrl : baseUrl.TrimEnd('/') + "/";
        services.AddHttpClient("PaydayApi", client =>
        {
            client.BaseAddress = new Uri(root);
            client.DefaultRequestHeaders.Add("Api-Version", "alpha");
        });

        services.AddScoped<IPaydayTokenService, PaydayTokenService>();
        services.AddScoped<IPaydayUsersApi, PaydayUsersApi>();
        services.AddScoped<IPaydayCompaniesApi, PaydayCompaniesApi>();
        services.AddScoped<IPaydayCustomersApi, PaydayCustomersApi>();
        services.AddScoped<IPaydayEmployeesApi, PaydayEmployeesApi>();
        services.AddScoped<IPaydayPensionApi, PaydayPensionApi>();
        services.AddScoped<IPaydayPayrollApi, PaydayPayrollApi>();
        services.AddScoped<IPaydayInvoicesApi, PaydayInvoicesApi>();
        services.AddScoped<IPaydayExpensesApi, PaydayExpensesApi>();
        services.AddScoped<IPaydayProductsApi, PaydayProductsApi>();

        return services;
    }
}
