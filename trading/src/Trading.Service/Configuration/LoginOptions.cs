namespace Trading.Service.Configuration;

/// <summary>Rules for traders' passwords, logins and sessions. Development turns them off; elsewhere the defaults hold.</summary>
public sealed class LoginOptions
{
    public const string SectionName = "Login";

    /// <summary>The shortest password the admin API accepts.</summary>
    public int MinimumPasswordLength { get; init; } = 10;

    /// <summary>Login attempts per minute from one address, or 0 for no limit.</summary>
    public int AttemptsPerMinute { get; init; } = 10;

    /// <summary>How long a session lasts without being used.</summary>
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromHours(12);
}
