using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IMediaRepository
{
    Task<IEnumerable<Media>> GetAllAsync();
    Task<Media?> GetByIdAsync(int id);
    Task<int> AddAsync(Media media);
}