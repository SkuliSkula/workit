using Microsoft.Extensions.DependencyInjection;

namespace Workit.Shared.Payday;

public static class PaydayServiceCollectionExtensions
{
    public static IServiceCollection AddPaydayApiClients(this IServiceCollection services)
    {
        services.AddHttpClient("PaydayApi", client =>
        {
            client.BaseAddress = new Uri("https://api.payday.is/");
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

        return services;
    }
}
