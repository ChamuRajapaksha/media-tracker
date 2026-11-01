using System.ComponentModel.DataAnnotations;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record SeasonCreateRequest
{
    [Range(0, int.MaxValue)]
    public int SeasonNumber { get; init; }

    [StringLength(255)]
    public string? Title { get; init; }
}

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

        group.MapPost("/", async (int mediaId, SeasonCreateRequest request, ISeasonRepository repo) =>
        {
            var season = new Season
            {
                MediaId = mediaId,
                SeasonNumber = request.SeasonNumber,
                Title = request.Title
            };

            var newId = await repo.AddAsync(season);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{mediaId}/seasons/{newId}", created);
        })
        .WithSummary("Add a season to a show")
        .Produces<Season>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

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