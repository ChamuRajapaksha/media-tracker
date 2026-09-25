using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IEpisodeRepository
{
    Task<IEnumerable<Episode>> GetBySeasonIdAsync(int seasonId);
    Task<Episode?> GetByIdAsync(int episodeId);
    Task<int> AddAsync(Episode episode);
}