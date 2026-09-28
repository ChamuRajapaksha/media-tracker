namespace MediaTracker.Api.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string OptInVariable = "MEDIATRACKER_TEST_DB";
    public const string SkipReason =
        "Requires a live Oracle database. Start media-tracker-db, then re-run with "
        + "MEDIATRACKER_TEST_DB=1 to execute this test.";

    public IntegrationFactAttribute()
    {
        if (!IsOptedIn(Environment.GetEnvironmentVariable(OptInVariable)))
        {
            Skip = SkipReason;
        }
    }

    public static bool IsOptedIn(string? value) => value == "1";
}
