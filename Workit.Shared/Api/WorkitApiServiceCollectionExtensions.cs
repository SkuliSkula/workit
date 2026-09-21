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
        services.AddScoped<IJobTasksApi, JobTasksApi>();
        services.AddScoped<IPaydayProductsCacheApi, PaydayProductsCacheApi>();
        services.AddScoped<IPaydayCustomerSyncApi, PaydayCustomerSyncApi>();
        services.AddScoped<IPayrollApi, PayrollApi>();
        services.AddScoped<ITimeEntriesApi, TimeEntriesApi>();
        services.AddScoped<IToolsApi, ToolsApi>();
        services.AddScoped<IMaterialsApi, MaterialsApi>();
        services.AddScoped<IInvoicesApi, InvoicesApi>();
        services.AddScoped<IAbsenceApi, AbsenceApi>();
        services.AddScoped<IWorkDutyApi, WorkDutyApi>();
        services.AddScoped<ISalesInvoicesApi, SalesInvoicesApi>();
        services.AddScoped<IExpensesApi, ExpensesApi>();
        services.AddScoped<IJobAttachmentsApi, JobAttachmentsApi>();

        return services;
    }
}
