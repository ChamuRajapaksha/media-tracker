using MediaTracker.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MediaTracker.Api.Tests;

public class OracleHealthCheckTests
{
    [Fact]
    public async Task Missing_connection_string_reports_unhealthy_without_connecting()
    {
        var check = new OracleHealthCheck(BuildConfiguration(null));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Unreachable_database_reports_unhealthy()
    {
        const string unreachable =
            "User Id=media_app;Password=wrong;Connection Timeout=1;"
            + "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=127.0.0.1)(PORT=1))"
            + "(CONNECT_DATA=(SERVICE_NAME=FREEPDB1)))";
        var check = new OracleHealthCheck(BuildConfiguration(unreachable));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [IntegrationFact]
    public async Task Live_database_reports_healthy()
    {
        var check = new OracleHealthCheck(BuildConfiguration(TestConfiguration.OracleConnectionString()));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static IConfiguration BuildConfiguration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OracleDb"] = connectionString
            })
            .Build();
}
