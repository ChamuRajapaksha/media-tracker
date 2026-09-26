using System.Text.Json;
using MediaTracker.Api.Models.Tmdb;

namespace MediaTracker.Api.Services;

public class TmdbService : ITmdbService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;

    public TmdbService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = config["Tmdb:ApiKey"]!;
    }

    public async Task<List<TmdbSearchResult>> SearchAsync(string query, string mediaType)
    {
        // mediaType should be "movie" or "tv"
        var client = _httpClientFactory.CreateClient("Tmdb");
        var url = $"search/{mediaType}?query={Uri.EscapeDataString(query)}&api_key={_apiKey}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbSearchResponse>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Results ?? new List<TmdbSearchResult>();
    }
    public async Task<TmdbSearchResult?> GetDetailsAsync(int tmdbId, string mediaType)
    {
        // mediaType should be "movie" or "tv"
        var client = _httpClientFactory.CreateClient("Tmdb");
        var url = $"{mediaType}/{tmdbId}?api_key={_apiKey}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TmdbSearchResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public async Task<List<TmdbSeasonSummary>> GetSeasonsAsync(int tmdbId)
    {
        var client = _httpClientFactory.CreateClient("Tmdb");
        var url = $"tv/{tmdbId}?api_key={_apiKey}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TmdbShowSeasons>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return result?.Seasons ?? new List<TmdbSeasonSummary>();
    }

    public async Task<TmdbSeasonDetail?> GetSeasonAsync(int tmdbId, int seasonNumber)
    {
        var client = _httpClientFactory.CreateClient("Tmdb");
        var url = $"tv/{tmdbId}/season/{seasonNumber}?api_key={_apiKey}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<TmdbSeasonDetail>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }
}