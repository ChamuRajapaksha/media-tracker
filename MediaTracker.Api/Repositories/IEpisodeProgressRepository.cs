using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IEpisodeProgressRepository
{
    Task<EpisodeProgress?> GetByEpisodeIdAsync(int episodeId);
    Task<IEnumerable<EpisodeProgress>> GetBySeasonIdAsync(int seasonId);
    Task MarkWatchedAsync(int episodeId);
    Task<bool> MarkUnwatchedAsync(int episodeId);
}
