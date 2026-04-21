namespace Workit.Api.Analytics;

public interface IAnalyticsService
{
    /// <summary>
    /// Capture an event for a specific user.
    /// </summary>
    void Capture(string userId, string eventName, object? properties = null);
}
