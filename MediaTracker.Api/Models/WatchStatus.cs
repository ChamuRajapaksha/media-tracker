namespace MediaTracker.Api.Models;

public class WatchStatus
{
    public int WatchStatusId { get; set; }
    public int MediaId { get; set; }
    public string Status { get; set; } = string.Empty; // PLAN_TO_WATCH, WATCHING, COMPLETED, DROPPED
    public DateTime UpdatedAt { get; set; }
}