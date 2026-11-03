using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Infrastructure;

public sealed class OracleHealthCheck : IHealthCheck
{
    private readonly string? _connectionString;

    public OracleHealthCheck(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("OracleDb");
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            return HealthCheckResult.Unhealthy("The Oracle connection string is not configured.");
        }

        try
        {
            await using var connection = new OracleConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>("SELECT 1 FROM dual");
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Oracle is not reachable.", exception);
        }
    }
}
