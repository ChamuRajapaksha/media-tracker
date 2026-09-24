using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class MediaRepository : IMediaRepository
{
    private readonly string _connectionString;

    public MediaRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<IEnumerable<Media>> GetAllAsync()
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT media_id AS MediaId, title AS Title, media_type AS MediaType,
                           release_year AS ReleaseYear, overview AS Overview,
                           poster_url AS PosterUrl, tmdb_id AS TmdbId, created_at AS CreatedAt
                    FROM media";
        return await connection.QueryAsync<Media>(sql);
    }

    public async Task<Media?> GetByIdAsync(int id)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT media_id AS MediaId, title AS Title, media_type AS MediaType,
                           release_year AS ReleaseYear, overview AS Overview,
                           poster_url AS PosterUrl, tmdb_id AS TmdbId, created_at AS CreatedAt
                    FROM media WHERE media_id = :Id";
        return await connection.QueryFirstOrDefaultAsync<Media>(sql, new { Id = id });
    }

    public async Task<int> AddAsync(Media media)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"INSERT INTO media (title, media_type, release_year, overview, poster_url, tmdb_id)
                    VALUES (:Title, :MediaType, :ReleaseYear, :Overview, :PosterUrl, :TmdbId)
                    RETURNING media_id INTO :NewId";

        var parameters = new DynamicParameters();
        parameters.Add("Title", media.Title);
        parameters.Add("MediaType", media.MediaType);
        parameters.Add("ReleaseYear", media.ReleaseYear);
        parameters.Add("Overview", media.Overview);
        parameters.Add("PosterUrl", media.PosterUrl);
        parameters.Add("TmdbId", media.TmdbId);
        parameters.Add("NewId", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(sql, parameters);
        return parameters.Get<int>("NewId");
    }
}