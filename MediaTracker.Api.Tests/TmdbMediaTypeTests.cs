using MediaTracker.Api.Endpoints;

namespace MediaTracker.Api.Tests;

public class TmdbMediaTypeTests
{
    [Theory]
    [InlineData("movie", "MOVIE")]
    [InlineData("tv", "SHOW")]
    public void Known_values_map_to_the_database_media_type(string value, string expected)
    {
        var parsed = TmdbMediaType.TryParse(value, out var mediaType);

        Assert.True(parsed);
        Assert.Equal(expected, mediaType);
    }

    [Theory]
    [InlineData("MOVIE")]
    [InlineData("TV")]
    [InlineData("book")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_values_are_rejected(string? value)
    {
        var parsed = TmdbMediaType.TryParse(value, out var mediaType);

        Assert.False(parsed);
        Assert.Equal(string.Empty, mediaType);
    }
}
