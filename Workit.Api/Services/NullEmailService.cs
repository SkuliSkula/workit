namespace Workit.Api.Services;

internal sealed class NullEmailService : IEmailService
{
    public Task SendOwnerInviteAsync(string name, string email, string setupUrl) => Task.CompletedTask;
    public Task SendEmployeeInviteAsync(string name, string email, string setupUrl) => Task.CompletedTask;
    public Task SendPasswordResetAsync(string email, string resetUrl) => Task.CompletedTask;
    public Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType, DateOnly start, DateOnly end) => Task.CompletedTask;
    public Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType, DateOnly start, DateOnly end, bool approved, string reviewNotes) => Task.CompletedTask;
}
