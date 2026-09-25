using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IRatingRepository
{
    Task<Rating?> GetByMediaIdAsync(int mediaId);
    Task SetRatingAsync(int mediaId, decimal? score, string? review);
}