using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IMediaRepository
{
    Task<IEnumerable<Media>> GetAllAsync();
    Task<Media?> GetByIdAsync(int id);
    Task<Media?> GetByTmdbIdAsync(int tmdbId, string mediaType);
    Task<int> AddAsync(Media media);
    Task AddGenreAsync(int mediaId, int genreId);
    Task<IEnumerable<Genre>> GetGenresForMediaAsync(int mediaId);
    Task<bool> DeleteAsync(int id);
    Task<bool> UpdateAsync(Media media);
}