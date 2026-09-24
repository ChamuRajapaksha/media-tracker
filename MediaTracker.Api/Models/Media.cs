namespace MediaTracker.Api.Models;

public class Media
{
    public int MediaId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty; // "MOVIE" or "SHOW"
    public int? ReleaseYear { get; set; }
    public string? Overview { get; set; }
    public string? PosterUrl { get; set; }
    public int? TmdbId { get; set; }
    public DateTime CreatedAt { get; set; }
}