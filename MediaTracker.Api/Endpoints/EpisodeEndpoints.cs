using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class EpisodeEndpoints
{
    public static void MapEpisodeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/episodes");

        group.MapGet("/", async (int mediaId, int seasonId, IEpisodeRepository repo) =>
        {
            var episodes = await repo.GetBySeasonIdAsync(seasonId);
            return Results.Ok(episodes);
        });

        group.MapPost("/", async (int mediaId, int seasonId, Episode episode, IEpisodeRepository repo) =>
        {
            episode.SeasonId = seasonId;
            var newId = await repo.AddAsync(episode);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{mediaId}/seasons/{seasonId}/episodes/{newId}", created);
        });

        group.MapDelete("/{episodeId}", async (int mediaId, int seasonId, int episodeId, IEpisodeRepository repo) =>
        {
            var removed = await repo.DeleteAsync(episodeId);
            return removed ? Results.NoContent() : Results.NotFound();
        });
    }
}