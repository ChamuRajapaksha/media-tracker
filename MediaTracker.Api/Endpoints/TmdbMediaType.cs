namespace MediaTracker.Api.Endpoints;

public static class TmdbMediaType
{
    public const string Movie = "movie";
    public const string Tv = "tv";

    public static bool TryParse(string? value, out string mediaType)
    {
        switch (value)
        {
            case Movie:
                mediaType = "MOVIE";
                return true;
            case Tv:
                mediaType = "SHOW";
                return true;
            default:
                mediaType = string.Empty;
                return false;
        }
    }
}
