using PostHog;
using System.Text.Json;

namespace Workit.Api.Analytics;

/// <summary>
/// Sends events to PostHog. Registered only when PostHog:ProjectApiKey is configured.
/// </summary>
public sealed class PostHogAnalyticsService(IPostHogClient postHog) : IAnalyticsService
{
    public void Capture(string userId, string eventName, object? properties = null)
    {
        var props = properties is null ? null : ToStringObjectDictionary(properties);
        postHog.Capture(userId, eventName, props);
    }

    /// <summary>
    /// Converts an anonymous object (or any POCO) to Dictionary&lt;string, object&gt;
    /// by serialising through JSON so PostHog receives proper primitives rather than JsonElement wrappers.
    /// </summary>
    private static Dictionary<string, object>? ToStringObjectDictionary(object obj)
    {
        var json = JsonSerializer.Serialize(obj);
        var doc  = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String  => (object)prop.Value.GetString()!,
                JsonValueKind.Number  => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null!,
                _                    => prop.Value.ToString()
            };
        }

        return dict;
    }
}

/// <summary>
/// No-op fallback used when PostHog is not configured.
/// </summary>
public sealed class NullAnalyticsService : IAnalyticsService
{
    public void Capture(string userId, string eventName, object? properties = null) { }
}
