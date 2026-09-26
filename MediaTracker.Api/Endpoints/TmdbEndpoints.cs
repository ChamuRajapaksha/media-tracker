using MediaTracker.Api.Services;
using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class TmdbEndpoints
{
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
    }
}