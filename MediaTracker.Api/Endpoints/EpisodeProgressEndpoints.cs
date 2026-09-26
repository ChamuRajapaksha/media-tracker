using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class EpisodeProgressEndpoints
{
    public static void MapEpisodeProgressEndpoints(this WebApplication app)
    {
        // Split into two groups rather than one group with a literal "progress" segment
        // alongside {episodeId}, so no route has to win on literal-vs-parameter precedence.
        var episodeGroup = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/episodes/{episodeId}/progress");

        episodeGroup.MapGet("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository repo) =>
        {
            var progress = await repo.GetByEpisodeIdAsync(episodeId);
            return progress is not null ? Results.Ok(progress) : Results.NotFound();
        });

        episodeGroup.MapPut("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository repo) =>
        {
            await repo.MarkWatchedAsync(episodeId);
            var progress = await repo.GetByEpisodeIdAsync(episodeId);
            return Results.Ok(progress);
        });

        episodeGroup.MapDelete("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository repo) =>
        {
            var removed = await repo.MarkUnwatchedAsync(episodeId);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        var seasonGroup = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/progress");

        seasonGroup.MapGet("/", async (int mediaId, int seasonId, IEpisodeProgressRepository repo) =>
        {
            var progress = await repo.GetBySeasonIdAsync(seasonId);
            return Results.Ok(progress);
        });
    }
}
