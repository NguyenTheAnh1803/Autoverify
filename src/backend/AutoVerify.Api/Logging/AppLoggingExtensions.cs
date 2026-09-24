using Microsoft.Extensions.Logging;

namespace AutoVerify.Api.Logging;

public static class AppLoggingExtensions
{
    public static IDisposable? BeginSpecialScope(this ILogger logger, string? reason = null)
    {
        var state = new Dictionary<string, object?>
        {
            [LoggingConstants.LogTypeProperty] = LoggingConstants.LogTypes.Special
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            state["Reason"] = reason;
        }

        return logger.BeginScope(state);
    }

    public static IDisposable? BeginUserScope(this ILogger logger, string? userId = null, string? action = null)
    {
        var state = new Dictionary<string, object?>
        {
            [LoggingConstants.LogTypeProperty] = LoggingConstants.LogTypes.UserEvent
        };

        if (!string.IsNullOrWhiteSpace(userId))
        {
            state[LoggingConstants.UserIdProperty] = userId;
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            state["UserAction"] = action;
        }

        return logger.BeginScope(state);
    }

    public static IDisposable? BeginAutoScope(this ILogger logger, string jobName)
    {
        var state = new Dictionary<string, object?>
        {
            [LoggingConstants.LogTypeProperty] = LoggingConstants.LogTypes.Auto,
            [LoggingConstants.JobNameProperty] = jobName
        };

        return logger.BeginScope(state);
    }

    public static void LogSpecial(
        this ILogger logger,
        LogLevel level,
        string message,
        params object?[] args)
    {
        using (logger.BeginSpecialScope())
        {
            logger.Log(level, message, args);
        }
    }

    public static void LogSpecial(
        this ILogger logger,
        Exception? exception,
        string message,
        params object?[] args)
    {
        using (logger.BeginSpecialScope())
        {
            logger.LogError(exception, message, args);
        }
    }

    public static void LogUserEvent(
        this ILogger logger,
        string? userId,
        string action,
        string message,
        params object?[] args)
    {
        using (logger.BeginUserScope(userId, action))
        {
            logger.LogInformation(message, args);
        }
    }

    public static void LogAuto(
        this ILogger logger,
        string jobName,
        string message,
        params object?[] args)
    {
        using (logger.BeginAutoScope(jobName))
        {
            logger.LogInformation(message, args);
        }
    }
}

