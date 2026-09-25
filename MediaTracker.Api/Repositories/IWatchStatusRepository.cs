using MediaTracker.Api.Models;

namespace MediaTracker.Api.Repositories;

public interface IWatchStatusRepository
{
    Task<WatchStatus?> GetByMediaIdAsync(int mediaId);
    Task SetStatusAsync(int mediaId, string status);
}