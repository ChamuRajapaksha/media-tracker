using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class EpisodeProgressRepository : IEpisodeProgressRepository
{
    private readonly string _connectionString;

    public EpisodeProgressRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<EpisodeProgress?> GetByEpisodeIdAsync(int episodeId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT episode_id AS EpisodeId, watched_at AS WatchedAt
                     FROM episode_progress
                     WHERE episode_id = :EpisodeId";
        return await connection.QueryFirstOrDefaultAsync<EpisodeProgress>(sql, new { EpisodeId = episodeId });
    }

    public async Task<IEnumerable<EpisodeProgress>> GetBySeasonIdAsync(int seasonId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT ep.episode_id AS EpisodeId, ep.watched_at AS WatchedAt
                     FROM episode_progress ep
                     JOIN episodes e ON e.episode_id = ep.episode_id
                     WHERE e.season_id = :SeasonId
                     ORDER BY e.episode_number";
        return await connection.QueryAsync<EpisodeProgress>(sql, new { SeasonId = seasonId });
    }

    public async Task MarkWatchedAsync(int episodeId)
    {
        using var connection = new OracleConnection(_connectionString);
        // No WHEN MATCHED clause: an existing row already means watched, and there is
        // nothing to update, so re-watching is a no-op rather than a timestamp refresh.
        var sql = @"MERGE INTO episode_progress ep
                    USING (SELECT :EpisodeId AS episode_id FROM dual) src
                    ON (ep.episode_id = src.episode_id)
                    WHEN NOT MATCHED THEN
                        INSERT (episode_id, watched_at)
                        VALUES (src.episode_id, SYSDATE)";
        await connection.ExecuteAsync(sql, new { EpisodeId = episodeId });
    }

    public async Task<bool> MarkUnwatchedAsync(int episodeId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"DELETE FROM episode_progress WHERE episode_id = :EpisodeId";
        var rows = await connection.ExecuteAsync(sql, new { EpisodeId = episodeId });
        return rows > 0;
    }
}
