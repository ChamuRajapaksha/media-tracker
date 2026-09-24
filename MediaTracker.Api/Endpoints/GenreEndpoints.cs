using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class GenreEndpoints
{
    public static void MapGenreEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/genres");

        group.MapGet("/", async (IGenreRepository repo) =>
        {
            var genres = await repo.GetAllAsync();
            return Results.Ok(genres);
        });

        group.MapGet("/{id}", async (int id, IGenreRepository repo) =>
        {
            var genre = await repo.GetByIdAsync(id);
            return genre is not null ? Results.Ok(genre) : Results.NotFound();
        });

        group.MapPost("/", async (Genre genre, IGenreRepository repo) =>
        {
            var newId = await repo.AddAsync(genre);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/genres/{newId}", created);
        });
    }
}