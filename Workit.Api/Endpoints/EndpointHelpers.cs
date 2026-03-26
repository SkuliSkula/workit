using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Workit.Api.Endpoints;

internal static class EndpointHelpers
{
    internal static async Task<IResult> ExecuteDbAsync(
        Func<Task<IResult>> action,
        ILogger logger,
        string operation)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Authorization context invalid while {Operation}.", operation);
            return Results.Unauthorized();
        }
        catch (Exception ex) when (IsDatabaseException(ex))
        {
            logger.LogError(ex, "Database unavailable while {Operation}.", operation);
            return Results.Problem(
                title: "Database unavailable",
                detail: $"The API could not access the database while {operation}. Check the database connection and try again.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static bool IsDatabaseException(Exception ex)
    {
        Exception? current = ex;
        while (current is not null)
        {
            if (current is DbException or DbUpdateException or TimeoutException)
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    internal static bool IsValidCompany(Workit.Shared.Models.Company company) =>
        !string.IsNullOrWhiteSpace(company.Name) &&
        !string.IsNullOrWhiteSpace(company.Ssn) &&
        !string.IsNullOrWhiteSpace(company.Email) &&
        !string.IsNullOrWhiteSpace(company.Address) &&
        !string.IsNullOrWhiteSpace(company.Phone) &&
        !string.IsNullOrWhiteSpace(company.Owner);

    internal static bool IsValidCustomer(Workit.Shared.Models.Customer customer) =>
        !string.IsNullOrWhiteSpace(customer.Name) &&
        !string.IsNullOrWhiteSpace(customer.Ssn);

    internal static bool IsValidEmployee(Workit.Shared.Models.Employee employee) =>
        !string.IsNullOrWhiteSpace(employee.DisplayName) &&
        !string.IsNullOrWhiteSpace(employee.Ssn) &&
        !string.IsNullOrWhiteSpace(employee.Email);

    internal static bool IsValidCredentials(string email, string password) =>
        !string.IsNullOrWhiteSpace(email) &&
        !string.IsNullOrWhiteSpace(password) &&
        password.Trim().Length >= 8;

    internal static string GenerateOwnerPassword()
    {
        var raw = Guid.NewGuid().ToString("N");
        return char.ToUpper(raw[0]) + raw[1..10] + "!1";
    }
}
