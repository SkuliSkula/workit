namespace Workit.Api.Services;

internal interface IEmailService
{
    Task SendOwnerWelcomeAsync(string name, string email, string password);
    Task SendEmployeeWelcomeAsync(string name, string email, string password);
    Task SendPasswordResetAsync(string email, string resetUrl);
    Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType, DateOnly start, DateOnly end);
    Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType, DateOnly start, DateOnly end, bool approved, string reviewNotes);
}
