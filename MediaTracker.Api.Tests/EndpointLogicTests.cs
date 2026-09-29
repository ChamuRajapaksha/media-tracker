using System.Net;
using System.Net.Http.Json;
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

    [Fact]
    public async Task Marking_an_episode_watched_returns_the_stored_progress_row()
    {
        GivenAValidChain();
        var watchedAt = new DateTime(2026, 9, 27, 20, 30, 0, DateTimeKind.Utc);
        _factory.Progress.GetByEpisodeIdAsync(3)
            .Returns(new EpisodeProgress { EpisodeId = 3, WatchedAt = watchedAt });

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var progress = await response.Content.ReadFromJsonAsync<EpisodeProgress>();
        Assert.NotNull(progress);
        Assert.Equal(3, progress.EpisodeId);
        Assert.Equal(watchedAt, progress.WatchedAt.ToUniversalTime());
    }

    [Fact]
    public async Task Re_watching_an_episode_still_returns_ok()
    {
        GivenAValidChain();
        var alreadyWatched = new EpisodeProgress
        {
            EpisodeId = 3,
            WatchedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        _factory.Progress.GetByEpisodeIdAsync(3).Returns(alreadyWatched);

        var first = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);
        var second = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        // EpisodeProgressRepository merges with no WHEN MATCHED clause, so a second mark is a
        // no-op at the database. The endpoint has to keep treating that as a plain success
        // rather than turning it into a conflict.
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Re_watching_never_reads_the_existing_row_before_marking()
    {
        GivenAValidChain();

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        // The endpoint does not check whether the episode was already watched, so it cannot
        // produce a different answer on a repeat and cannot skip the mark. The idempotency
        // lives entirely in the MERGE.
        await _factory.Progress.Received(1).MarkWatchedAsync(3);
        Received.InOrder(() =>
        {
            _factory.Progress.MarkWatchedAsync(3);
            _factory.Progress.GetByEpisodeIdAsync(3);
        });
    }

    private void GivenAValidChain()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns(new Episode { EpisodeId = 3, SeasonId = 2, EpisodeNumber = 1 });
    }
}
