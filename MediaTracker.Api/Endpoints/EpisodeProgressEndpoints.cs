using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class EpisodeProgressEndpoints
{
    private const string Completed = "COMPLETED";
    private const string Watching = "WATCHING";

    public static void MapEpisodeProgressEndpoints(this WebApplication app)
    {
        // Split into two groups rather than one group with a literal "progress" segment
        // alongside {episodeId}, so no route has to win on literal-vs-parameter precedence.
        var episodeGroup = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/episodes/{episodeId}/progress").WithTags("Episode Progress");

        episodeGroup.MapGet("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository repo) =>
        {
            var progress = await repo.GetByEpisodeIdAsync(episodeId);
            return progress is not null ? Results.Ok(progress) : Results.NotFound();
        })
        .WithSummary("Get an episode's watch progress")
        .Produces<EpisodeProgress>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        episodeGroup.MapPut("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository progressRepo, IEpisodeRepository episodeRepo, ISeasonRepository seasonRepo, IWatchStatusRepository watchStatusRepo) =>
        {
            // Nothing in the schema ties the URL's ids together, so confirm the chain
            // media -> season -> episode before acting on it. Wrong ids in the URL would
            // otherwise mark progress against one season and update another media's status.
            if (!await ChainExistsAsync(mediaId, seasonId, episodeId, seasonRepo, episodeRepo))
            {
                return Results.NotFound();
            }

            await progressRepo.MarkWatchedAsync(episodeId);

            var totalEpisodes = await episodeRepo.CountByMediaIdAsync(mediaId);
            var watchedEpisodes = await episodeRepo.CountWatchedByMediaIdAsync(mediaId);
            if (totalEpisodes > 0 && watchedEpisodes >= totalEpisodes)
            {
                await watchStatusRepo.SetStatusAsync(mediaId, Completed);
            }

            var progress = await progressRepo.GetByEpisodeIdAsync(episodeId);
            return Results.Ok(progress);
        })
        .WithSummary("Mark an episode watched")
        .WithDescription("Validates the media/season/episode chain from the URL, then marks the episode watched. Auto-completes the show's watch status only once every episode of every season is watched.")
        .Produces<EpisodeProgress>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        episodeGroup.MapDelete("/", async (int mediaId, int seasonId, int episodeId, IEpisodeProgressRepository repo, IEpisodeRepository episodeRepo, ISeasonRepository seasonRepo, IWatchStatusRepository watchStatusRepo) =>
        {
            if (!await ChainExistsAsync(mediaId, seasonId, episodeId, seasonRepo, episodeRepo))
            {
                return Results.NotFound();
            }

            var removed = await repo.MarkUnwatchedAsync(episodeId);
            if (!removed)
            {
                return Results.NotFound();
            }

            // Unmarking the last-watched episode of a finished show makes COMPLETED a lie.
            // Other statuses are left alone: DROPPED stays dropped, WATCHING is already right.
            var status = await watchStatusRepo.GetByMediaIdAsync(mediaId);
            if (status?.Status == Completed)
            {
                var totalEpisodes = await episodeRepo.CountByMediaIdAsync(mediaId);
                var watchedEpisodes = await episodeRepo.CountWatchedByMediaIdAsync(mediaId);
                if (watchedEpisodes < totalEpisodes)
                {
                    await watchStatusRepo.SetStatusAsync(mediaId, Watching);
                }
            }

            return Results.NoContent();
        })
        .WithSummary("Mark an episode unwatched")
        .WithDescription("Validates the media/season/episode chain from the URL, then marks the episode unwatched. Downgrades a COMPLETED show to WATCHING once an episode is unmarked.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

        var seasonGroup = app.MapGroup("/media/{mediaId}/seasons/{seasonId}/progress").WithTags("Episode Progress");

        seasonGroup.MapGet("/", async (int mediaId, int seasonId, IEpisodeProgressRepository repo) =>
        {
            var progress = await repo.GetBySeasonIdAsync(seasonId);
            return Results.Ok(progress);
        })
        .WithSummary("List watch progress for a season")
        .Produces<List<EpisodeProgress>>(StatusCodes.Status200OK);
    }

    private static async Task<bool> ChainExistsAsync(
        int mediaId,
        int seasonId,
        int episodeId,
        ISeasonRepository seasonRepo,
        IEpisodeRepository episodeRepo)
    {
        var season = await seasonRepo.GetByIdAsync(seasonId);
        if (season is null || season.MediaId != mediaId)
        {
            return false;
        }

        var episode = await episodeRepo.GetByIdAsync(episodeId);
        return episode is not null && episode.SeasonId == seasonId;
    }
}
