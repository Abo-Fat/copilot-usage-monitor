namespace CopilotUsage.Core;

public sealed class RefreshPolicy
{
    public static TimeSpan Interval => TimeSpan.FromMinutes(5);
    public DateTimeOffset NextAttempt { get; private set; } = DateTimeOffset.MinValue;
    public DateTimeOffset NotBefore { get; private set; } = DateTimeOffset.MinValue;
    public int Failures { get; private set; }
    public bool AuthenticationRequired { get; private set; }

    public bool CanStart(DateTimeOffset now, bool manual) =>
        !AuthenticationRequired && now >= NotBefore && (manual || now >= NextAttempt);

    public void Success(DateTimeOffset now)
    {
        Failures = 0;
        AuthenticationRequired = false;
        NotBefore = now.AddSeconds(15);
        NextAttempt = now + Interval;
    }

    public void Fail(UsageException error, DateTimeOffset now)
    {
        Failures = Math.Min(Failures + 1, 10);
        AuthenticationRequired = !error.CanRetry;
        var delay = TimeSpan.FromSeconds(Math.Min(1800, 30 * Math.Pow(2, Failures - 1)));
        NotBefore = now + delay;
        if (error.RetryAt > NotBefore) NotBefore = error.RetryAt.Value;
        NextAttempt = NotBefore;
    }

    public void Reset()
    {
        Failures = 0;
        AuthenticationRequired = false;
        NotBefore = DateTimeOffset.MinValue;
        NextAttempt = DateTimeOffset.MinValue;
    }
}
