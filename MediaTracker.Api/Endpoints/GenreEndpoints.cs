using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class GenreEndpoints
{
    public static void MapGenreEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/genres").WithTags("Genres");

        group.MapGet("/", async (IGenreRepository repo) =>
        {
            var genres = await repo.GetAllAsync();
            return Results.Ok(genres);
        })
        .WithSummary("List all genres")
        .Produces<List<Genre>>(StatusCodes.Status200OK);

        group.MapGet("/{id}", async (int id, IGenreRepository repo) =>
        {
            var genre = await repo.GetByIdAsync(id);
            return genre is not null ? Results.Ok(genre) : Results.NotFound();
        })
        .WithSummary("Get a genre by id")
        .Produces<Genre>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (Genre genre, IGenreRepository repo) =>
        {
            var newId = await repo.AddAsync(genre);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/genres/{newId}", created);
        })
        .WithSummary("Create a genre")
        .Produces<Genre>(StatusCodes.Status201Created);

        group.MapDelete("/{id}", async (int id, IGenreRepository repo) =>
        {
            var removed = await repo.DeleteAsync(id);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete a genre")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
    }
}