using System.Net;
using System.Net.Http.Json;
using Dapper;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Tests;

public class RepositoryIntegrationTests : IClassFixture<MediaApiFactory>, IAsyncLifetime
{
    private readonly MediaApiFactory _factory;
    private readonly List<int> _mediaIds = new();
    private readonly List<int> _genreIds = new();
    private IServiceScope? _scope;

    // Tags every row this test inserts so a crashed run can be cleaned up by marker
    // (database/purge-test-data.sql) instead of by guessing at ids.
    private string Marker { get; } = "ITEST-" + Guid.NewGuid().ToString("N")[..8] + "-";

    public RepositoryIntegrationTests(MediaApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Deleting the media row takes seasons, episodes, progress, genres, watch status
        // and ratings with it via ON DELETE CASCADE, so one delete per row is enough.
        foreach (var mediaId in _mediaIds)
        {
            await Media.DeleteAsync(mediaId);
        }

        // Genres are not owned by the media row, only referenced, so they go separately.
        foreach (var genreId in _genreIds)
        {
            await Genres.DeleteAsync(genreId);
        }

        _scope?.Dispose();
    }

    private IServiceProvider Services => (_scope ??= _factory.Services.CreateScope()).ServiceProvider;

    private IMediaRepository Media => Services.GetRequiredService<IMediaRepository>();
    private IGenreRepository Genres => Services.GetRequiredService<IGenreRepository>();
    private ISeasonRepository Seasons => Services.GetRequiredService<ISeasonRepository>();
    private IEpisodeRepository Episodes => Services.GetRequiredService<IEpisodeRepository>();
    private IEpisodeProgressRepository Progress => Services.GetRequiredService<IEpisodeProgressRepository>();
    private IWatchStatusRepository WatchStatus => Services.GetRequiredService<IWatchStatusRepository>();
    private IRatingRepository Ratings => Services.GetRequiredService<IRatingRepository>();

    private OracleConnection OpenConnection()
    {
        var connection = new OracleConnection(TestConfiguration.OracleConnectionString());
        connection.Open();
        return connection;
    }

    private Media Track(Media media)
    {
        _mediaIds.Add(media.MediaId);
        return media;
    }

    private async Task<Media> AddMediaAsync(string title, string mediaType = "MOVIE", int? releaseYear = null)
    {
        var media = new Media
        {
            Title = Marker + title,
            MediaType = mediaType,
            ReleaseYear = releaseYear
        };

        media.MediaId = await Media.AddAsync(media);
        return Track(media);
    }

    private async Task<Genre> AddGenreAsync(string name)
    {
        var genre = new Genre { Name = Marker + name };
        genre.GenreId = await Genres.AddAsync(genre);
        _genreIds.Add(genre.GenreId);
        return genre;
    }

    private async Task<Season> AddSeasonAsync(int mediaId, int seasonNumber, string? title = null)
    {
        var season = new Season { MediaId = mediaId, SeasonNumber = seasonNumber, Title = title };
        season.SeasonId = await Seasons.AddAsync(season);
        return season;
    }

    private async Task<Episode> AddEpisodeAsync(int seasonId, int episodeNumber, string? title = null)
    {
        var episode = new Episode { SeasonId = seasonId, EpisodeNumber = episodeNumber, Title = title };
        episode.EpisodeId = await Episodes.AddAsync(episode);
        return episode;
    }

    // Most progress tests only care about a single watched episode, so building the
    // show -> season -> episode chain inline in each of them reads worse than this does.
    private async Task<Episode> AddFirstEpisodeAsync(int mediaId)
    {
        var season = await AddSeasonAsync(mediaId, 1);
        return await AddEpisodeAsync(season.SeasonId, 1);
    }

    // Same orphan query as STEP 5 of database/purge-test-data.sql. Running it here rather
    // than trusting ON DELETE CASCADE means a dropped cascade constraint fails the suite.
    private async Task<Dictionary<string, int>> ReadOrphanCountsAsync()
    {
        const string sql = @"SELECT 'seasons' AS TableName, COUNT(*) AS Orphans
                              FROM seasons s WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = s.media_id)
                             UNION ALL
                            SELECT 'episodes', COUNT(*)
                              FROM episodes e WHERE NOT EXISTS (
                                  SELECT 1 FROM seasons s JOIN media m ON m.media_id = s.media_id
                                   WHERE s.season_id = e.season_id)
                             UNION ALL
                            SELECT 'episode_progress', COUNT(*)
                              FROM episode_progress ep WHERE NOT EXISTS (
                                  SELECT 1 FROM episodes e WHERE e.episode_id = ep.episode_id)
                             UNION ALL
                            SELECT 'media_genres', COUNT(*)
                              FROM media_genres mg WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = mg.media_id)
                             UNION ALL
                            SELECT 'watch_status', COUNT(*)
                              FROM watch_status ws WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = ws.media_id)
                             UNION ALL
                            SELECT 'ratings', COUNT(*)
                              FROM ratings r WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = r.media_id)";

        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<(string TableName, int Orphans)>(sql);

        return rows.ToDictionary(row => row.TableName, row => row.Orphans);
    }

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
        var configuration = Services.GetRequiredService<IConfiguration>();

        Assert.Equal(
            TestConfiguration.OracleConnectionString(),
            configuration.GetConnectionString(TestConfiguration.ConnectionStringName));
    }

    [IntegrationFact]
    public async Task Adding_media_returns_the_generated_identity_value()
    {
        var media = await AddMediaAsync("insert-identity");

        Assert.True(media.MediaId > 0);
        Assert.NotNull(await Media.GetByIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Each_insert_returns_a_distinct_identity_value()
    {
        var first = await AddMediaAsync("insert-identity-first");
        var second = await AddMediaAsync("insert-identity-second");

        Assert.NotEqual(first.MediaId, second.MediaId);
    }

    [IntegrationFact]
    public async Task The_inserted_row_is_committed_and_visible_to_a_fresh_connection()
    {
        var media = await AddMediaAsync("insert-committed");

        using var connection = OpenConnection();
        var title = await connection.QuerySingleAsync<string>(
            "SELECT title FROM media WHERE media_id = :Id",
            new { Id = media.MediaId });

        Assert.Equal(Marker + "insert-committed", title);
    }

    [IntegrationFact]
    public async Task Reading_media_back_maps_every_column()
    {
        var inserted = await Media.AddAsync(new Media
        {
            Title = Marker + "mapping",
            MediaType = "SHOW",
            ReleaseYear = 1999,
            Overview = Marker + "overview",
            PosterUrl = "https://example.invalid/poster.jpg",
            TmdbId = 4242
        });
        Track(new Media { MediaId = inserted });

        var media = await Media.GetByIdAsync(inserted);

        Assert.NotNull(media);
        Assert.Equal(inserted, media.MediaId);
        Assert.Equal(Marker + "mapping", media.Title);
        Assert.Equal("SHOW", media.MediaType);
        Assert.Equal(1999, media.ReleaseYear);
        Assert.Equal(Marker + "overview", media.Overview);
        Assert.Equal("https://example.invalid/poster.jpg", media.PosterUrl);
        Assert.Equal(4242, media.TmdbId);
    }

    [IntegrationFact]
    public async Task Reading_media_back_maps_the_created_at_timestamp()
    {
        using var connection = OpenConnection();
        var before = await connection.QuerySingleAsync<DateTime>("SELECT SYSDATE FROM dual");

        var inserted = await Media.AddAsync(new Media { Title = Marker + "created-at", MediaType = "MOVIE" });
        Track(new Media { MediaId = inserted });

        var media = await Media.GetByIdAsync(inserted);
        var after = await connection.QuerySingleAsync<DateTime>("SELECT SYSDATE FROM dual");

        Assert.NotNull(media);
        // created_at is SYSDATE, so the column is stamped by the database clock rather than
        // the app clock. Comparing against SYSDATE is what makes this stable on a machine
        // and container whose timezones differ. Oracle DATE also has no sub-second part.
        Assert.InRange(media.CreatedAt, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [IntegrationFact]
    public async Task Reading_media_back_maps_the_nullable_columns_as_null()
    {
        var inserted = await Media.AddAsync(new Media { Title = Marker + "nulls", MediaType = "MOVIE" });
        Track(new Media { MediaId = inserted });

        var media = await Media.GetByIdAsync(inserted);

        Assert.NotNull(media);
        Assert.Null(media.ReleaseYear);
        Assert.Null(media.Overview);
        Assert.Null(media.PosterUrl);
        Assert.Null(media.TmdbId);
    }

    [IntegrationFact]
    public async Task Reading_an_unknown_media_id_returns_null()
    {
        Assert.Null(await Media.GetByIdAsync(-1));
    }

    [IntegrationFact]
    public async Task Listing_media_includes_the_inserted_row()
    {
        var media = await AddMediaAsync("list-includes");

        var all = (await Media.GetAllAsync()).ToList();

        Assert.Contains(all, item => item.MediaId == media.MediaId);
    }

    [IntegrationFact]
    public async Task Updating_media_changes_the_editable_columns()
    {
        var media = await AddMediaAsync("update-before", "MOVIE", 2001);

        var updated = await Media.UpdateAsync(new Media
        {
            MediaId = media.MediaId,
            Title = Marker + "update-after",
            MediaType = "SHOW",
            ReleaseYear = 2025,
            Overview = Marker + "update-overview",
            PosterUrl = "https://example.invalid/updated.jpg",
            TmdbId = 777
        });

        Assert.True(updated);

        var reloaded = await Media.GetByIdAsync(media.MediaId);

        Assert.NotNull(reloaded);
        Assert.Equal(Marker + "update-after", reloaded.Title);
        Assert.Equal("SHOW", reloaded.MediaType);
        Assert.Equal(2025, reloaded.ReleaseYear);
        Assert.Equal(Marker + "update-overview", reloaded.Overview);
        Assert.Equal("https://example.invalid/updated.jpg", reloaded.PosterUrl);
        Assert.Equal(777, reloaded.TmdbId);
    }

    [IntegrationFact]
    public async Task Updating_media_leaves_created_at_alone()
    {
        var media = await AddMediaAsync("update-preserves-created-at");
        var before = (await Media.GetByIdAsync(media.MediaId))!.CreatedAt;

        await Media.UpdateAsync(new Media
        {
            MediaId = media.MediaId,
            Title = Marker + "update-preserves-created-at-renamed",
            MediaType = "MOVIE"
        });

        var after = (await Media.GetByIdAsync(media.MediaId))!.CreatedAt;

        Assert.Equal(before, after);
    }

    [IntegrationFact]
    public async Task Updating_media_does_not_rename_the_row_when_the_title_is_unchanged()
    {
        var media = await AddMediaAsync("update-idempotent");

        await Media.UpdateAsync(new Media
        {
            MediaId = media.MediaId,
            Title = Marker + "update-idempotent",
            MediaType = "MOVIE"
        });

        Assert.Equal(Marker + "update-idempotent", (await Media.GetByIdAsync(media.MediaId))!.Title);
    }

    [IntegrationFact]
    public async Task Updating_an_unknown_media_id_reports_no_rows_changed()
    {
        var updated = await Media.UpdateAsync(new Media
        {
            MediaId = -1,
            Title = Marker + "update-missing",
            MediaType = "MOVIE"
        });

        Assert.False(updated);
    }

    [IntegrationFact]
    public async Task Deleting_media_removes_the_row()
    {
        var media = await AddMediaAsync("delete-existing");

        Assert.True(await Media.DeleteAsync(media.MediaId));
        Assert.Null(await Media.GetByIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Deleting_the_same_media_twice_reports_no_rows_the_second_time()
    {
        var media = await AddMediaAsync("delete-twice");

        Assert.True(await Media.DeleteAsync(media.MediaId));
        Assert.False(await Media.DeleteAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Deleting_an_unknown_media_id_reports_no_rows_changed()
    {
        Assert.False(await Media.DeleteAsync(-1));
    }

    [IntegrationFact]
    public async Task Deleting_one_media_leaves_the_others_alone()
    {
        var deleted = await AddMediaAsync("delete-one-of-two-a");
        var kept = await AddMediaAsync("delete-one-of-two-b");

        await Media.DeleteAsync(deleted.MediaId);

        Assert.Null(await Media.GetByIdAsync(deleted.MediaId));
        Assert.NotNull(await Media.GetByIdAsync(kept.MediaId));
    }

    [IntegrationFact]
    public async Task Deleting_media_cascades_to_every_child_table()
    {
        // Builds one row in each child table so the cascade has something to remove.
        var media = await AddMediaAsync("cascade-all", "SHOW");
        var genre = await AddGenreAsync("cascade-genre");
        await Media.AddGenreAsync(media.MediaId, genre.GenreId);
        var season = await AddSeasonAsync(media.MediaId, 1, Marker + "cascade-season");
        var episode = await AddEpisodeAsync(season.SeasonId, 1, Marker + "cascade-episode");
        await Progress.MarkWatchedAsync(episode.EpisodeId);
        await WatchStatus.SetStatusAsync(media.MediaId, "WATCHING");
        await Ratings.SetRatingAsync(media.MediaId, 8.5m, Marker + "cascade-review");

        // Sanity check: the child rows are really there before the delete.
        Assert.Single(await Seasons.GetByMediaIdAsync(media.MediaId));
        Assert.Single(await Episodes.GetBySeasonIdAsync(season.SeasonId));
        Assert.NotNull(await Progress.GetByEpisodeIdAsync(episode.EpisodeId));
        Assert.Single(await Media.GetGenresForMediaAsync(media.MediaId));
        Assert.NotNull(await WatchStatus.GetByMediaIdAsync(media.MediaId));
        Assert.NotNull(await Ratings.GetByMediaIdAsync(media.MediaId));

        Assert.True(await Media.DeleteAsync(media.MediaId));

        var orphans = await ReadOrphanCountsAsync();

        Assert.All(orphans, entry => Assert.True(entry.Value == 0, $"{entry.Key} has {entry.Value} orphans"));
        // The genre row itself is shared vocabulary, not a child, so it survives on purpose.
        Assert.NotNull(await Genres.GetByIdAsync(genre.GenreId));
    }

    [IntegrationFact]
    public async Task Deleting_a_season_cascades_to_its_episodes_and_their_progress()
    {
        var media = await AddMediaAsync("cascade-season-delete", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var watched = await AddEpisodeAsync(season.SeasonId, 1);
        var unwatched = await AddEpisodeAsync(season.SeasonId, 2);
        await Progress.MarkWatchedAsync(watched.EpisodeId);

        Assert.True(await Seasons.DeleteAsync(season.SeasonId));

        Assert.Null(await Episodes.GetByIdAsync(watched.EpisodeId));
        Assert.Null(await Episodes.GetByIdAsync(unwatched.EpisodeId));
        Assert.Null(await Progress.GetByEpisodeIdAsync(watched.EpisodeId));
        Assert.Empty(await Episodes.GetBySeasonIdAsync(season.SeasonId));

        var orphans = await ReadOrphanCountsAsync();

        Assert.All(orphans, entry => Assert.True(entry.Value == 0, $"{entry.Key} has {entry.Value} orphans"));
    }

    [IntegrationFact]
    public async Task Deleting_an_episode_cascades_to_its_progress_row()
    {
        var media = await AddMediaAsync("cascade-episode-delete", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var episode = await AddEpisodeAsync(season.SeasonId, 1);
        await Progress.MarkWatchedAsync(episode.EpisodeId);

        Assert.True(await Episodes.DeleteAsync(episode.EpisodeId));

        Assert.Null(await Progress.GetByEpisodeIdAsync(episode.EpisodeId));
        Assert.Empty(await Progress.GetBySeasonIdAsync(season.SeasonId));
    }

    [IntegrationFact]
    public async Task Deleting_a_genre_removes_the_media_genres_links_but_not_the_media()
    {
        var media = await AddMediaAsync("cascade-genre-link", "MOVIE");
        var genre = await AddGenreAsync("cascade-genre-link");
        await Media.AddGenreAsync(media.MediaId, genre.GenreId);

        Assert.True(await Genres.DeleteAsync(genre.GenreId));

        Assert.Empty(await Media.GetGenresForMediaAsync(media.MediaId));
        Assert.NotNull(await Media.GetByIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Setting_watch_status_inserts_the_missing_row()
    {
        var media = await AddMediaAsync("watch-status-insert");

        await WatchStatus.SetStatusAsync(media.MediaId, "PLAN_TO_WATCH");

        var status = await WatchStatus.GetByMediaIdAsync(media.MediaId);

        Assert.NotNull(status);
        Assert.Equal(media.MediaId, status.MediaId);
        Assert.Equal("PLAN_TO_WATCH", status.Status);
    }

    [IntegrationFact]
    public async Task Setting_watch_status_again_updates_the_existing_row()
    {
        var media = await AddMediaAsync("watch-status-update");

        await WatchStatus.SetStatusAsync(media.MediaId, "PLAN_TO_WATCH");
        await WatchStatus.SetStatusAsync(media.MediaId, "WATCHING");

        var status = await WatchStatus.GetByMediaIdAsync(media.MediaId);

        Assert.NotNull(status);
        Assert.Equal("WATCHING", status.Status);
    }

    [IntegrationFact]
    public async Task Setting_watch_status_again_leaves_exactly_one_row()
    {
        var media = await AddMediaAsync("watch-status-single-row");

        await WatchStatus.SetStatusAsync(media.MediaId, "PLAN_TO_WATCH");
        await WatchStatus.SetStatusAsync(media.MediaId, "WATCHING");
        await WatchStatus.SetStatusAsync(media.MediaId, "COMPLETED");

        using var connection = OpenConnection();
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM watch_status WHERE media_id = :Id",
            new { Id = media.MediaId });

        Assert.Equal(1, rows);
    }

    [IntegrationFact]
    public async Task Setting_watch_status_again_keeps_the_same_row_identity()
    {
        var media = await AddMediaAsync("watch-status-identity");

        await WatchStatus.SetStatusAsync(media.MediaId, "WATCHING");
        var first = (await WatchStatus.GetByMediaIdAsync(media.MediaId))!;

        await WatchStatus.SetStatusAsync(media.MediaId, "COMPLETED");
        var second = (await WatchStatus.GetByMediaIdAsync(media.MediaId))!;

        Assert.Equal(first.WatchStatusId, second.WatchStatusId);
    }

    [IntegrationFact]
    public async Task Setting_watch_status_again_moves_updated_at_forward()
    {
        var media = await AddMediaAsync("watch-status-timestamp");

        await WatchStatus.SetStatusAsync(media.MediaId, "WATCHING");
        var first = (await WatchStatus.GetByMediaIdAsync(media.MediaId))!.UpdatedAt;

        // SYSDATE has a one-second resolution, so the second write needs a real gap to
        // register. A second of sleep is cheap next to a flaky assertion.
        await Task.Delay(TimeSpan.FromSeconds(1.1));
        await WatchStatus.SetStatusAsync(media.MediaId, "COMPLETED");
        var second = (await WatchStatus.GetByMediaIdAsync(media.MediaId))!.UpdatedAt;

        Assert.True(second > first, $"expected {second:O} to be later than {first:O}");
    }

    [IntegrationFact]
    public async Task Reading_watch_status_for_media_without_one_returns_null()
    {
        var media = await AddMediaAsync("watch-status-missing");

        Assert.Null(await WatchStatus.GetByMediaIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Watch_status_is_scoped_to_its_own_media_row()
    {
        var first = await AddMediaAsync("watch-status-scope-a");
        var second = await AddMediaAsync("watch-status-scope-b");

        await WatchStatus.SetStatusAsync(first.MediaId, "WATCHING");
        await WatchStatus.SetStatusAsync(second.MediaId, "COMPLETED");

        Assert.Equal("WATCHING", (await WatchStatus.GetByMediaIdAsync(first.MediaId))!.Status);
        Assert.Equal("COMPLETED", (await WatchStatus.GetByMediaIdAsync(second.MediaId))!.Status);
    }

    [IntegrationFact]
    public async Task Marking_an_episode_watched_inserts_the_missing_row()
    {
        var media = await AddMediaAsync("progress-insert", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);

        await Progress.MarkWatchedAsync(episode.EpisodeId);

        var progress = await Progress.GetByEpisodeIdAsync(episode.EpisodeId);

        Assert.NotNull(progress);
        Assert.Equal(episode.EpisodeId, progress.EpisodeId);
    }

    [IntegrationFact]
    public async Task Marking_a_watched_episode_watched_again_leaves_exactly_one_row()
    {
        var media = await AddMediaAsync("progress-idempotent", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);

        await Progress.MarkWatchedAsync(episode.EpisodeId);
        await Progress.MarkWatchedAsync(episode.EpisodeId);

        using var connection = OpenConnection();
        var rows = await connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM episode_progress WHERE episode_id = :Id",
            new { Id = episode.EpisodeId });

        Assert.Equal(1, rows);
    }

    [IntegrationFact]
    public async Task Marking_a_watched_episode_watched_again_does_not_refresh_watched_at()
    {
        // The MERGE deliberately has no WHEN MATCHED clause, so a re-watch keeps the
        // original timestamp rather than pretending the episode was watched again.
        var media = await AddMediaAsync("progress-no-refresh", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);

        await Progress.MarkWatchedAsync(episode.EpisodeId);
        var first = (await Progress.GetByEpisodeIdAsync(episode.EpisodeId))!.WatchedAt;

        await Task.Delay(TimeSpan.FromSeconds(1.1));
        await Progress.MarkWatchedAsync(episode.EpisodeId);
        var second = (await Progress.GetByEpisodeIdAsync(episode.EpisodeId))!.WatchedAt;

        Assert.Equal(first, second);
    }

    [IntegrationFact]
    public async Task Reading_season_progress_returns_only_that_seasons_episodes()
    {
        var media = await AddMediaAsync("progress-by-season", "SHOW");
        var seasonOne = await AddSeasonAsync(media.MediaId, 1);
        var seasonTwo = await AddSeasonAsync(media.MediaId, 2);
        var oneOne = await AddEpisodeAsync(seasonOne.SeasonId, 1);
        var oneTwo = await AddEpisodeAsync(seasonOne.SeasonId, 2);
        var twoOne = await AddEpisodeAsync(seasonTwo.SeasonId, 1);
        await Progress.MarkWatchedAsync(oneOne.EpisodeId);
        await Progress.MarkWatchedAsync(twoOne.EpisodeId);

        var seasonOneProgress = (await Progress.GetBySeasonIdAsync(seasonOne.SeasonId)).ToList();
        var seasonTwoProgress = (await Progress.GetBySeasonIdAsync(seasonTwo.SeasonId)).ToList();

        Assert.Equal(new[] { oneOne.EpisodeId }, seasonOneProgress.Select(row => row.EpisodeId));
        Assert.Equal(new[] { twoOne.EpisodeId }, seasonTwoProgress.Select(row => row.EpisodeId));
        Assert.DoesNotContain(oneTwo.EpisodeId, seasonOneProgress.Select(row => row.EpisodeId));
    }

    [IntegrationFact]
    public async Task Reading_season_progress_returns_the_episodes_in_number_order()
    {
        var media = await AddMediaAsync("progress-order", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var three = await AddEpisodeAsync(season.SeasonId, 3);
        var one = await AddEpisodeAsync(season.SeasonId, 1);
        var two = await AddEpisodeAsync(season.SeasonId, 2);
        await Progress.MarkWatchedAsync(three.EpisodeId);
        await Progress.MarkWatchedAsync(one.EpisodeId);
        await Progress.MarkWatchedAsync(two.EpisodeId);

        var progress = (await Progress.GetBySeasonIdAsync(season.SeasonId)).ToList();

        Assert.Equal(new[] { one.EpisodeId, two.EpisodeId, three.EpisodeId }, progress.Select(row => row.EpisodeId));
    }

    [IntegrationFact]
    public async Task Reading_progress_for_an_unwatched_episode_returns_null()
    {
        var media = await AddMediaAsync("progress-missing", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);

        Assert.Null(await Progress.GetByEpisodeIdAsync(episode.EpisodeId));
    }

    [IntegrationFact]
    public async Task Unmarking_a_watched_episode_removes_the_row()
    {
        var media = await AddMediaAsync("progress-unmark", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);
        await Progress.MarkWatchedAsync(episode.EpisodeId);

        Assert.True(await Progress.MarkUnwatchedAsync(episode.EpisodeId));
        Assert.Null(await Progress.GetByEpisodeIdAsync(episode.EpisodeId));
    }

    [IntegrationFact]
    public async Task Unmarking_an_unwatched_episode_reports_no_rows_changed()
    {
        var media = await AddMediaAsync("progress-unmark-twice", "SHOW");
        var episode = await AddFirstEpisodeAsync(media.MediaId);
        await Progress.MarkWatchedAsync(episode.EpisodeId);
        await Progress.MarkUnwatchedAsync(episode.EpisodeId);

        Assert.False(await Progress.MarkUnwatchedAsync(episode.EpisodeId));
    }

    [IntegrationFact]
    public async Task Counting_watched_episodes_matches_what_was_marked()
    {
        var media = await AddMediaAsync("progress-count", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var one = await AddEpisodeAsync(season.SeasonId, 1);
        await AddEpisodeAsync(season.SeasonId, 2);
        await AddEpisodeAsync(season.SeasonId, 3);
        await Progress.MarkWatchedAsync(one.EpisodeId);

        Assert.Equal(3, await Episodes.CountBySeasonIdAsync(season.SeasonId));
        Assert.Equal(1, await Episodes.CountWatchedBySeasonIdAsync(season.SeasonId));
    }

    [IntegrationFact]
    public async Task Upserting_a_new_season_inserts_it_and_returns_the_id()
    {
        var media = await AddMediaAsync("season-upsert-insert", "SHOW");

        var seasonId = await Seasons.UpsertAsync(new Season { MediaId = media.MediaId, SeasonNumber = 3, Title = Marker + "season-three" });

        Assert.True(seasonId > 0);
        var season = await Seasons.GetByIdAsync(seasonId);
        Assert.NotNull(season);
        Assert.Equal(media.MediaId, season.MediaId);
        Assert.Equal(3, season.SeasonNumber);
        Assert.Equal(Marker + "season-three", season.Title);
    }

    [IntegrationFact]
    public async Task Upserting_the_same_season_twice_updates_the_title_instead_of_failing()
    {
        // A plain INSERT here would raise ORA-00001 against uq_season, which is the whole
        // reason UpsertAsync exists: re-importing a show has to be safe.
        var media = await AddMediaAsync("season-upsert-update", "SHOW");

        var first = await Seasons.UpsertAsync(new Season { MediaId = media.MediaId, SeasonNumber = 1, Title = Marker + "before" });
        var second = await Seasons.UpsertAsync(new Season { MediaId = media.MediaId, SeasonNumber = 1, Title = Marker + "after" });

        Assert.Equal(first, second);
        var season = await Seasons.GetByIdAsync(second);
        Assert.NotNull(season);
        Assert.Equal(Marker + "after", season.Title);
        Assert.Single(await Seasons.GetByMediaIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Upserting_seasons_for_different_media_keeps_them_apart()
    {
        var first = await AddMediaAsync("season-upsert-scope-a", "SHOW");
        var second = await AddMediaAsync("season-upsert-scope-b", "SHOW");

        var seasonOne = await Seasons.UpsertAsync(new Season { MediaId = first.MediaId, SeasonNumber = 1, Title = Marker + "a" });
        var seasonTwo = await Seasons.UpsertAsync(new Season { MediaId = second.MediaId, SeasonNumber = 1, Title = Marker + "b" });

        Assert.NotEqual(seasonOne, seasonTwo);
        Assert.Equal(first.MediaId, (await Seasons.GetByIdAsync(seasonOne))!.MediaId);
        Assert.Equal(second.MediaId, (await Seasons.GetByIdAsync(seasonTwo))!.MediaId);
    }

    [IntegrationFact]
    public async Task Listing_seasons_returns_them_in_number_order()
    {
        var media = await AddMediaAsync("season-order", "SHOW");
        await AddSeasonAsync(media.MediaId, 2, Marker + "season-two");
        await AddSeasonAsync(media.MediaId, 1, Marker + "season-one");
        await AddSeasonAsync(media.MediaId, 3, Marker + "season-three");

        var seasons = (await Seasons.GetByMediaIdAsync(media.MediaId)).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, seasons.Select(season => season.SeasonNumber));
    }

    [IntegrationFact]
    public async Task Upserting_a_new_episode_inserts_it_and_returns_the_id()
    {
        var media = await AddMediaAsync("episode-upsert-insert", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var airDate = new DateTime(2026, 2, 14);

        var episodeId = await Episodes.UpsertAsync(new Episode
        {
            SeasonId = season.SeasonId,
            EpisodeNumber = 1,
            Title = Marker + "episode-one",
            AirDate = airDate
        });

        Assert.True(episodeId > 0);
        var episode = await Episodes.GetByIdAsync(episodeId);
        Assert.NotNull(episode);
        Assert.Equal(season.SeasonId, episode.SeasonId);
        Assert.Equal(1, episode.EpisodeNumber);
        Assert.Equal(Marker + "episode-one", episode.Title);
        Assert.Equal(airDate.Date, episode.AirDate!.Value.Date);
    }

    [IntegrationFact]
    public async Task Upserting_the_same_episode_twice_updates_it_instead_of_failing()
    {
        var media = await AddMediaAsync("episode-upsert-update", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);

        var first = await Episodes.UpsertAsync(new Episode { SeasonId = season.SeasonId, EpisodeNumber = 1, Title = Marker + "before" });
        var second = await Episodes.UpsertAsync(new Episode { SeasonId = season.SeasonId, EpisodeNumber = 1, Title = Marker + "after" });

        Assert.Equal(first, second);
        var episode = await Episodes.GetByIdAsync(second);
        Assert.NotNull(episode);
        Assert.Equal(Marker + "after", episode.Title);
        Assert.Single(await Episodes.GetBySeasonIdAsync(season.SeasonId));
    }

    [IntegrationFact]
    public async Task Upserting_the_same_episode_twice_refreshes_the_air_date()
    {
        var media = await AddMediaAsync("episode-upsert-air-date", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);

        await Episodes.UpsertAsync(new Episode { SeasonId = season.SeasonId, EpisodeNumber = 1, AirDate = new DateTime(2026, 1, 1) });
        var second = await Episodes.UpsertAsync(new Episode { SeasonId = season.SeasonId, EpisodeNumber = 1, AirDate = new DateTime(2026, 3, 3) });

        var episode = await Episodes.GetByIdAsync(second);

        Assert.NotNull(episode);
        Assert.Equal(new DateTime(2026, 3, 3), episode.AirDate!.Value.Date);
    }

    [IntegrationFact]
    public async Task Upserting_episodes_for_different_seasons_keeps_them_apart()
    {
        var media = await AddMediaAsync("episode-upsert-scope", "SHOW");
        var seasonOne = await AddSeasonAsync(media.MediaId, 1);
        var seasonTwo = await AddSeasonAsync(media.MediaId, 2);

        var episodeOne = await Episodes.UpsertAsync(new Episode { SeasonId = seasonOne.SeasonId, EpisodeNumber = 1 });
        var episodeTwo = await Episodes.UpsertAsync(new Episode { SeasonId = seasonTwo.SeasonId, EpisodeNumber = 1 });

        Assert.NotEqual(episodeOne, episodeTwo);
        Assert.Equal(seasonOne.SeasonId, (await Episodes.GetByIdAsync(episodeOne))!.SeasonId);
        Assert.Equal(seasonTwo.SeasonId, (await Episodes.GetByIdAsync(episodeTwo))!.SeasonId);
    }

    [IntegrationFact]
    public async Task Listing_episodes_returns_them_in_number_order()
    {
        var media = await AddMediaAsync("episode-order", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        await AddEpisodeAsync(season.SeasonId, 3);
        await AddEpisodeAsync(season.SeasonId, 1);
        await AddEpisodeAsync(season.SeasonId, 2);

        var episodes = (await Episodes.GetBySeasonIdAsync(season.SeasonId)).ToList();

        Assert.Equal(new[] { 1, 2, 3 }, episodes.Select(episode => episode.EpisodeNumber));
    }

    [IntegrationFact]
    public async Task Reading_a_season_or_episode_that_does_not_exist_returns_null()
    {
        Assert.Null(await Seasons.GetByIdAsync(-1));
        Assert.Null(await Episodes.GetByIdAsync(-1));
    }

    [IntegrationFact]
    public async Task Deleting_a_season_or_episode_twice_reports_no_rows_the_second_time()
    {
        var media = await AddMediaAsync("delete-child-twice", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        var episode = await AddEpisodeAsync(season.SeasonId, 1);

        Assert.True(await Episodes.DeleteAsync(episode.EpisodeId));
        Assert.False(await Episodes.DeleteAsync(episode.EpisodeId));

        Assert.True(await Seasons.DeleteAsync(season.SeasonId));
        Assert.False(await Seasons.DeleteAsync(season.SeasonId));
    }

    [IntegrationFact]
    public async Task Inserting_media_with_an_unknown_media_type_is_rejected()
    {
        var media = new Media { Title = Marker + "bad-media-type", MediaType = "BOOK" };

        var exception = await Assert.ThrowsAnyAsync<OracleException>(
            async () => await Media.AddAsync(media));

        // ORA-02290 is the check constraint violation, which is schema.sql:17 doing its job.
        Assert.Equal(2290, exception.Number);
    }

    [IntegrationFact]
    public async Task Inserting_media_without_a_title_is_rejected()
    {
        var media = new Media { Title = string.Empty, MediaType = "MOVIE" };

        await Assert.ThrowsAnyAsync<OracleException>(async () => await Media.AddAsync(media));
    }

    [IntegrationFact]
    public async Task Updating_media_to_an_unknown_media_type_is_rejected()
    {
        var media = await AddMediaAsync("update-to-bad-type", "MOVIE");

        await Assert.ThrowsAnyAsync<OracleException>(
            async () => await Media.UpdateAsync(new Media { MediaId = media.MediaId, Title = media.Title, MediaType = "BOOK" }));
    }

    [IntegrationFact]
    public async Task Setting_an_unknown_watch_status_is_rejected()
    {
        var media = await AddMediaAsync("bad-watch-status");

        await Assert.ThrowsAnyAsync<OracleException>(
            async () => await WatchStatus.SetStatusAsync(media.MediaId, "MAYBE"));

        Assert.Null(await WatchStatus.GetByMediaIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Setting_a_rating_outside_the_allowed_range_is_rejected()
    {
        var media = await AddMediaAsync("bad-rating-score");

        await Assert.ThrowsAnyAsync<OracleException>(
            async () => await Ratings.SetRatingAsync(media.MediaId, 11m, Marker + "too-high"));

        Assert.Null(await Ratings.GetByMediaIdAsync(media.MediaId));
    }

    [IntegrationFact]
    public async Task Adding_a_duplicate_genre_name_is_rejected()
    {
        var genre = await AddGenreAsync("duplicate");
        var duplicate = new Genre { Name = genre.Name };

        await Assert.ThrowsAnyAsync<OracleException>(async () => await Genres.AddAsync(duplicate));
    }

    [IntegrationFact]
    public async Task Adding_the_same_season_number_twice_for_one_media_is_rejected()
    {
        // Proves uq_season exists, which is what makes SeasonRepository.UpsertAsync necessary.
        var media = await AddMediaAsync("duplicate-season-number", "SHOW");
        await AddSeasonAsync(media.MediaId, 1);

        await Assert.ThrowsAnyAsync<OracleException>(async () => await AddSeasonAsync(media.MediaId, 1));
    }

    [IntegrationFact]
    public async Task Adding_the_same_episode_number_twice_for_one_season_is_rejected()
    {
        var media = await AddMediaAsync("duplicate-episode-number", "SHOW");
        var season = await AddSeasonAsync(media.MediaId, 1);
        await AddEpisodeAsync(season.SeasonId, 1);

        await Assert.ThrowsAnyAsync<OracleException>(async () => await AddEpisodeAsync(season.SeasonId, 1));
    }

    [IntegrationFact]
    public async Task Linking_a_genre_to_media_that_does_not_exist_is_rejected()
    {
        var genre = await AddGenreAsync("orphan-link");

        await Assert.ThrowsAnyAsync<OracleException>(async () => await Media.AddGenreAsync(-1, genre.GenreId));
    }

    [IntegrationFact]
    public async Task Linking_the_same_genre_to_media_twice_is_rejected()
    {
        var media = await AddMediaAsync("duplicate-genre-link", "MOVIE");
        var genre = await AddGenreAsync("duplicate-link");
        await Media.AddGenreAsync(media.MediaId, genre.GenreId);

        await Assert.ThrowsAnyAsync<OracleException>(async () => await Media.AddGenreAsync(media.MediaId, genre.GenreId));
    }

    [IntegrationFact]
    public async Task Marking_an_episode_watched_that_does_not_exist_is_rejected()
    {
        await Assert.ThrowsAnyAsync<OracleException>(async () => await Progress.MarkWatchedAsync(-1));
    }

    [IntegrationFact]
    public async Task A_rejected_write_does_not_stop_the_next_one()
    {
        // Oracle keeps a failed statement from poisoning later ones here only because each
        // repository opens its own connection. Worth pinning: a shared connection would
        // leave the next statement running against an aborted transaction.
        var media = await AddMediaAsync("recovery-after-rejection");

        await Assert.ThrowsAnyAsync<OracleException>(async () => await Media.AddAsync(new Media { Title = Marker + "rejected", MediaType = "BOOK" }));

        Assert.True(media.MediaId > 0);
        Assert.NotNull(await Media.GetByIdAsync(media.MediaId));
    }
}