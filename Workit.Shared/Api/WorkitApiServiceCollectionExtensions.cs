using Microsoft.Extensions.DependencyInjection;

namespace Workit.Shared.Api;

public static class WorkitApiServiceCollectionExtensions
{
    public static IServiceCollection AddWorkitApiClients(this IServiceCollection services)
    {
        services.AddScoped<IAccessTokenAccessor, NoOpAccessTokenAccessor>();
        services.AddScoped<IAuthApi, AuthApi>();
        services.AddScoped<ICompanyApi, CompanyApi>();
        services.AddScoped<ICustomersApi, CustomersApi>();
        services.AddScoped<IEmployeesApi, EmployeesApi>();
        services.AddScoped<IJobsApi, JobsApi>();
        services.AddScoped<ITimeEntriesApi, TimeEntriesApi>();

        return services;
    }
}
