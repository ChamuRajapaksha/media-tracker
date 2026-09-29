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

    [Fact]
    public async Task Watching_the_final_episode_completes_the_media()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 10);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WatchStatus.Received(1).SetStatusAsync(1, "COMPLETED");
    }

    [Fact]
    public async Task Completing_a_later_season_completes_the_media_not_the_season()
    {
        _factory.Seasons.GetByIdAsync(5).Returns(new Season { SeasonId = 5, MediaId = 42, SeasonNumber = 3 });
        _factory.Episodes.GetByIdAsync(6).Returns(new Episode { EpisodeId = 6, SeasonId = 5, EpisodeNumber = 1 });
        _factory.Episodes.CountBySeasonIdAsync(5).Returns(1);
        _factory.Episodes.CountWatchedBySeasonIdAsync(5).Returns(1);

        await _client.PutAsync("/media/42/seasons/5/episodes/6/progress", null);

        // Season 3 of a show is not what the user is completing, the show is.
        await _factory.WatchStatus.Received(1).SetStatusAsync(42, "COMPLETED");
    }

    [Fact]
    public async Task Completing_a_season_reads_both_counts()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 10);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.Episodes.Received(1).CountBySeasonIdAsync(2);
        await _factory.Episodes.Received(1).CountWatchedBySeasonIdAsync(2);
    }

    [Fact]
    public async Task Completing_a_season_happens_before_the_response_is_read()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 10);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Received.InOrder(() =>
        {
            _factory.Progress.MarkWatchedAsync(3);
            _factory.WatchStatus.SetStatusAsync(1, "COMPLETED");
            _factory.Progress.GetByEpisodeIdAsync(3);
        });
    }

    [Fact]
    public async Task Watching_mid_season_leaves_the_status_alone()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 4);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Watching_the_second_to_last_episode_leaves_the_status_alone()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 9);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task An_empty_season_is_never_treated_as_complete()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 0, watched: 0);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        // Zero of zero is complete by a naive watched >= total. Dividing by it, or letting a
        // season with no episodes imported yet finish the show, would both be wrong.
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task The_episode_is_still_marked_watched_when_the_status_is_not_changed()
    {
        GivenAValidChain();
        GivenSeasonCounts(total: 10, watched: 4);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.Progress.Received(1).MarkWatchedAsync(3);
    }

    [Fact]
    public async Task Unmarking_an_episode_that_was_never_watched_returns_404()
    {
        _factory.Progress.MarkUnwatchedAsync(3).Returns(false);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unmarking_a_watched_episode_returns_no_content()
    {
        _factory.Progress.MarkUnwatchedAsync(3).Returns(true);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unmarking_does_not_change_the_watch_status()
    {
        _factory.Progress.MarkUnwatchedAsync(3).Returns(true);

        await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        // The media stays COMPLETED after an unmark, which the plan records as known Stage A
        // behaviour rather than something this endpoint silently fixes.
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Reading_season_progress_returns_the_seasons_rows()
    {
        var rows = new[]
        {
            new EpisodeProgress { EpisodeId = 3, WatchedAt = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc) },
            new EpisodeProgress { EpisodeId = 4, WatchedAt = new DateTime(2026, 9, 21, 8, 0, 0, DateTimeKind.Utc) }
        };
        _factory.Progress.GetBySeasonIdAsync(2).Returns(rows);

        var response = await _client.GetAsync("/media/1/seasons/2/progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var progress = await response.Content.ReadFromJsonAsync<List<EpisodeProgress>>();
        Assert.Equal([3, 4], progress?.Select(p => p.EpisodeId));
    }

    [Fact]
    public async Task Reading_season_progress_for_a_season_with_nothing_watched_returns_an_empty_list()
    {
        _factory.Progress.GetBySeasonIdAsync(2).Returns([]);

        var response = await _client.GetAsync("/media/1/seasons/2/progress");

        // An unwatched season is empty, not missing, so the body is [] rather than a 404.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var progress = await response.Content.ReadFromJsonAsync<List<EpisodeProgress>>();
        Assert.Empty(progress!);
    }

    private void GivenAValidChain()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns(new Episode { EpisodeId = 3, SeasonId = 2, EpisodeNumber = 1 });
    }

    private void GivenSeasonCounts(int total, int watched)
    {
        _factory.Episodes.CountBySeasonIdAsync(2).Returns(total);
        _factory.Episodes.CountWatchedBySeasonIdAsync(2).Returns(watched);
    }
}
