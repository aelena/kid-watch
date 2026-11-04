using Microsoft.Extensions.Logging;

namespace KidWatch.Agent.Notifications;

/// <summary>
/// Shows a message to the person using the laptop. The transparent, visible
/// surface of the tool.
/// </summary>
public interface INotifier
{
    void Notify(string title, string message);
}

/// <summary>
/// Default notifier: writes to the log. A real Windows toast must run in the
/// user's session (a service lives in Session 0 and cannot draw UI), so the toast
/// implementation ships with the deferred companion user-session helper.
/// </summary>
public sealed class LoggingNotifier(ILogger<LoggingNotifier> logger) : INotifier
{
    public void Notify(string title, string message) =>
        logger.LogInformation("NOTIFY {Title}: {Message}", title, message);
}
