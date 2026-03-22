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
        services.AddScoped<IToolsApi, ToolsApi>();
        services.AddScoped<IMaterialsApi, MaterialsApi>();
        services.AddScoped<IInvoicesApi, InvoicesApi>();
        services.AddScoped<IAbsenceApi, AbsenceApi>();
        services.AddScoped<IWorkDutyApi, WorkDutyApi>();

        return services;
    }
}
