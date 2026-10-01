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
}