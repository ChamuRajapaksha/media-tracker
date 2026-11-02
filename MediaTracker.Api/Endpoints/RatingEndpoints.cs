using System.ComponentModel.DataAnnotations;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record RatingRequest
{
    [Range(0, 10)]
    public decimal? Score { get; init; }

    [StringLength(4000)]
    public string? Review { get; init; }
}

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

        group.MapPut("/", async (int mediaId, RatingRequest request, IRatingRepository repo, IMediaRepository mediaRepo) =>
        {
            var media = await mediaRepo.GetByIdAsync(mediaId);
            if (media is null)
            {
                return Results.NotFound();
            }

            await repo.SetRatingAsync(mediaId, request.Score, request.Review);
            var updated = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(updated);
        })
        .WithSummary("Set a media item's rating")
        .WithDescription("Upserts the single rating row for this media item, including an optional review.")
        .Produces<Rating>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}