using Microsoft.Extensions.Configuration;

namespace MediaTracker.Api.Tests;

public sealed class TestConfigurationTests
{
    private const string SampleConnectionString =
        "User Id=media_app;Password=not-a-real-one;Data Source=localhost:1521/FREEPDB1";

    [Fact]
    public void A_configured_connection_string_is_returned_unchanged()
    {
        var configuration = Configuration(("ConnectionStrings:OracleDb", SampleConnectionString));

        Assert.Equal(SampleConnectionString, TestConfiguration.RequireOracleConnectionString(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_connection_string_is_never_silently_accepted(string? value)
    {
        var configuration = value is null
            ? new ConfigurationBuilder().Build()
            : Configuration(("ConnectionStrings:OracleDb", value));

        Assert.Throws<InvalidOperationException>(
            () => TestConfiguration.RequireOracleConnectionString(configuration));
    }

    [Fact]
    public void An_unrelated_secret_does_not_satisfy_the_loader()
    {
        var configuration = Configuration((TestConfiguration.TmdbApiKey, "abc123"));

        Assert.Throws<InvalidOperationException>(
            () => TestConfiguration.RequireOracleConnectionString(configuration));
    }

    [Fact]
    public void The_failure_message_carries_the_command_needed_to_fix_it()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => TestConfiguration.RequireOracleConnectionString(new ConfigurationBuilder().Build()));

        Assert.Contains("dotnet user-secrets set", failure.Message);
        Assert.Contains("ConnectionStrings:OracleDb", failure.Message);
        Assert.Contains("--project MediaTracker.Api", failure.Message);
        Assert.Contains("localhost:1521/FREEPDB1", failure.Message);
    }

    private static IConfigurationRoot Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(value => new KeyValuePair<string, string?>(value.Key, value.Value)))
            .Build();
}
