using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class EpisodeProgressEndpoints
{
    private const string Completed = "COMPLETED";

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

        episodeGroup.MapPut("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository progressRepo, IEpisodeRepository episodeRepo, ISeasonRepository seasonRepo, IWatchStatusRepository watchStatusRepo) =>
        {
            // Nothing in the schema ties the URL's ids together, so confirm the chain
            // media -> season -> episode before acting on it. Wrong ids in the URL would
            // otherwise mark progress against one season and update another media's status.
            var season = await seasonRepo.GetByIdAsync(seasonId);
            if (season is null || season.MediaId != mediaId)
            {
                return Results.NotFound();
            }

            var episode = await episodeRepo.GetByIdAsync(episodeId);
            if (episode is null || episode.SeasonId != seasonId)
            {
                return Results.NotFound();
            }

            await progressRepo.MarkWatchedAsync(episodeId);

            var totalEpisodes = await episodeRepo.CountBySeasonIdAsync(seasonId);
            var watchedEpisodes = await episodeRepo.CountWatchedBySeasonIdAsync(seasonId);
            if (totalEpisodes > 0 && watchedEpisodes >= totalEpisodes)
            {
                await watchStatusRepo.SetStatusAsync(season.MediaId, Completed);
            }

            var progress = await progressRepo.GetByEpisodeIdAsync(episodeId);
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
