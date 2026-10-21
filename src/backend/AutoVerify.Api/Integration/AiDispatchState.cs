namespace AutoVerify.Api.Integration;

public sealed record AiDispatchSnapshot(
    DateTimeOffset? LastRunAtUtc, DateTimeOffset? LastSubmitAtUtc, int LastPicked, int LastSubmitted,
    string? LastError, DateTimeOffset? PausedUntilUtc);

/// <summary>What the automatic sender did most recently. Held in memory; the UI reads it, the background job writes it.</summary>
public sealed class AiDispatchState
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastRun, _lastSubmit, _pausedUntil;
    private int _lastPicked, _lastSubmitted;
    private string? _lastError;

    public bool IsPaused(DateTimeOffset now)
    {
        lock (_gate) return _pausedUntil is { } until && until > now;
    }

    public void Record(AiAutoSubmitResult result, DateTimeOffset now, TimeSpan backoff)
    {
        lock (_gate)
        {
            _lastRun = now;
            _lastPicked = result.Picked;
            _lastSubmitted = result.Submitted;
            if (result.Submitted > 0) _lastSubmit = now;
            if (result.Transient)
            {
                _pausedUntil = now + backoff;
                _lastError = result.Message;
            }
            else
            {
                _pausedUntil = null;
                if (result.Picked > 0) _lastError = result.Message; // a clean run with work clears the old error
            }
        }
    }

    public AiDispatchSnapshot Snapshot()
    {
        lock (_gate) return new AiDispatchSnapshot(_lastRun, _lastSubmit, _lastPicked, _lastSubmitted, _lastError, _pausedUntil);
    }
}
