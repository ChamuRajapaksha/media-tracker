using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class WatchStatusRepository : IWatchStatusRepository
{
    private readonly string _connectionString;

    public WatchStatusRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<WatchStatus?> GetByMediaIdAsync(int mediaId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT watch_status_id AS WatchStatusId, media_id AS MediaId,
                           status AS Status, updated_at AS UpdatedAt
                    FROM watch_status
                    WHERE media_id = :MediaId";
        return await connection.QueryFirstOrDefaultAsync<WatchStatus>(sql, new { MediaId = mediaId });
    }

    public async Task SetStatusAsync(int mediaId, string status)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"MERGE INTO watch_status ws
                    USING (SELECT :MediaId AS media_id, :Status AS status FROM dual) src
                    ON (ws.media_id = src.media_id)
                    WHEN MATCHED THEN
                        UPDATE SET status = src.status, updated_at = SYSDATE
                    WHEN NOT MATCHED THEN
                        INSERT (media_id, status, updated_at)
                        VALUES (src.media_id, src.status, SYSDATE)";
        await connection.ExecuteAsync(sql, new { MediaId = mediaId, Status = status });
    }
}