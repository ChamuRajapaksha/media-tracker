using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record WatchStatusRequest(string Status);

public static class WatchStatusEndpoints
{
    public static void MapWatchStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/watch-status");

        group.MapGet("/", async (int mediaId, IWatchStatusRepository repo) =>
        {
            var status = await repo.GetByMediaIdAsync(mediaId);
            return status is not null ? Results.Ok(status) : Results.NotFound();
        });

        group.MapPut("/", async (int mediaId, WatchStatusRequest request, IWatchStatusRepository repo) =>
        {
            await repo.SetStatusAsync(mediaId, request.Status);
            var updated = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(updated);
        });
    }
}