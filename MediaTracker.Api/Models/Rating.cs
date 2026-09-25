namespace MediaTracker.Api.Models;

public class Rating
{
    public int RatingId { get; set; }
    public int MediaId { get; set; }
    public decimal? Score { get; set; }
    public string? Review { get; set; }
    public DateTime RatedAt { get; set; }
}