namespace Workit.OwnerApp.Services;

/// <summary>
/// One place for "the app is doing something": a page wraps a slow action in
/// <see cref="Begin"/> and the layout's <c>BusyOverlay</c> shows a spinner
/// with the message until it ends. Nested scopes stack; the overlay stays up
/// until the last one is disposed and shows the most recent message.
/// Scoped per circuit, so one user's work never shows on another's screen.
/// </summary>
public sealed class BusyService
{
    private readonly List<Scope> scopes = [];

    /// <summary>Raised whenever the busy state or message changes.</summary>
    public event Action? Changed;

    public bool IsBusy => scopes.Count > 0;

    /// <summary>What the overlay says right now — the most recently begun scope's message.</summary>
    public string? Message => scopes.Count > 0 ? scopes[^1].Message : null;

    /// <summary>
    /// Marks the app busy until the returned scope is disposed:
    /// <c>using var _ = Busy.Begin("Linking expenses to jobs…");</c>
    /// </summary>
    public IDisposable Begin(string message)
    {
        var scope = new Scope(this, message);
        scopes.Add(scope);
        Changed?.Invoke();
        return scope;
    }

    /// <summary>Runs <paramref name="action"/> while busy, whatever it returns or throws.</summary>
    public async Task RunAsync(string message, Func<Task> action)
    {
        using var _ = Begin(message);
        await action();
    }

    private void End(Scope scope)
    {
        if (scopes.Remove(scope))
            Changed?.Invoke();
    }

    private sealed class Scope(BusyService owner, string message) : IDisposable
    {
        public string Message { get; } = message;
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            owner.End(this);
        }
    }
}
