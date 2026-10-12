using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class SeasonEndpoints
{
    public static void MapSeasonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/seasons").WithTags("Seasons");

        group.MapGet("/", async (int mediaId, ISeasonRepository repo) =>
        {
            var seasons = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(seasons);
        })
        .WithSummary("List the seasons of a show")
        .Produces<List<Season>>(StatusCodes.Status200OK);

        group.MapPost("/", async (int mediaId, Season season, ISeasonRepository repo) =>
        {
            season.MediaId = mediaId;
            var newId = await repo.AddAsync(season);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{mediaId}/seasons/{newId}", created);
        })
        .WithSummary("Add a season to a show")
        .Produces<Season>(StatusCodes.Status201Created);

        group.MapDelete("/{seasonId}", async (int mediaId, int seasonId, ISeasonRepository repo) =>
        {
            var removed = await repo.DeleteAsync(seasonId);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete a season")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}