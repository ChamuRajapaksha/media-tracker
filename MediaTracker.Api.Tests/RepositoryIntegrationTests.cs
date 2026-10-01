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
}