using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class GenreRepository : IGenreRepository
{
    private readonly string _connectionString;

    public GenreRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<IEnumerable<Genre>> GetAllAsync()
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT genre_id AS GenreId, name AS Name
                    FROM genres";
        return await connection.QueryAsync<Genre>(sql);
    }

    public async Task<Genre?> GetByIdAsync(int id)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT genre_id AS GenreId, name AS Name
                    FROM genres
                    WHERE genre_id = :id";
        return await connection.QueryFirstOrDefaultAsync<Genre>(sql, new { id });
    }

    public async Task<int> AddAsync(Genre genre)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"INSERT INTO genres (name)
                    VALUES (:Name)
                    RETURNING genre_id INTO :NewId";

        var parameters = new DynamicParameters();
        parameters.Add("Name", genre.Name);
        parameters.Add("NewId", dbType: System.Data.DbType.Int32, direction: System.Data.ParameterDirection.Output);

        await connection.ExecuteAsync(sql, parameters);
        return parameters.Get<int>("NewId");
    }
}