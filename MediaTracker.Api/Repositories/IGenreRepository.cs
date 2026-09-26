using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;
public interface IGenreRepository
{
    Task<IEnumerable<Genre>> GetAllAsync();
    Task<Genre?> GetByIdAsync(int id);
    Task<int> AddAsync(Genre genre);
    Task<bool> DeleteAsync(int id);
}