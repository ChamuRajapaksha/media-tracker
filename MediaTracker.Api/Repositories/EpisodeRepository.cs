using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class EpisodeRepository : IEpisodeRepository
{
    private readonly string _connectionString;

    public EpisodeRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<IEnumerable<Episode>> GetBySeasonIdAsync(int seasonId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT episode_id AS EpisodeId, season_id AS SeasonId,
                           episode_number AS EpisodeNumber, title AS Title, air_date AS AirDate
                    FROM episodes
                    WHERE season_id = :SeasonId
                    ORDER BY episode_number";
        return await connection.QueryAsync<Episode>(sql, new { SeasonId = seasonId });
    }

    public async Task<Episode?> GetByIdAsync(int episodeId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT episode_id AS EpisodeId, season_id AS SeasonId,
                           episode_number AS EpisodeNumber, title AS Title, air_date AS AirDate
                    FROM episodes
                    WHERE episode_id = :EpisodeId";
        return await connection.QueryFirstOrDefaultAsync<Episode>(sql, new { EpisodeId = episodeId });
    }

    public async Task<int> AddAsync(Episode episode)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"INSERT INTO episodes (season_id, episode_number, title, air_date)
                    VALUES (:SeasonId, :EpisodeNumber, :Title, :AirDate)
                    RETURNING episode_id INTO :NewId";

        var parameters = new DynamicParameters();
        parameters.Add("SeasonId", episode.SeasonId);
        parameters.Add("EpisodeNumber", episode.EpisodeNumber);
        parameters.Add("Title", episode.Title);
        parameters.Add("AirDate", episode.AirDate);
        parameters.Add("NewId", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(sql, parameters);
        return parameters.Get<int>("NewId");
    }
}