using System.Net;
using MediaTracker.Api.Models;
using NSubstitute;

namespace MediaTracker.Api.Tests;

public class EndpointLogicTests : IClassFixture<EndpointApiFactory>
{
    private readonly EndpointApiFactory _factory;
    private readonly HttpClient _client;

    public EndpointLogicTests(EndpointApiFactory factory)
    {
        _factory = factory;
        _factory.Reset();
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Marking_watched_returns_404_when_the_season_belongs_to_another_media()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 99, SeasonNumber = 1 });

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkWatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Marking_watched_returns_404_when_the_season_does_not_exist()
    {
        _factory.Seasons.GetByIdAsync(2).Returns((Season?)null);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkWatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Marking_watched_returns_404_when_the_episode_belongs_to_another_season()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns(new Episode { EpisodeId = 3, SeasonId = 77, EpisodeNumber = 1 });

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkWatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Marking_watched_returns_404_when_the_episode_does_not_exist()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns((Episode?)null);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkWatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task A_rejected_chain_never_touches_the_watch_status()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 99, SeasonNumber = 1 });

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.WatchStatus.DidNotReceive()
            .SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }
}
