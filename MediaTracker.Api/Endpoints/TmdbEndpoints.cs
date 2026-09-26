using MediaTracker.Api.Services;

namespace MediaTracker.Api.Endpoints;

public static class TmdbEndpoints
{
    public static void MapTmdbEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/tmdb");

        group.MapGet("/search", async (string query, string type, ITmdbService tmdb) =>
        {
            var results = await tmdb.SearchAsync(query, type);
            return Results.Ok(results);
        });
    }
}