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

        group.MapGet("/", async (int mediaId, int seasonId, IEpisodeRepository repo, ISeasonRepository seasonRepo) =>
        {
            var season = await seasonRepo.GetByIdAsync(seasonId);
            if (season is null || season.MediaId != mediaId)
            {
                return Results.NotFound();
            }

            var episodes = await repo.GetBySeasonIdAsync(seasonId);
            return Results.Ok(episodes);
        })
        .WithSummary("List the episodes of a season")
        .Produces<List<Episode>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (int mediaId, int seasonId, EpisodeCreateRequest request, IEpisodeRepository repo, ISeasonRepository seasonRepo) =>
        {
            var season = await seasonRepo.GetByIdAsync(seasonId);
            if (season is null || season.MediaId != mediaId)
            {
                return Results.NotFound();
            }

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
        .Produces(StatusCodes.Status404NotFound)
        .ProducesValidationProblem();

        group.MapDelete("/{episodeId}", async (int mediaId, int seasonId, int episodeId, IEpisodeRepository repo) =>
        {
            var episode = await repo.GetByIdAsync(episodeId);
            if (episode is null || episode.SeasonId != seasonId)
            {
                return Results.NotFound();
            }

            var removed = await repo.DeleteAsync(episodeId);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete an episode")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}