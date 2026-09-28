using Microsoft.Extensions.Configuration;

namespace MediaTracker.Api.Tests;

public static class TestConfiguration
{
    public const string UserSecretsId = "460cb5f5-53f5-4fe9-a334-bdce496dc898";
    public const string ConnectionStringName = "OracleDb";
    public const string ConnectionStringKey = "ConnectionStrings:" + ConnectionStringName;
    public const string TmdbApiKey = "Tmdb:ApiKey";

    public static IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets(UserSecretsId, true)
            .Build();

    public static string OracleConnectionString() =>
        RequireOracleConnectionString(BuildConfiguration());

    public static string RequireOracleConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No {ConnectionStringKey} was found in user secrets, so the tests cannot reach Oracle. "
                + "Run this from the repository root: "
                + $"dotnet user-secrets set \"{ConnectionStringKey}\" "
                + "\"User Id=media_app;Password=<password>;Data Source=localhost:1521/FREEPDB1\" "
                + "--project MediaTracker.Api");
        }

        return connectionString;
    }
}
