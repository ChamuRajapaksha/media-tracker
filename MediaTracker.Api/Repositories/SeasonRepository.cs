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
}