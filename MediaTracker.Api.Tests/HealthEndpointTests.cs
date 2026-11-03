using System.Net;

namespace MediaTracker.Api.Tests;

public class HealthEndpointTests : IDisposable
{
    private readonly MediaApiFactory _factory = new();
    private readonly HttpClient _client;

    public HealthEndpointTests() => _client = _factory.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Liveness_endpoint_returns_ok_without_touching_the_database()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
