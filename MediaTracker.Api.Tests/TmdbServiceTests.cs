using System.Net;
using System.Text.Json;
using MediaTracker.Api.Models.Tmdb;
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

    [Fact]
    public async Task SearchAsync_maps_every_field_of_a_movie_result()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"page":1,"total_results":1,"results":[
              {"id":603,"title":"The Matrix","overview":"A hacker learns the truth.",
               "poster_path":"/matrix.jpg","release_date":"1999-03-30"}
            ]}
            """);
        var service = CreateService(handler);

        var results = await service.SearchAsync("The Matrix", "movie");

        var result = Assert.Single(results);
        Assert.Equal(603, result.Id);
        Assert.Equal("The Matrix", result.Title);
        Assert.Null(result.Name);
        Assert.Equal("A hacker learns the truth.", result.Overview);
        Assert.Equal("/matrix.jpg", result.PosterPath);
        Assert.Equal("1999-03-30", result.ReleaseDate);
        Assert.Null(result.FirstAirDate);
    }

    [Fact]
    public async Task SearchAsync_maps_the_tv_field_names()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"results":[{"id":1396,"name":"Breaking Bad","first_air_date":"2008-01-20"}]}
            """);
        var service = CreateService(handler);

        var result = Assert.Single(await service.SearchAsync("Breaking Bad", "tv"));

        Assert.Equal("Breaking Bad", result.Name);
        Assert.Equal("2008-01-20", result.FirstAirDate);
        Assert.Null(result.Title);
        Assert.Null(result.ReleaseDate);
    }

    [Fact]
    public async Task SearchAsync_returns_the_results_in_the_order_tmdb_sent_them()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"results":[{"id":2},{"id":1},{"id":3}]}
            """);
        var service = CreateService(handler);

        var results = await service.SearchAsync("a", "movie");

        Assert.Equal([2, 1, 3], results.Select(r => r.Id));
    }

    [Fact]
    public async Task GetDetailsAsync_maps_a_single_result()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"id":1396,"name":"Breaking Bad","overview":"A chemistry teacher turns to meth.",
             "poster_path":"/bb.jpg","first_air_date":"2008-01-20"}
            """);
        var service = CreateService(handler);

        var result = await service.GetDetailsAsync(1396, "tv");

        Assert.NotNull(result);
        Assert.Equal(1396, result.Id);
        Assert.Equal("Breaking Bad", result.Name);
        Assert.Equal("A chemistry teacher turns to meth.", result.Overview);
        Assert.Equal("/bb.jpg", result.PosterPath);
        Assert.Equal("2008-01-20", result.FirstAirDate);
    }

    [Fact]
    public async Task GetSeasonsAsync_maps_the_season_summaries()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"id":1396,"seasons":[
              {"id":3572,"season_number":0,"name":"Specials"},
              {"id":3573,"season_number":1,"name":"Season 1"}
            ]}
            """);
        var service = CreateService(handler);

        var seasons = await service.GetSeasonsAsync(1396);

        Assert.Equal(2, seasons.Count);
        Assert.Equal(0, seasons[0].SeasonNumber);
        Assert.Equal("Specials", seasons[0].Name);
        Assert.Equal(1, seasons[1].SeasonNumber);
        Assert.Equal("Season 1", seasons[1].Name);
    }

    [Fact]
    public async Task GetSeasonAsync_maps_the_season_and_its_episodes()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"id":3573,"season_number":1,"name":"Season 1","episodes":[
              {"id":62085,"episode_number":1,"name":"Pilot","air_date":"2008-01-20"},
              {"id":62086,"episode_number":2,"name":"Cat's in the Bag...","air_date":"2008-01-27"}
            ]}
            """);
        var service = CreateService(handler);

        var season = await service.GetSeasonAsync(1396, 1);

        Assert.NotNull(season);
        Assert.Equal(1, season.SeasonNumber);
        Assert.Equal("Season 1", season.Name);
        Assert.Collection(
            season.Episodes,
            first =>
            {
                Assert.Equal(62085, first.Id);
                Assert.Equal(1, first.EpisodeNumber);
                Assert.Equal("Pilot", first.Name);
                Assert.Equal("2008-01-20", first.AirDate);
            },
            second =>
            {
                Assert.Equal(2, second.EpisodeNumber);
                Assert.Equal("Cat's in the Bag...", second.Name);
            });
    }

    [Fact]
    public async Task An_unaired_episode_keeps_its_empty_air_date_as_an_empty_string()
    {
        var handler = FakeTmdbHandler.ReturningJson("""
            {"season_number":2,"episodes":[{"id":1,"episode_number":9,"air_date":""}]}
            """);
        var service = CreateService(handler);

        var season = await service.GetSeasonAsync(1396, 2);

        Assert.Equal(string.Empty, Assert.Single(Assert.IsType<List<TmdbEpisode>>(season!.Episodes)).AirDate);
    }

    [Fact]
    public async Task Fields_tmdb_omits_are_left_null()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"id":5}""");
        var service = CreateService(handler);

        var result = await service.GetDetailsAsync(5, "movie");

        Assert.NotNull(result);
        Assert.Null(result.Title);
        Assert.Null(result.Name);
        Assert.Null(result.Overview);
        Assert.Null(result.PosterPath);
        Assert.Null(result.ReleaseDate);
        Assert.Null(result.FirstAirDate);
    }

    [Fact]
    public async Task Property_names_are_matched_whatever_their_casing()
    {
        // TmdbService sets PropertyNameCaseInsensitive, so a differently cased payload still maps.
        var handler = FakeTmdbHandler.ReturningJson("""{"Results":[{"ID":7,"Season_Number":3}]}""");
        var service = CreateService(handler);

        var result = Assert.Single(await service.SearchAsync("a", "tv"));

        Assert.Equal(7, result.Id);
    }

    [Fact]
    public async Task SearchAsync_returns_an_empty_list_for_a_null_payload()
    {
        var handler = FakeTmdbHandler.ReturningJson("null");
        var service = CreateService(handler);

        var results = await service.SearchAsync("a", "movie");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_returns_an_empty_list_when_results_is_null()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"page":1,"results":null}""");
        var service = CreateService(handler);

        var results = await service.SearchAsync("a", "movie");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetSeasonsAsync_returns_an_empty_list_for_a_null_payload()
    {
        var handler = FakeTmdbHandler.ReturningJson("null");
        var service = CreateService(handler);

        var seasons = await service.GetSeasonsAsync(1396);

        Assert.Empty(seasons);
    }

    [Fact]
    public async Task GetSeasonsAsync_returns_an_empty_list_when_seasons_is_null()
    {
        var handler = FakeTmdbHandler.ReturningJson("""{"id":1396,"seasons":null}""");
        var service = CreateService(handler);

        var seasons = await service.GetSeasonsAsync(1396);

        Assert.Empty(seasons);
    }

    [Fact]
    public async Task GetDetailsAsync_returns_null_for_a_null_payload()
    {
        var handler = FakeTmdbHandler.ReturningJson("null");
        var service = CreateService(handler);

        Assert.Null(await service.GetDetailsAsync(1396, "tv"));
    }

    [Fact]
    public async Task GetSeasonAsync_returns_null_for_a_null_payload()
    {
        var handler = FakeTmdbHandler.ReturningJson("null");
        var service = CreateService(handler);

        Assert.Null(await service.GetSeasonAsync(1396, 1));
    }

    [Fact]
    public async Task SearchAsync_throws_rather_than_returning_a_half_built_list()
    {
        var handler = FakeTmdbHandler.Failing(HttpStatusCode.NotFound);
        var service = CreateService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.SearchAsync("a", "movie"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_error_status_becomes_an_exception_not_an_empty_result(HttpStatusCode statusCode)
    {
        var handler = FakeTmdbHandler.Failing(statusCode);
        var service = CreateService(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.GetDetailsAsync(1, "movie"));
    }

    [Fact]
    public async Task An_error_status_throws_for_the_season_calls_too()
    {
        var seasonList = CreateService(FakeTmdbHandler.Failing(HttpStatusCode.ServiceUnavailable));
        var seasonDetail = CreateService(FakeTmdbHandler.Failing(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<HttpRequestException>(() => seasonList.GetSeasonsAsync(1));
        await Assert.ThrowsAsync<HttpRequestException>(() => seasonDetail.GetSeasonAsync(1, 1));
    }

    [Fact]
    public async Task A_response_body_that_is_not_json_throws()
    {
        // TMDB returns HTML from its edge when it is unhappy; that must not read as an empty result.
        var handler = FakeTmdbHandler.ReturningJson("<html>gateway timeout</html>");
        var service = CreateService(handler);

        await Assert.ThrowsAsync<JsonException>(() => service.SearchAsync("a", "movie"));
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
