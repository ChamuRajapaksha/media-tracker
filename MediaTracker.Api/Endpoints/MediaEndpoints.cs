using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public record MediaUpdateRequest(
    string Title,
    string MediaType,
    int? ReleaseYear,
    string? Overview,
    string? PosterUrl,
    int? TmdbId);

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media");

        group.MapGet("/", async (IMediaRepository repo) =>
        {
            var media = await repo.GetAllAsync();
            return Results.Ok(media);
        });

        group.MapGet("/{id}", async (int id, IMediaRepository repo) =>
        {
            var media = await repo.GetByIdAsync(id);
            return media is not null ? Results.Ok(media) : Results.NotFound();
        });

        group.MapPost("/", async (Media media, IMediaRepository repo) =>
        {
            var newId = await repo.AddAsync(media);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{newId}", created);
        });

        group.MapPost("/{id}/genres/{genreId}", async (int id, int genreId, IMediaRepository repo) =>
        {
            await repo.AddGenreAsync(id, genreId);
            return Results.NoContent();
        });

        group.MapGet("/{id}/genres", async (int id, IMediaRepository repo) =>
        {
            var genres = await repo.GetGenresForMediaAsync(id);
            return Results.Ok(genres);
        });

        group.MapDelete("/{id}", async (int id, IMediaRepository repo) =>
        {
            var removed = await repo.DeleteAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        });

        group.MapPut("/{id}", async (int id, MediaUpdateRequest request, IMediaRepository repo) =>
        {
            // A request record rather than the Media model, so media_id and created_at
            // cannot be set by the caller.
            var existing = await repo.GetByIdAsync(id);
            if (existing is null)
            {
                return Results.NotFound();
            }

            var updated = new Media
            {
                MediaId = id,
                Title = request.Title,
                MediaType = request.MediaType,
                ReleaseYear = request.ReleaseYear,
                Overview = request.Overview,
                PosterUrl = request.PosterUrl,
                TmdbId = request.TmdbId
            };

            await repo.UpdateAsync(updated);
            var result = await repo.GetByIdAsync(id);
            return Results.Ok(result);
        });
    }
}