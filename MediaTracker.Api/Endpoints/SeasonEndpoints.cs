using MediaTracker.Api.Models;
using MediaTracker.Api.Repositories;

namespace MediaTracker.Api.Endpoints;

public static class SeasonEndpoints
{
    public static void MapSeasonEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/media/{mediaId}/seasons");

        group.MapGet("/", async (int mediaId, ISeasonRepository repo) =>
        {
            var seasons = await repo.GetByMediaIdAsync(mediaId);
            return Results.Ok(seasons);
        });

        group.MapPost("/", async (int mediaId, Season season, ISeasonRepository repo) =>
        {
            season.MediaId = mediaId;
            var newId = await repo.AddAsync(season);
            var created = await repo.GetByIdAsync(newId);
            return Results.Created($"/media/{mediaId}/seasons/{newId}", created);
        });
    }
}