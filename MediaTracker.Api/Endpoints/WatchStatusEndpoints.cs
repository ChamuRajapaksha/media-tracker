using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record WatchStatusRequest(string Status);

public static class WatchStatusEndpoints
{
    public static void MapWatchStatusEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/watch-status").WithTags("Watch Status");

        group.MapGet("/", async (int mediaId, IWatchStatusRepository repo) =>
        {
            var status = await repo.GetByMediaIdAsync(mediaId);
            return status is not null ? Results.Ok(status) : Results.NotFound();
        })
        .WithSummary("Get a media item's watch status")
        .Produces<WatchStatus>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/", async (int mediaId, WatchStatusRequest request, IWatchStatusRepository repo) =>
        {
            await repo.SetStatusAsync(mediaId, request.Status);
            var updated = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(updated);
        })
        .WithSummary("Set a media item's watch status")
        .WithDescription("Upserts the single watch status row for this media item.")
        .Produces<WatchStatus>(StatusCodes.Status200OK);
    }
}