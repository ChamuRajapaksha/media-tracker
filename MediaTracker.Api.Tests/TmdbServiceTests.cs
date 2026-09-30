using MediaTracker.Api.Services;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace MediaTracker.Api.Tests;

/// <summary>
/// TmdbService is exercised through a stubbed <see cref="HttpMessageHandler"/>, so the URL it
/// builds and the way it reads the JSON are both checked without touching the network. Every
/// test asserts on what the handler was asked for rather than on the returned object, except
/// where the mapping itself is the point.
/// </summary>
public class TmdbServiceTests : IDisposable
{
    private const string ApiKey = "test-api-key";
    private const string NoResults = """{"page":1,"results":[]}""";

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task SearchAsync_calls_the_movie_search_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson(NoResults);
        var service = CreateService(handler);

        await service.SearchAsync("The Matrix", "movie");

        Assert.Equal($"/3/search/movie?query=The%20Matrix&api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task SearchAsync_calls_the_tv_search_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson(NoResults);
        var service = CreateService(handler);

        await service.SearchAsync("Breaking Bad", "tv");

        Assert.Equal($"/3/search/tv?query=Breaking%20Bad&api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Theory]
    [InlineData("Blade Runner 2049 & more", "Blade%20Runner%202049%20%26%20more")]
    [InlineData("Søren og Co", "S%C3%B8ren%20og%20Co")]
    [InlineData("a/b?c=d", "a%2Fb%3Fc%3Dd")]
    public async Task SearchAsync_escapes_the_query_so_it_cannot_break_the_url(string query, string expected)
    {
        var handler = FakeTmdbHandler.ReturningJson(NoResults);
        var service = CreateService(handler);

        await service.SearchAsync(query, "movie");

        Assert.Equal(
            $"/3/search/movie?query={expected}&api_key={ApiKey}",
            handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task GetDetailsAsync_calls_the_movie_details_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"id":603}""");
        var service = CreateService(handler);

        await service.GetDetailsAsync(603, "movie");

        Assert.Equal($"/3/movie/603?api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task GetDetailsAsync_calls_the_tv_details_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"id":1396}""");
        var service = CreateService(handler);

        await service.GetDetailsAsync(1396, "tv");

        Assert.Equal($"/3/tv/1396?api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task GetSeasonsAsync_calls_the_tv_season_list_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"seasons":[]}""");
        var service = CreateService(handler);

        await service.GetSeasonsAsync(1396);

        Assert.Equal($"/3/tv/1396?api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task GetSeasonAsync_calls_the_tv_season_detail_endpoint()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"season_number":1}""");
        var service = CreateService(handler);

        await service.GetSeasonAsync(1396, 1);

        Assert.Equal($"/3/tv/1396/season/1?api_key={ApiKey}", handler.LastRequestPathAndQuery);
    }

    [Fact]
    public async Task Every_call_sends_the_api_key_as_a_query_parameter_only()
    {
        var handler = FakeTmdbHandler.RespondingWith(NoResults, """{"id":1}""", """{"seasons":[]}""", """{"id":2}""");
        var service = CreateService(handler);

        await service.SearchAsync("a", "movie");
        await service.GetDetailsAsync(1, "movie");
        await service.GetSeasonsAsync(1);
        await service.GetSeasonAsync(1, 0);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, uri =>
        {
            Assert.Equal(ApiKey, QueryHelpers.ParseQuery(uri.Query)["api_key"]);
            Assert.DoesNotContain(ApiKey, uri.AbsolutePath);
        });
    }

    private TmdbService CreateService(FakeTmdbHandler handler)
    {
        var client = handler.CreateClient();
        _clients.Add(client);

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Tmdb").Returns(client);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TestConfiguration.TmdbApiKey] = ApiKey
            })
            .Build();

        return new TmdbService(factory, configuration);
    }
}
