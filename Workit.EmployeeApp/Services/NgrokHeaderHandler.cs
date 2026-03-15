namespace Workit.EmployeeApp.Services;

/// <summary>
/// Adds the ngrok-skip-browser-warning header to every outgoing request so that
/// ngrok's browser interstitial page is bypassed when the API is tunnelled via ngrok.
/// This header is harmless against non-ngrok hosts.
/// </summary>
public sealed class NgrokHeaderHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation("ngrok-skip-browser-warning", "1");
        return base.SendAsync(request, cancellationToken);
    }
}
