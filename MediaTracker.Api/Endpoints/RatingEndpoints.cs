using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record RatingRequest(decimal? Score, string? Review);

public static class RatingEndpoints
{
    public static void MapRatingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/rating").WithTags("Ratings");

        group.MapGet("/", async (int mediaId, IRatingRepository repo) =>
        {
            var rating = await repo.GetByMediaIdAsync(mediaId);
            return rating is not null ? Results.Ok(rating) : Results.NotFound();
        })
        .WithSummary("Get a media item's rating")
        .Produces<Rating>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/", async (int mediaId, RatingRequest request, IRatingRepository repo) =>
        {
            await repo.SetRatingAsync(mediaId, request.Score, request.Review);
            var updated = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(updated);
        })
        .WithSummary("Set a media item's rating")
        .WithDescription("Upserts the single rating row for this media item, including an optional review.")
        .Produces<Rating>(StatusCodes.Status200OK);
    }
}