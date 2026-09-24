using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

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
    }
}