using System.Text.Json.Serialization;

namespace MediaTracker.Api.Models.Tmdb;

public class TmdbSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbSearchResult> Results { get; set; } = new();
}

public class TmdbSearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; } // present for movies

    [JsonPropertyName("name")]
    public string? Name { get; set; } // present for TV shows

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; } // movies

    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; } // TV shows
}