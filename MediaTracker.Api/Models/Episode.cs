namespace MediaTracker.Api.Models;

public class Episode
{
    public int EpisodeId { get; set; }
    public int SeasonId { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Title { get; set; }
    public DateTime? AirDate { get; set; }
}