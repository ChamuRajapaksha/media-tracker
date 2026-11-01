using System.ComponentModel.DataAnnotations;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record EpisodeCreateRequest
{
    [Range(0, int.MaxValue)]
    public int EpisodeNumber { get; init; }

    [StringLength(255)]
    public string? Title { get; init; }

    public DateOnly? AirDate { get; init; }
}

public static class EpisodeEndpoints
{
    public static void MapEpisodeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/episodes").WithTags("Episodes");

        group.MapGet("/", async (int mediaId, int seasonId, IEpisodeRepository repo) =>
        {
            var episodes = await repo.GetBySeasonIdAsync(seasonId);
            return Results.Ok(episodes);
        })
        .WithSummary("List the episodes of a season")
        .Produces<List<Episode>>(StatusCodes.Status200OK);

        group.MapPost("/", async (int mediaId, int seasonId, EpisodeCreateRequest request, IEpisodeRepository repo) =>
        {
            var episode = new Episode
            {
                SeasonId = seasonId,
                EpisodeNumber = request.EpisodeNumber,
                Title = request.Title,
                AirDate = request.AirDate?.ToDateTime(TimeOnly.MinValue)
            };

            var newId = await repo.AddAsync(episode);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{mediaId}/seasons/{seasonId}/episodes/{newId}", created);
        })
        .WithSummary("Add an episode to a season")
        .Produces<Episode>(StatusCodes.Status201Created)
        .ProducesValidationProblem();

        group.MapDelete("/{episodeId}", async (int mediaId, int seasonId, int episodeId, IEpisodeRepository repo) =>
        {
            var removed = await repo.DeleteAsync(episodeId);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete an episode")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}