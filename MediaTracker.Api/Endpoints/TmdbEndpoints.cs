using MediaTracker.Api.Services;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class TmdbEndpoints
{
    // TMDB needs one extra request per season, so a long-running show turns into dozens
    // of calls. This keeps a single import from becoming a very slow request.
    private const int MaxSeasonsPerImport = 25;

    public static void MapTmdbEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/tmdb");
        group.MapPost("/import", async (int tmdbId, string type, ITmdbService tmdb, IMediaRepository mediaRepo) =>
        {
            var details = await tmdb.GetDetailsAsync(tmdbId, type);
            if (details is null)
            {
                return Results.NotFound();
            }

            var dateString = details.ReleaseDate ?? details.FirstAirDate;
            int? releaseYear = null;
            if (!string.IsNullOrEmpty(dateString) && DateTime.TryParse(dateString, out var parsedDate))
            {
                releaseYear = parsedDate.Year;
            }

            var media = new Media
            {
                Title = details.Title ?? details.Name ?? "Unknown Title",
                MediaType = type == "movie" ? "MOVIE" : "SHOW",
                ReleaseYear = releaseYear,
                Overview = details.Overview,
                PosterUrl = details.PosterPath is not null ? $"https://image.tmdb.org/t/p/w500{details.PosterPath}" : null,
                TmdbId = details.Id
            };

            var newId = await mediaRepo.AddAsync(media);
            var created = await mediaRepo.GetByIdAsync(newId);
            return Results.Created($"/media/{newId}", created);
        });

        group.MapPost("/import-seasons", async (int mediaId, ITmdbService tmdb, IMediaRepository mediaRepo, ISeasonRepository seasonRepo, IEpisodeRepository episodeRepo) =>
        {
            var media = await mediaRepo.GetByIdAsync(mediaId);
            if (media is null)
            {
                return Results.NotFound();
            }

            if (media.MediaType != "SHOW")
            {
                return Results.BadRequest("Seasons can only be imported for a show.");
            }

            if (media.TmdbId is null)
            {
                return Results.BadRequest("This media item has no TMDB id. Import it from TMDB first.");
            }

            var summaries = await tmdb.GetSeasonsAsync(media.TmdbId.Value);

            // Season 0 is TMDB's "specials" bucket, not a numbered season.
            var importable = summaries
                .Where(s => s.SeasonNumber > 0)
                .OrderBy(s => s.SeasonNumber)
                .Take(MaxSeasonsPerImport)
                .ToList();

            var seasonsImported = 0;
            var episodesImported = 0;

            foreach (var summary in importable)
            {
                var detail = await tmdb.GetSeasonAsync(media.TmdbId.Value, summary.SeasonNumber);
                if (detail is null)
                {
                    continue;
                }

                var seasonId = await seasonRepo.UpsertAsync(new Season
                {
                    MediaId = mediaId,
                    SeasonNumber = detail.SeasonNumber,
                    Title = detail.Name
                });
                seasonsImported++;

                foreach (var tmdbEpisode in detail.Episodes)
                {
                    DateTime? airDate = null;
                    if (!string.IsNullOrEmpty(tmdbEpisode.AirDate) && DateTime.TryParse(tmdbEpisode.AirDate, out var parsed))
                    {
                        airDate = parsed;
                    }

                    await episodeRepo.UpsertAsync(new Episode
                    {
                        SeasonId = seasonId,
                        EpisodeNumber = tmdbEpisode.EpisodeNumber,
                        Title = tmdbEpisode.Name,
                        AirDate = airDate
                    });
                    episodesImported++;
                }
            }

            return Results.Ok(new { mediaId, seasonsImported, episodesImported });
        });
    }
}