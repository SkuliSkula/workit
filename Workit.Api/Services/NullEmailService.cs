namespace Workit.Api.Services;

internal sealed class NullEmailService : IEmailService
{
    public Task SendOwnerWelcomeAsync(string name, string email, string password) => Task.CompletedTask;
    public Task SendEmployeeWelcomeAsync(string name, string email, string password) => Task.CompletedTask;
    public Task SendPasswordResetAsync(string email, string resetUrl) => Task.CompletedTask;
    public Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType, DateOnly start, DateOnly end) => Task.CompletedTask;
    public Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType, DateOnly start, DateOnly end, bool approved, string reviewNotes) => Task.CompletedTask;
}
