namespace MediaTracker.Api.Models;

public class Season
{
    public int SeasonId { get; set; }
    public int MediaId { get; set; }
    public int SeasonNumber { get; set; }
    public string? Title { get; set; }
}