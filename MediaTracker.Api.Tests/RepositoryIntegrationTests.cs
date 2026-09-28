using System.Net;
using System.Net.Http.Json;
using MediaTracker.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediaTracker.Api.Tests;

public class RepositoryIntegrationTests : IClassFixture<MediaApiFactory>
{
    private readonly MediaApiFactory _factory;

    public RepositoryIntegrationTests(MediaApiFactory factory) => _factory = factory;

    [IntegrationFact]
    public async Task The_test_host_serves_media_from_the_live_database()
    {
        TestConfiguration.OracleConnectionString();

        var client = _factory.CreateClient();
        var response = await client.GetAsync("/media");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var media = await response.Content.ReadFromJsonAsync<List<Media>>();

        Assert.NotNull(media);
    }

    [IntegrationFact]
    public void The_test_host_resolves_the_connection_string_the_loader_reads()
    {
        using var scope = _factory.Services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        Assert.Equal(
            TestConfiguration.OracleConnectionString(),
            configuration.GetConnectionString(TestConfiguration.ConnectionStringName));
    }
}
