using System.Text;
using System.Text.Json;

namespace Workit.Api.Services;

internal sealed class ResendEmailService : IEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _from;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<ResendEmailService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _from = configuration["Resend:From"] ?? "Workit <noreply@workit.is>";
        _logger = logger;
    }

    public Task SendOwnerWelcomeAsync(string name, string email, string password) =>
        SendAsync(email, "Welcome to Workit – Your Account Details", $"""
            <h2>Welcome to Workit, {H(name)}!</h2>
            <p>Your owner account has been created. Log in with the credentials below.</p>
            <p><strong>Email:</strong> {H(email)}</p>
            <p><strong>Password:</strong> {H(password)}</p>
            <p>Please change your password after your first login.</p>
            <p><a href="https://app.workit.is">Log in to Workit</a></p>
            """);

    public Task SendEmployeeWelcomeAsync(string name, string email, string password) =>
        SendAsync(email, "Welcome to Workit – Your Account Details", $"""
            <h2>Welcome to Workit, {H(name)}!</h2>
            <p>Your employee account has been created. Log in with the credentials below.</p>
            <p><strong>Email:</strong> {H(email)}</p>
            <p><strong>Password:</strong> {H(password)}</p>
            <p>Please change your password after your first login.</p>
            <p><a href="https://app.workit.is">Log in to Workit</a></p>
            """);

    public Task SendPasswordResetAsync(string email, string resetUrl) =>
        SendAsync(email, "Workit – Password Reset", $"""
            <h2>Reset your password</h2>
            <p>We received a request to reset the password for your Workit account.</p>
            <p><a href="{H(resetUrl)}">Click here to reset your password</a></p>
            <p>This link expires in 1 hour. If you did not request a password reset, you can ignore this email.</p>
            """);

    public Task SendAbsenceRequestedAsync(string ownerEmail, string employeeName, string absenceType, DateOnly start, DateOnly end) =>
        SendAsync(ownerEmail, $"New absence request from {employeeName}", $"""
            <h2>New absence request</h2>
            <p><strong>{H(employeeName)}</strong> has submitted an absence request.</p>
            <table>
              <tr><td><strong>Type:</strong></td><td>{H(absenceType)}</td></tr>
              <tr><td><strong>From:</strong></td><td>{start:dd MMM yyyy}</td></tr>
              <tr><td><strong>To:</strong></td><td>{end:dd MMM yyyy}</td></tr>
            </table>
            <p><a href="https://app.workit.is">Review in Workit</a></p>
            """);

    public Task SendAbsenceReviewedAsync(string employeeEmail, string employeeName, string absenceType, DateOnly start, DateOnly end, bool approved, string reviewNotes) =>
        SendAsync(employeeEmail, $"Your absence request has been {(approved ? "approved" : "denied")}", $"""
            <h2>Absence request {(approved ? "approved" : "denied")}</h2>
            <p>Hi {H(employeeName)},</p>
            <p>Your absence request has been <strong>{(approved ? "approved" : "denied")}</strong>.</p>
            <table>
              <tr><td><strong>Type:</strong></td><td>{H(absenceType)}</td></tr>
              <tr><td><strong>From:</strong></td><td>{start:dd MMM yyyy}</td></tr>
              <tr><td><strong>To:</strong></td><td>{end:dd MMM yyyy}</td></tr>
            </table>
            {(string.IsNullOrWhiteSpace(reviewNotes) ? "" : $"<p><strong>Notes:</strong> {H(reviewNotes)}</p>")}
            <p><a href="https://app.workit.is">View in Workit</a></p>
            """);

    private async Task SendAsync(string to, string subject, string html)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("Resend");
            var payload = JsonSerializer.Serialize(new
            {
                from = _from,
                to = new[] { to },
                subject,
                html
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("emails", content);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError("Resend returned {StatusCode} sending to {Email}: {Body}", (int)response.StatusCode, to, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", to);
        }
    }

    private static string H(string s) => System.Web.HttpUtility.HtmlEncode(s);
}
