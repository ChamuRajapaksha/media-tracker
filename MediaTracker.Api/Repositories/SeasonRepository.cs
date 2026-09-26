using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class SeasonRepository : ISeasonRepository
{
    private readonly string _connectionString;

    public SeasonRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<IEnumerable<Season>> GetByMediaIdAsync(int mediaId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT season_id AS SeasonId, media_id AS MediaId,
                           season_number AS SeasonNumber, title AS Title
                    FROM seasons
                    WHERE media_id = :MediaId
                    ORDER BY season_number";
        return await connection.QueryAsync<Season>(sql, new { MediaId = mediaId });
    }

    public async Task<Season?> GetByIdAsync(int seasonId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT season_id AS SeasonId, media_id AS MediaId,
                           season_number AS SeasonNumber, title AS Title
                    FROM seasons
                    WHERE season_id = :SeasonId";
        return await connection.QueryFirstOrDefaultAsync<Season>(sql, new { SeasonId = seasonId });
    }

    public async Task<int> AddAsync(Season season)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"INSERT INTO seasons (media_id, season_number, title)
                    VALUES (:MediaId, :SeasonNumber, :Title)
                    RETURNING season_id INTO :NewId";

        var parameters = new DynamicParameters();
        parameters.Add("MediaId", season.MediaId);
        parameters.Add("SeasonNumber", season.SeasonNumber);
        parameters.Add("Title", season.Title);
        parameters.Add("NewId", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(sql, parameters);
        return parameters.Get<int>("NewId");
    }

    public async Task<bool> DeleteAsync(int seasonId)
    {
        // Episodes and their episode_progress rows cascade from here.
        using var connection = new OracleConnection(_connectionString);
        var sql = @"DELETE FROM seasons WHERE season_id = :SeasonId";
        var rows = await connection.ExecuteAsync(sql, new { SeasonId = seasonId });
        return rows > 0;
    }

    public async Task<int> UpsertAsync(Season season)
    {
        using var connection = new OracleConnection(_connectionString);
        // Matches on uq_season so re-importing a show refreshes titles instead of
        // failing on the unique constraint the way a plain INSERT would.
        var mergeSql = @"MERGE INTO seasons s
                         USING (SELECT :MediaId AS media_id, :SeasonNumber AS season_number, :Title AS title FROM dual) src
                         ON (s.media_id = src.media_id AND s.season_number = src.season_number)
                         WHEN MATCHED THEN
                             UPDATE SET title = src.title
                         WHEN NOT MATCHED THEN
                             INSERT (media_id, season_number, title)
                             VALUES (src.media_id, src.season_number, src.title)";
        await connection.ExecuteAsync(mergeSql, new
        {
            season.MediaId,
            season.SeasonNumber,
            season.Title
        });

        // Read the id back rather than using RETURNING ... INTO: Oracle's support for a
        // RETURNING clause on MERGE varies by version, and a second round trip to a
        // uniquely-indexed row is cheap next to being wrong on someone's database.
        var selectSql = @"SELECT season_id FROM seasons
                           WHERE media_id = :MediaId AND season_number = :SeasonNumber";
        return await connection.ExecuteScalarAsync<int>(selectSql, new
        {
            season.MediaId,
            season.SeasonNumber
        });
    }
}