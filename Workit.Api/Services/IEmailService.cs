namespace Workit.Api.Services;

internal interface IEmailService
{
    /// <summary>
    /// Welcomes a new account with a one-time link to choose its own password.
    /// Passwords are never emailed — see <see cref="IAccountInviteService"/>.
    /// </summary>
    Task SendOwnerInviteAsync(string name, string email, string setupUrl);
    Task SendEmployeeInviteAsync(string name, string email, string setupUrl);
    Task SendPasswordResetAsync(string email, string resetUrl);
    Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType, DateOnly start, DateOnly end);
    Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType, DateOnly start, DateOnly end, bool approved, string reviewNotes);
}
