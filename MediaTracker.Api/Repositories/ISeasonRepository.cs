using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface ISeasonRepository
{
    Task<IEnumerable<Season>> GetByMediaIdAsync(int mediaId);
    Task<Season?> GetByIdAsync(int seasonId);
    Task<int> AddAsync(Season season);
}