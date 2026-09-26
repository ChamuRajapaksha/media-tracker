using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IEpisodeRepository
{
    Task<IEnumerable<Episode>> GetBySeasonIdAsync(int seasonId);
    Task<Episode?> GetByIdAsync(int episodeId);
    Task<int> AddAsync(Episode episode);
    Task<int> CountBySeasonIdAsync(int seasonId);
    Task<int> CountWatchedBySeasonIdAsync(int seasonId);
    Task<bool> DeleteAsync(int episodeId);
    Task<int> UpsertAsync(Episode episode);
}