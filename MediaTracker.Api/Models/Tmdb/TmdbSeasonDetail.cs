using System.Text.Json.Serialization;

namespace MediaTracker.Api.Models.Tmdb;

// GET /tv/{id} returns a seasons[] array of summaries, one per season.
public class TmdbShowSeasons
{
    [JsonPropertyName("seasons")]
    public List<TmdbSeasonSummary> Seasons { get; set; } = new();
}

public class TmdbSeasonSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

// GET /tv/{id}/season/{season_number} returns the season plus its episodes[].
public class TmdbSeasonDetail
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("episodes")]
    public List<TmdbEpisode> Episodes { get; set; } = new();
}

public class TmdbEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episode_number")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("air_date")]
    public string? AirDate { get; set; } // "yyyy-MM-dd" or empty for unaired episodes
}
