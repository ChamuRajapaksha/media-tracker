using MediaTracker.Api.Models.Tmdb;

namespace MediaTracker.Api.Services;

public interface ITmdbService
{
    Task<List<TmdbSearchResult>> SearchAsync(string query, string mediaType);
    Task<TmdbSearchResult?> GetDetailsAsync(int tmdbId, string mediaType);
    Task<List<TmdbSeasonSummary>> GetSeasonsAsync(int tmdbId);
    Task<TmdbSeasonDetail?> GetSeasonAsync(int tmdbId, int seasonNumber);
}