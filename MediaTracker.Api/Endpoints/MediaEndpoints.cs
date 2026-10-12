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
        var group = app.MapGroup("/media").WithTags("Media");

        group.MapGet("/", async (IMediaRepository repo) =>
        {
            var media = await repo.GetAllAsync();
            return Results.Ok(media);
        })
        .WithSummary("List all media")
        .WithDescription("Returns every movie and show in the library.")
        .Produces<List<Media>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (int id, IMediaRepository repo) =>
        {
            var media = await repo.GetByIdAsync(id);
            return media is not null ? Results.Ok(media) : Results.NotFound();
        })
        .WithSummary("Get a media item by id")
        .Produces<Media>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (Media media, IMediaRepository repo) =>
        {
            var newId = await repo.AddAsync(media);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{newId}", created);
        })
        .WithSummary("Create a media item")
        .Produces<Media>(StatusCodes.Status201Created);

        group.MapPost("/{id}/genres/{genreId}", async (int id, int genreId, IMediaRepository repo) =>
        {
            await repo.AddGenreAsync(id, genreId);
            return Results.NoContent();
        })
        .WithSummary("Link a genre to a media item")
        .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/{id}/genres", async (int id, IMediaRepository repo) =>
        {
            var genres = await repo.GetGenresForMediaAsync(id);
            return Results.Ok(genres);
        })
        .WithSummary("List the genres of a media item")
        .Produces<List<Genre>>(StatusCodes.Status200OK);

        group.MapDelete("/{id}", async (int id, IMediaRepository repo) =>
        {
            var removed = await repo.DeleteAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete a media item")
        .WithDescription("Deleting a media item cascades to its seasons, episodes, genres links, watch status, and rating.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);

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
        })
        .WithSummary("Update a media item's own fields")
        .WithDescription("Updates title, type, release year, overview, poster, and TMDB id. Does not change watch status or rating.")
        .Produces<Media>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}