using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using MediaTracker.Api.Models;
using MediaTracker.Api.Models.Tmdb;
using NSubstitute;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Tests;

public class EndpointLogicTests : IDisposable
{
    // TmdbEndpoints returns an anonymous object, so the tests need a matching shape to read.
    private sealed record ImportResult(int MediaId, int SeasonsImported, int EpisodesImported);

    private readonly EndpointApiFactory _factory;
    private readonly HttpClient _client;

    public EndpointLogicTests()
    {
        _factory = new EndpointApiFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
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
        GivenShowCounts(total: 10, watched: 10);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WatchStatus.Received(1).SetStatusAsync(1, "COMPLETED");
    }

    [Fact]
    public async Task Completing_a_later_season_completes_the_media_not_the_season()
    {
        _factory.Seasons.GetByIdAsync(5).Returns(new Season { SeasonId = 5, MediaId = 42, SeasonNumber = 3 });
        _factory.Episodes.GetByIdAsync(6).Returns(new Episode { EpisodeId = 6, SeasonId = 5, EpisodeNumber = 1 });
        _factory.Episodes.CountByMediaIdAsync(42).Returns(1);
        _factory.Episodes.CountWatchedByMediaIdAsync(42).Returns(1);

        await _client.PutAsync("/media/42/seasons/5/episodes/6/progress", null);

        // Season 3 of a show is not what the user is completing, the show is.
        await _factory.WatchStatus.Received(1).SetStatusAsync(42, "COMPLETED");
    }

    [Fact]
    public async Task Completing_the_show_reads_both_whole_show_counts()
    {
        GivenAValidChain();
        GivenShowCounts(total: 10, watched: 10);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.Episodes.Received(1).CountByMediaIdAsync(1);
        await _factory.Episodes.Received(1).CountWatchedByMediaIdAsync(1);
    }

    [Fact]
    public async Task A_finished_season_does_not_complete_a_show_with_another_season_left()
    {
        GivenAValidChain();
        GivenShowCounts(total: 20, watched: 10);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        // Ten watched of twenty is every episode of this season but not of the show.
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Completing_the_show_happens_before_the_response_is_read()
    {
        GivenAValidChain();
        GivenShowCounts(total: 10, watched: 10);

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
        GivenShowCounts(total: 10, watched: 4);

        var response = await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Watching_the_second_to_last_episode_leaves_the_status_alone()
    {
        GivenAValidChain();
        GivenShowCounts(total: 10, watched: 9);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task An_empty_show_is_never_treated_as_complete()
    {
        GivenAValidChain();
        GivenShowCounts(total: 0, watched: 0);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        // Zero of zero is complete by a naive watched >= total. Dividing by it, or letting a
        // show with no episodes imported yet finish, would both be wrong.
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task The_episode_is_still_marked_watched_when_the_status_is_not_changed()
    {
        GivenAValidChain();
        GivenShowCounts(total: 10, watched: 4);

        await _client.PutAsync("/media/1/seasons/2/episodes/3/progress", null);

        await _factory.Progress.Received(1).MarkWatchedAsync(3);
    }

    [Fact]
    public async Task Unmarking_an_episode_that_was_never_watched_returns_404()
    {
        GivenAValidChain();
        _factory.Progress.MarkUnwatchedAsync(3).Returns(false);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unmarking_a_watched_episode_returns_no_content()
    {
        GivenAValidChain();
        _factory.Progress.MarkUnwatchedAsync(3).Returns(true);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unmarking_does_not_change_the_watch_status()
    {
        GivenAValidChain();
        _factory.Progress.MarkUnwatchedAsync(3).Returns(true);

        await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        // The media stays COMPLETED after an unmark, which the plan records as known Stage A
        // behaviour rather than something this endpoint silently fixes.
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Unmarking_returns_404_when_the_season_belongs_to_another_media()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 99, SeasonNumber = 1 });

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkUnwatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Unmarking_returns_404_when_the_season_does_not_exist()
    {
        _factory.Seasons.GetByIdAsync(2).Returns((Season?)null);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkUnwatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Unmarking_returns_404_when_the_episode_belongs_to_another_season()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns(new Episode { EpisodeId = 3, SeasonId = 77, EpisodeNumber = 1 });

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkUnwatchedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Unmarking_returns_404_when_the_episode_does_not_exist()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns((Episode?)null);

        var response = await _client.DeleteAsync("/media/1/seasons/2/episodes/3/progress");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Progress.DidNotReceive().MarkUnwatchedAsync(Arg.Any<int>());
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

    [Fact]
    public async Task Updating_media_maps_every_request_field_onto_the_model()
    {
        GivenExistingMedia();
        _factory.Media.UpdateAsync(Arg.Any<Media>()).Returns(true);
        GivenUpdatedMedia();

        var response = await _client.PutAsync("/media/7", UpdateRequest(
            title: "Severance (updated)",
            mediaType: "MOVIE",
            releaseYear: 2023,
            overview: "Edited overview",
            posterUrl: "https://example.test/poster.jpg",
            tmdbId: 1396));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _factory.Media.Received(1).UpdateAsync(Arg.Is<Media>(media =>
            media.MediaId == 7
            && media.Title == "Severance (updated)"
            && media.MediaType == "MOVIE"
            && media.ReleaseYear == 2023
            && media.Overview == "Edited overview"
            && media.PosterUrl == "https://example.test/poster.jpg"
            && media.TmdbId == 1396));
    }

    [Fact]
    public async Task Updating_media_keeps_the_id_from_the_url()
    {
        GivenExistingMedia();
        _factory.Media.UpdateAsync(Arg.Any<Media>()).Returns(true);
        GivenUpdatedMedia();

        await _client.PutAsync("/media/7", UpdateRequest());

        // The id comes from the route, never the body, so a request cannot move an update
        // onto a different row than the one it named.
        await _factory.Media.Received(1).UpdateAsync(Arg.Is<Media>(media => media.MediaId == 7));
    }

    [Fact]
    public async Task Updating_media_never_overwrites_created_at()
    {
        GivenExistingMedia();
        _factory.Media.UpdateAsync(Arg.Any<Media>()).Returns(true);
        GivenUpdatedMedia();

        await _client.PutAsync("/media/7", UpdateRequest());

        // MediaUpdateRequest has no CreatedAt member, so the timestamp the model is built
        // with is the default and the repository has to leave the stored column alone.
        await _factory.Media.Received(1).UpdateAsync(
            Arg.Is<Media>(media => media.CreatedAt == default));
    }

    [Fact]
    public async Task Updating_media_clears_fields_that_are_sent_as_null()
    {
        GivenExistingMedia();
        _factory.Media.UpdateAsync(Arg.Any<Media>()).Returns(true);
        GivenUpdatedMedia();

        await _client.PutAsync("/media/7", UpdateRequest(releaseYear: null, overview: null, posterUrl: null, tmdbId: null));

        await _factory.Media.Received(1).UpdateAsync(Arg.Is<Media>(media =>
            media.ReleaseYear == null
            && media.Overview == null
            && media.PosterUrl == null
            && media.TmdbId == null));
    }

    [Fact]
    public async Task Updating_media_returns_the_row_as_it_stands_afterwards()
    {
        GivenExistingMedia();
        _factory.Media.UpdateAsync(Arg.Any<Media>()).Returns(true);
        GivenUpdatedMedia();

        var response = await _client.PutAsync("/media/7", UpdateRequest());

        var body = await response.Content.ReadFromJsonAsync<Media>();
        Assert.NotNull(body);
        Assert.Equal("Severance (updated by the database)", body.Title);
        Assert.Equal(7, body.MediaId);
    }

    [Fact]
    public async Task Creating_media_maps_every_request_field_onto_the_model()
    {
        _factory.Media.AddAsync(Arg.Any<Media>()).Returns(11);
        GivenCreatedMedia(11);

        var response = await _client.PostAsync("/media", CreateRequest(
            title: "Severance",
            mediaType: "SHOW",
            releaseYear: 2022,
            overview: "A workplace thriller",
            posterUrl: "https://example.test/poster.jpg",
            tmdbId: 95396));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media =>
            media.Title == "Severance"
            && media.MediaType == "SHOW"
            && media.ReleaseYear == 2022
            && media.Overview == "A workplace thriller"
            && media.PosterUrl == "https://example.test/poster.jpg"
            && media.TmdbId == 95396));
    }

    [Fact]
    public async Task Creating_media_with_a_blank_title_returns_400()
    {
        var response = await _client.PostAsync("/media", CreateRequest(title: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Creating_media_with_an_unknown_media_type_returns_400()
    {
        var response = await _client.PostAsync("/media", CreateRequest(mediaType: "BOOK"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Creating_media_with_an_out_of_range_year_returns_400()
    {
        var response = await _client.PostAsync("/media", CreateRequest(releaseYear: 1800));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Creating_media_with_an_oversized_overview_returns_400()
    {
        var response = await _client.PostAsync("/media", CreateRequest(overview: new string('x', 4001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Updating_media_with_an_invalid_media_type_returns_400()
    {
        var response = await _client.PutAsync("/media/7", UpdateRequest(mediaType: "BOOK"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Media.DidNotReceive().UpdateAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Creating_a_genre_maps_the_name_onto_the_model()
    {
        _factory.Genres.AddAsync(Arg.Any<Genre>()).Returns(5);
        _factory.Genres.GetByIdAsync(5).Returns(new Genre { GenreId = 5, Name = "Drama" });

        var response = await _client.PostAsync("/genres", GenreRequest("Drama"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Genres.Received(1).AddAsync(Arg.Is<Genre>(genre => genre.Name == "Drama"));
    }

    [Fact]
    public async Task Creating_a_genre_with_a_blank_name_returns_400()
    {
        var response = await _client.PostAsync("/genres", GenreRequest(""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Genres.DidNotReceive().AddAsync(Arg.Any<Genre>());
    }

    [Fact]
    public async Task Creating_a_genre_with_an_oversized_name_returns_400()
    {
        var response = await _client.PostAsync("/genres", GenreRequest(new string('x', 51)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Genres.DidNotReceive().AddAsync(Arg.Any<Genre>());
    }

    [Fact]
    public async Task Linking_a_genre_returns_no_content()
    {
        GivenMediaAndGenre();

        var response = await _client.PostAsync("/media/7/genres/3", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.Media.Received(1).AddGenreAsync(7, 3);
    }

    [Fact]
    public async Task Linking_a_genre_to_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(7).Returns((Media?)null);

        var response = await _client.PostAsync("/media/7/genres/3", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Media.DidNotReceive().AddGenreAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Linking_an_unknown_genre_returns_404()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Severance", MediaType = "SHOW" });
        _factory.Genres.GetByIdAsync(3).Returns((Genre?)null);

        var response = await _client.PostAsync("/media/7/genres/3", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Media.DidNotReceive().AddGenreAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task Linking_a_genre_twice_returns_409()
    {
        GivenMediaAndGenre();
        _factory.Media.AddGenreAsync(7, 3)
            .Returns(Task.FromException(OracleExceptionWithNumber(1)));

        var response = await _client.PostAsync("/media/7/genres/3", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_season_maps_the_route_and_request_onto_the_model()
    {
        _factory.Media.GetByIdAsync(42).Returns(new Media { MediaId = 42, Title = "Severance", MediaType = "SHOW" });
        _factory.Seasons.AddAsync(Arg.Any<Season>()).Returns(8);
        _factory.Seasons.GetByIdAsync(8).Returns(new Season
        {
            SeasonId = 8,
            MediaId = 42,
            SeasonNumber = 2,
            Title = "Season 2"
        });

        var response = await _client.PostAsync("/media/42/seasons", SeasonRequest(seasonNumber: 2, title: "Season 2"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Seasons.Received(1).AddAsync(Arg.Is<Season>(season =>
            season.MediaId == 42
            && season.SeasonNumber == 2
            && season.Title == "Season 2"));
    }

    [Fact]
    public async Task Creating_a_season_with_a_negative_number_returns_400()
    {
        var response = await _client.PostAsync("/media/42/seasons", SeasonRequest(seasonNumber: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Seasons.DidNotReceive().AddAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task Creating_a_season_with_an_oversized_title_returns_400()
    {
        var response = await _client.PostAsync("/media/42/seasons", SeasonRequest(title: new string('x', 256)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Seasons.DidNotReceive().AddAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task Listing_seasons_returns_the_shows_rows()
    {
        _factory.Media.GetByIdAsync(42).Returns(new Media { MediaId = 42, Title = "Severance", MediaType = "SHOW" });
        _factory.Seasons.GetByMediaIdAsync(42).Returns(
        [
            new Season { SeasonId = 8, MediaId = 42, SeasonNumber = 1 },
            new Season { SeasonId = 9, MediaId = 42, SeasonNumber = 2 }
        ]);

        var response = await _client.GetAsync("/media/42/seasons");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seasons = await response.Content.ReadFromJsonAsync<List<Season>>();
        Assert.Equal([1, 2], seasons?.Select(season => season.SeasonNumber));
    }

    [Fact]
    public async Task Listing_seasons_for_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.GetAsync("/media/404/seasons");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().GetByMediaIdAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Listing_seasons_for_a_movie_returns_404()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Fight Club", MediaType = "MOVIE" });

        var response = await _client.GetAsync("/media/7/seasons");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().GetByMediaIdAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Creating_a_season_for_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.PostAsync("/media/404/seasons", SeasonRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().AddAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task Creating_a_season_for_a_movie_returns_404()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Fight Club", MediaType = "MOVIE" });

        var response = await _client.PostAsync("/media/7/seasons", SeasonRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().AddAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task Deleting_a_season_that_belongs_to_another_media_returns_404()
    {
        _factory.Seasons.GetByIdAsync(8).Returns(new Season { SeasonId = 8, MediaId = 99, SeasonNumber = 1 });

        var response = await _client.DeleteAsync("/media/42/seasons/8");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Deleting_an_unknown_season_returns_404()
    {
        _factory.Seasons.GetByIdAsync(8).Returns((Season?)null);

        var response = await _client.DeleteAsync("/media/42/seasons/8");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Deleting_a_season_that_belongs_to_the_media_returns_no_content()
    {
        _factory.Seasons.GetByIdAsync(8).Returns(new Season { SeasonId = 8, MediaId = 42, SeasonNumber = 1 });
        _factory.Seasons.DeleteAsync(8).Returns(true);

        var response = await _client.DeleteAsync("/media/42/seasons/8");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.Seasons.Received(1).DeleteAsync(8);
    }

    [Fact]
    public async Task Listing_episodes_returns_the_seasons_rows()
    {
        GivenASeasonFor(42, 2);
        _factory.Episodes.GetBySeasonIdAsync(2).Returns(
        [
            new Episode { EpisodeId = 9, SeasonId = 2, EpisodeNumber = 1 },
            new Episode { EpisodeId = 10, SeasonId = 2, EpisodeNumber = 2 }
        ]);

        var response = await _client.GetAsync("/media/42/seasons/2/episodes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var episodes = await response.Content.ReadFromJsonAsync<List<Episode>>();
        Assert.Equal([1, 2], episodes?.Select(episode => episode.EpisodeNumber));
    }

    [Fact]
    public async Task Listing_episodes_for_a_season_that_belongs_to_another_media_returns_404()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 99, SeasonNumber = 1 });

        var response = await _client.GetAsync("/media/42/seasons/2/episodes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().GetBySeasonIdAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Listing_episodes_for_an_unknown_season_returns_404()
    {
        _factory.Seasons.GetByIdAsync(2).Returns((Season?)null);

        var response = await _client.GetAsync("/media/42/seasons/2/episodes");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().GetBySeasonIdAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Creating_an_episode_maps_the_route_and_request_onto_the_model()
    {
        GivenASeasonFor(42, 2);
        _factory.Episodes.AddAsync(Arg.Any<Episode>()).Returns(9);
        _factory.Episodes.GetByIdAsync(9).Returns(new Episode
        {
            EpisodeId = 9,
            SeasonId = 2,
            EpisodeNumber = 3,
            Title = "Half Loop",
            AirDate = new DateTime(2022, 2, 25)
        });

        var response = await _client.PostAsync(
            "/media/42/seasons/2/episodes",
            EpisodeRequest(episodeNumber: 3, title: "Half Loop", airDate: new DateOnly(2022, 2, 25)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Episodes.Received(1).AddAsync(Arg.Is<Episode>(episode =>
            episode.SeasonId == 2
            && episode.EpisodeNumber == 3
            && episode.Title == "Half Loop"
            && episode.AirDate == new DateTime(2022, 2, 25)));
    }

    [Fact]
    public async Task Creating_an_episode_without_an_air_date_stores_null()
    {
        GivenASeasonFor(42, 2);
        _factory.Episodes.AddAsync(Arg.Any<Episode>()).Returns(9);
        _factory.Episodes.GetByIdAsync(9).Returns(new Episode { EpisodeId = 9, SeasonId = 2 });

        var response = await _client.PostAsync("/media/42/seasons/2/episodes", EpisodeRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Episodes.Received(1).AddAsync(Arg.Is<Episode>(episode => episode.AirDate == null));
    }

    [Fact]
    public async Task Creating_an_episode_with_a_negative_number_returns_400()
    {
        var response = await _client.PostAsync(
            "/media/42/seasons/2/episodes",
            EpisodeRequest(episodeNumber: -1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Episodes.DidNotReceive().AddAsync(Arg.Any<Episode>());
    }

    [Fact]
    public async Task Creating_an_episode_with_an_oversized_title_returns_400()
    {
        var response = await _client.PostAsync(
            "/media/42/seasons/2/episodes",
            EpisodeRequest(title: new string('x', 256)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Episodes.DidNotReceive().AddAsync(Arg.Any<Episode>());
    }

    [Fact]
    public async Task Creating_an_episode_for_a_season_that_belongs_to_another_media_returns_404()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 99, SeasonNumber = 1 });

        var response = await _client.PostAsync("/media/42/seasons/2/episodes", EpisodeRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().AddAsync(Arg.Any<Episode>());
    }

    [Fact]
    public async Task Creating_an_episode_for_an_unknown_season_returns_404()
    {
        _factory.Seasons.GetByIdAsync(2).Returns((Season?)null);

        var response = await _client.PostAsync("/media/42/seasons/2/episodes", EpisodeRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().AddAsync(Arg.Any<Episode>());
    }

    [Fact]
    public async Task Deleting_an_episode_that_belongs_to_another_season_returns_404()
    {
        _factory.Episodes.GetByIdAsync(9).Returns(new Episode { EpisodeId = 9, SeasonId = 77, EpisodeNumber = 1 });

        var response = await _client.DeleteAsync("/media/42/seasons/2/episodes/9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Deleting_an_unknown_episode_returns_404()
    {
        _factory.Episodes.GetByIdAsync(9).Returns((Episode?)null);

        var response = await _client.DeleteAsync("/media/42/seasons/2/episodes/9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Episodes.DidNotReceive().DeleteAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Deleting_an_episode_that_belongs_to_the_season_returns_no_content()
    {
        _factory.Episodes.GetByIdAsync(9).Returns(new Episode { EpisodeId = 9, SeasonId = 2, EpisodeNumber = 1 });
        _factory.Episodes.DeleteAsync(9).Returns(true);

        var response = await _client.DeleteAsync("/media/42/seasons/2/episodes/9");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _factory.Episodes.Received(1).DeleteAsync(9);
    }

    [Theory]
    [InlineData("PLAN_TO_WATCH")]
    [InlineData("WATCHING")]
    [InlineData("COMPLETED")]
    [InlineData("DROPPED")]
    public async Task Setting_a_known_watch_status_returns_ok(string status)
    {
        _factory.Media.GetByIdAsync(1).Returns(new Media { MediaId = 1, Title = "Existing", MediaType = "SHOW" });

        var response = await _client.PutAsync("/media/1/watch-status", WatchStatusContent(status));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.WatchStatus.Received(1).SetStatusAsync(1, status);
    }

    [Fact]
    public async Task Setting_an_unknown_watch_status_returns_400()
    {
        var response = await _client.PutAsync("/media/1/watch-status", WatchStatusContent("BINGEING"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Setting_watch_status_for_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.PutAsync("/media/404/watch-status", WatchStatusContent("WATCHING"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.WatchStatus.DidNotReceive().SetStatusAsync(Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Setting_a_rating_maps_the_score_and_review_onto_the_request()
    {
        _factory.Media.GetByIdAsync(1).Returns(new Media { MediaId = 1, Title = "Existing", MediaType = "SHOW" });

        var response = await _client.PutAsync("/media/1/rating", RatingContent(8.5m, "Excellent"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.Ratings.Received(1).SetRatingAsync(1, 8.5m, "Excellent");
    }

    [Fact]
    public async Task Setting_a_rating_without_a_score_is_allowed()
    {
        _factory.Media.GetByIdAsync(1).Returns(new Media { MediaId = 1, Title = "Existing", MediaType = "SHOW" });

        var response = await _client.PutAsync("/media/1/rating", RatingContent(null, "Not scored yet"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.Ratings.Received(1).SetRatingAsync(1, null, "Not scored yet");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(-1)]
    public async Task Setting_a_rating_outside_the_score_range_returns_400(int score)
    {
        var response = await _client.PutAsync("/media/1/rating", RatingContent(score, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Ratings.DidNotReceive()
            .SetRatingAsync(Arg.Any<int>(), Arg.Any<decimal?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Setting_a_rating_with_an_oversized_review_returns_400()
    {
        var response = await _client.PutAsync("/media/1/rating", RatingContent(8m, new string('x', 4001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Ratings.DidNotReceive()
            .SetRatingAsync(Arg.Any<int>(), Arg.Any<decimal?>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Setting_a_rating_for_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.PutAsync("/media/404/rating", RatingContent(8m, "Excellent"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Ratings.DidNotReceive()
            .SetRatingAsync(Arg.Any<int>(), Arg.Any<decimal?>(), Arg.Any<string?>());
    }

    private static StringContent RatingContent(decimal? score, string? review) => new(
        Json(new { score, review }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent WatchStatusContent(string status) => new(
        Json(new { status }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent EpisodeRequest(
        int episodeNumber = 1,
        string? title = "Pilot",
        DateOnly? airDate = null) => new(
        Json(new { episodeNumber, title, airDate }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent SeasonRequest(int seasonNumber = 1, string? title = "Season 1") => new(
        Json(new { seasonNumber, title }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent GenreRequest(string name) => new(
        Json(new { name }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent CreateRequest(
        string title = "Severance",
        string mediaType = "SHOW",
        int? releaseYear = 2022,
        string? overview = "Created from a test",
        string? posterUrl = null,
        int? tmdbId = 95396) => new(
        Json(new
        {
            title,
            mediaType,
            releaseYear,
            overview,
            posterUrl,
            tmdbId
        }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static StringContent UpdateRequest(
        string title = "Severance",
        string mediaType = "SHOW",
        int? releaseYear = 2022,
        string? overview = "Edited from a test",
        string? posterUrl = null,
        int? tmdbId = 95396) => new(
        Json(new
        {
            title,
            mediaType,
            releaseYear,
            overview,
            posterUrl,
            tmdbId
        }),
        System.Text.Encoding.UTF8,
        "application/json");

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    [Fact]
    public async Task Updating_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.PutAsync("/media/404", UpdateRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Updating_unknown_media_writes_nothing()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        await _client.PutAsync("/media/404", UpdateRequest());

        // No blind insert behind the 404: a PUT to an id that is not there has to stay a
        // no-op rather than create a second row that only looks like the one being edited.
        await _factory.Media.DidNotReceive().UpdateAsync(Arg.Any<Media>());
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Importing_a_movie_stores_the_movie_media_type()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1396, Title = "Fight Club", ReleaseDate = "1999-10-15" });

        var response = await _client.PostAsync("/tmdb/import?tmdbId=1396&type=movie", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.MediaType == "MOVIE"));
    }

    [Fact]
    public async Task Importing_a_show_stores_the_show_media_type()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 95396, Name = "Severance", FirstAirDate = "2022-02-18" });

        var response = await _client.PostAsync("/tmdb/import?tmdbId=95396&type=tv", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.MediaType == "SHOW"));
    }

    [Fact]
    public async Task The_tmdb_type_parameter_is_passed_through_unchanged()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Anything" });

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        // The service decides which TMDB path to hit, so translating here would make the
        // call and the stored media type disagree.
        await _factory.Tmdb.Received(1).GetDetailsAsync(1, "movie");
    }

    [Fact]
    public async Task Importing_with_an_unknown_media_type_returns_400()
    {
        var response = await _client.PostAsync("/tmdb/import?tmdbId=1&type=book", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Tmdb.DidNotReceive().GetDetailsAsync(Arg.Any<int>(), Arg.Any<string>());
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task Importing_copies_the_tmdb_fields_onto_the_new_media()
    {
        GivenTmdbDetails(new TmdbSearchResult
        {
            Id = 1396,
            Title = "Fight Club",
            Overview = "A ticking-time-bomb insomniac.",
            PosterPath = "/poster.jpg",
            ReleaseDate = "1999-10-15"
        });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1396&type=movie", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media =>
            media.Title == "Fight Club"
            && media.Overview == "A ticking-time-bomb insomniac."
            && media.PosterUrl == "https://image.tmdb.org/t/p/w500/poster.jpg"
            && media.ReleaseYear == 1999
            && media.TmdbId == 1396));
    }

    [Fact]
    public async Task A_show_title_is_read_from_the_name_field()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 95396, Name = "Severance", FirstAirDate = "2022-02-18" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=95396&type=tv", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.Title == "Severance"));
    }

    [Fact]
    public async Task Importing_returns_the_new_rows_location()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1396, Title = "Fight Club" });
        _factory.Media.AddAsync(Arg.Any<Media>()).Returns(11);
        GivenCreatedMedia(11);

        var response = await _client.PostAsync("/tmdb/import?tmdbId=1396&type=movie", null);

        Assert.Equal("/media/11", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task A_tmdb_id_that_resolves_to_nothing_returns_404()
    {
        _factory.Tmdb.GetDetailsAsync(404, Arg.Any<string>()).Returns((TmdbSearchResult?)null);

        var response = await _client.PostAsync("/tmdb/import?tmdbId=404&type=movie", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Media.DidNotReceive().AddAsync(Arg.Any<Media>());
    }

    [Fact]
    public async Task A_movie_with_no_release_date_stores_no_year()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Untitled" });
        GivenCreatedMedia();

        var response = await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == null));
    }

    [Fact]
    public async Task A_show_with_no_first_air_date_stores_no_year()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Name = "Untitled" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=tv", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == null));
    }

    [Fact]
    public async Task An_empty_date_string_stores_no_year()
    {
        // TMDB sends "" rather than null for an unaired episode, and an empty string is
        // truthy enough to slip past a plain null check before DateTime.TryParse throws.
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Untitled", ReleaseDate = "" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == null));
    }

    [Fact]
    public async Task A_date_that_cannot_be_parsed_stores_no_year()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Untitled", ReleaseDate = "not-a-date" });
        GivenCreatedMedia();

        var response = await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        // A malformed date is not worth failing the whole import over.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == null));
    }

    [Fact]
    public async Task A_movie_release_date_yields_its_year()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Fight Club", ReleaseDate = "1999-10-15" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == 1999));
    }

    [Fact]
    public async Task A_show_first_air_date_yields_its_year()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Name = "Severance", FirstAirDate = "2022-02-18" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=tv", null);

        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.ReleaseYear == 2022));
    }

    [Fact]
    public async Task A_missing_poster_path_stores_no_poster_url()
    {
        GivenTmdbDetails(new TmdbSearchResult { Id = 1, Title = "Untitled" });
        GivenCreatedMedia();

        await _client.PostAsync("/tmdb/import?tmdbId=1&type=movie", null);

        // Empty string rather than null would give a broken image request on the client.
        await _factory.Media.Received(1).AddAsync(Arg.Is<Media>(media => media.PosterUrl == null));
    }

    [Fact]
    public async Task Importing_seasons_for_a_movie_is_rejected()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Fight Club", MediaType = "MOVIE" });

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Tmdb.DidNotReceive().GetSeasonsAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Importing_seasons_for_a_show_with_no_tmdb_id_is_rejected()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Handmade", MediaType = "SHOW" });

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await _factory.Tmdb.DidNotReceive().GetSeasonsAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task Importing_seasons_for_unknown_media_returns_404()
    {
        _factory.Media.GetByIdAsync(404).Returns((Media?)null);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=404", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await _factory.Seasons.DidNotReceive().UpsertAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task The_media_type_guard_runs_before_the_tmdb_id_guard()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, MediaType = "MOVIE" });

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        // A movie with no TMDB id has two possible complaints, and saying the more specific
        // one first saves a user from a message about a show they never asked to import.
        var body = await response.Content.ReadFromJsonAsync<string>();
        Assert.Equal("Seasons can only be imported for a show.", body);
    }

    [Fact]
    public async Task Importing_seasons_uses_the_media_rows_tmdb_id()
    {
        GivenAnImportableShow(7, 95396);
        _factory.Tmdb.GetSeasonsAsync(95396).Returns([]);
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(1);

        await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        // Not a tmdbId from the query string: the stored one is the only trustworthy source.
        await _factory.Tmdb.Received(1).GetSeasonsAsync(95396);
    }

    [Fact]
    public async Task Season_zero_is_not_imported()
    {
        // Season 0 is the TMDB specials bucket, not a numbered season.
        GivenAnImportableShow(7, 95396);
        GivenSeasonSummaries(0, 1, 2);
        GivenSeasonDetails(1, 2);

        await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        await _factory.Tmdb.DidNotReceive().GetSeasonAsync(95396, 0);
        await _factory.Seasons.Received(2).UpsertAsync(Arg.Any<Season>());
    }

    [Fact]
    public async Task Seasons_are_imported_in_number_order()
    {
        GivenAnImportableShow(7, 95396);
        GivenSeasonSummaries(3, 1, 2);
        GivenSeasonDetails(1, 2, 3);

        await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        Received.InOrder(() =>
        {
            _factory.Tmdb.GetSeasonAsync(95396, 1);
            _factory.Tmdb.GetSeasonAsync(95396, 2);
            _factory.Tmdb.GetSeasonAsync(95396, 3);
        });
    }

    [Fact]
    public async Task A_show_with_more_than_twenty_five_seasons_stops_at_twenty_five()
    {
        GivenAnImportableShow(7, 95396);
        GivenSeasonSummaries([.. Enumerable.Range(1, 30)]);
        _factory.Tmdb.GetSeasonAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => new TmdbSeasonDetail { SeasonNumber = call.ArgAt<int>(1) });
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(1);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(25, result?.SeasonsImported);
        // TMDB needs one call per season, so an uncapped import of a long-running show turns
        // into a request that hangs for minutes.
        await _factory.Tmdb.Received(25).GetSeasonAsync(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task A_season_with_no_detail_is_skipped_without_failing_the_import()
    {
        GivenAnImportableShow(7, 95396);
        GivenSeasonSummaries(1, 2);
        _factory.Tmdb.GetSeasonAsync(95396, 1).Returns((TmdbSeasonDetail?)null);
        _factory.Tmdb.GetSeasonAsync(95396, 2)
            .Returns(new TmdbSeasonDetail { SeasonNumber = 2, Name = "Season 2" });
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(1);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(1, result?.SeasonsImported);
        await _factory.Seasons.Received(1).UpsertAsync(Arg.Is<Season>(season => season.SeasonNumber == 2));
    }

    [Fact]
    public async Task Imported_episodes_are_attached_to_the_season_they_belong_to()
    {
        GivenAnImportableShow(7, 95396);
        _factory.Tmdb.GetSeasonsAsync(95396).Returns([new TmdbSeasonSummary { Id = 1, SeasonNumber = 1 }]);
        _factory.Tmdb.GetSeasonAsync(95396, 1).Returns(new TmdbSeasonDetail
        {
            SeasonNumber = 1,
            Name = "Season 1",
            Episodes =
            [
                new TmdbEpisode { Id = 10, EpisodeNumber = 1, Name = "Good News About Hell", AirDate = "2022-02-18" },
                new TmdbEpisode { Id = 11, EpisodeNumber = 2, Name = "Half Loop", AirDate = "2022-02-25" }
            ]
        });
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(50);
        _factory.Episodes.UpsertAsync(Arg.Any<Episode>()).Returns(0);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(1, result?.SeasonsImported);
        Assert.Equal(2, result?.EpisodesImported);
        await _factory.Episodes.Received(2).UpsertAsync(Arg.Any<Episode>());
        await _factory.Episodes.Received(1).UpsertAsync(Arg.Is<Episode>(episode =>
            episode.SeasonId == 50
            && episode.Title == "Half Loop"
            && episode.EpisodeNumber == 2
            && episode.AirDate == new DateTime(2022, 2, 25)));
    }

    [Fact]
    public async Task An_episode_with_no_air_date_is_stored_with_a_null_date()
    {
        GivenAnImportableShow(7, 95396);
        _factory.Tmdb.GetSeasonsAsync(95396).Returns([new TmdbSeasonSummary { Id = 1, SeasonNumber = 1 }]);
        _factory.Tmdb.GetSeasonAsync(95396, 1).Returns(new TmdbSeasonDetail
        {
            SeasonNumber = 1,
            Episodes = [new TmdbEpisode { Id = 10, EpisodeNumber = 1, Name = "Unaired", AirDate = "" }]
        });
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(50);
        _factory.Episodes.UpsertAsync(Arg.Any<Episode>()).Returns(0);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _factory.Episodes.Received(1).UpsertAsync(Arg.Is<Episode>(episode => episode.AirDate == null));
    }

    [Fact]
    public async Task The_import_result_reports_the_media_it_touched()
    {
        GivenAnImportableShow(7, 95396);
        _factory.Tmdb.GetSeasonsAsync(95396).Returns([]);
        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(1);

        var response = await _client.PostAsync("/tmdb/import-seasons?mediaId=7", null);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        Assert.Equal(7, result?.MediaId);
        Assert.Equal(0, result?.SeasonsImported);
        Assert.Equal(0, result?.EpisodesImported);
    }

    private void GivenSeasonSummaries(params int[] seasonNumbers) =>
        _factory.Tmdb.GetSeasonsAsync(95396).Returns(
            Array.ConvertAll(seasonNumbers, n => new TmdbSeasonSummary { Id = n, SeasonNumber = n }).ToList());

    private void GivenSeasonDetails(params int[] seasonNumbers)
    {
        foreach (var seasonNumber in seasonNumbers)
        {
            _factory.Tmdb.GetSeasonAsync(95396, seasonNumber)
                .Returns(new TmdbSeasonDetail { SeasonNumber = seasonNumber, Name = $"Season {seasonNumber}" });
        }

        _factory.Seasons.UpsertAsync(Arg.Any<Season>()).Returns(1);
    }

    private void GivenAnImportableShow(int mediaId, int tmdbId) =>
        _factory.Media.GetByIdAsync(mediaId).Returns(new Media
        {
            MediaId = mediaId,
            Title = "Severance",
            MediaType = "SHOW",
            TmdbId = tmdbId
        });

    private void GivenTmdbDetails(TmdbSearchResult details) =>
        _factory.Tmdb.GetDetailsAsync(details.Id, Arg.Any<string>()).Returns(details);

    private void GivenCreatedMedia(int mediaId = 1) =>
        _factory.Media.GetByIdAsync(mediaId).Returns(new Media { MediaId = mediaId, Title = "Created" });

    private void GivenMediaAndGenre()
    {
        _factory.Media.GetByIdAsync(7).Returns(new Media { MediaId = 7, Title = "Severance", MediaType = "SHOW" });
        _factory.Genres.GetByIdAsync(3).Returns(new Genre { GenreId = 3, Name = "Drama" });
    }

    private static OracleException OracleExceptionWithNumber(int number)
    {
        var constructor = typeof(OracleException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(int), typeof(string), typeof(string), typeof(string), typeof(int)],
            modifiers: null)!;
        return (OracleException)constructor.Invoke([number, "ORA test", "test", "test", 1]);
    }

    private void GivenExistingMedia() =>
        _factory.Media.GetByIdAsync(7).Returns(new Media
        {
            MediaId = 7,
            Title = "Severance",
            MediaType = "SHOW",
            ReleaseYear = 2022,
            CreatedAt = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        });

    private void GivenUpdatedMedia() =>
        _factory.Media.GetByIdAsync(7).Returns(new Media
        {
            MediaId = 7,
            Title = "Severance (updated by the database)",
            MediaType = "SHOW",
            ReleaseYear = 2022
        });

    private void GivenAValidChain()
    {
        _factory.Seasons.GetByIdAsync(2).Returns(new Season { SeasonId = 2, MediaId = 1, SeasonNumber = 1 });
        _factory.Episodes.GetByIdAsync(3).Returns(new Episode { EpisodeId = 3, SeasonId = 2, EpisodeNumber = 1 });
    }

    private void GivenASeasonFor(int mediaId, int seasonId) =>
        _factory.Seasons.GetByIdAsync(seasonId)
            .Returns(new Season { SeasonId = seasonId, MediaId = mediaId, SeasonNumber = 1 });

    private void GivenShowCounts(int total, int watched)
    {
        _factory.Episodes.CountByMediaIdAsync(1).Returns(total);
        _factory.Episodes.CountWatchedByMediaIdAsync(1).Returns(watched);
    }
}
