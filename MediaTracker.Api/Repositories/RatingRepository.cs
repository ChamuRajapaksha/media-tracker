using Dapper;
using MediaTracker.Api.Models;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Repositories;

public class RatingRepository : IRatingRepository
{
    private readonly string _connectionString;

    public RatingRepository(IConfiguration config)
    {
        _connectionString = config.GetConnectionString("OracleDb")!;
    }

    public async Task<Rating?> GetByMediaIdAsync(int mediaId)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"SELECT rating_id AS RatingId, media_id AS MediaId,
                           score AS Score, review AS Review, rated_at AS RatedAt
                    FROM ratings
                    WHERE media_id = :MediaId";
        return await connection.QueryFirstOrDefaultAsync<Rating>(sql, new { MediaId = mediaId });
    }

    public async Task SetRatingAsync(int mediaId, decimal? score, string? review)
    {
        using var connection = new OracleConnection(_connectionString);
        var sql = @"MERGE INTO ratings r
                    USING (SELECT :MediaId AS media_id, :Score AS score, :Review AS review FROM dual) src
                    ON (r.media_id = src.media_id)
                    WHEN MATCHED THEN
                        UPDATE SET score = src.score, review = src.review, rated_at = SYSDATE
                    WHEN NOT MATCHED THEN
                        INSERT (media_id, score, review, rated_at)
                        VALUES (src.media_id, src.score, src.review, SYSDATE)";
        await connection.ExecuteAsync(sql, new { MediaId = mediaId, Score = score, Review = review });
    }
}