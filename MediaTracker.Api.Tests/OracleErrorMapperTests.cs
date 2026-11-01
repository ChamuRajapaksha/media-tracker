using MediaTracker.Api.Infrastructure;

namespace MediaTracker.Api.Tests;

public class OracleErrorMapperTests
{
    [Theory]
    [InlineData(1, 409, "Duplicate value")]
    [InlineData(2290, 400, "Invalid value")]
    [InlineData(2291, 400, "Missing related record")]
    [InlineData(1400, 400, "Missing required value")]
    [InlineData(12899, 400, "Value too long")]
    public void Known_oracle_errors_map_to_their_status(int number, int statusCode, string title)
    {
        var mapping = OracleErrorMapper.Map(number);

        Assert.Equal(statusCode, mapping.StatusCode);
        Assert.Equal(title, mapping.Title);
    }

    [Theory]
    [InlineData(942)]
    [InlineData(1017)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Unknown_oracle_errors_fall_back_to_internal_server_error(int number)
    {
        var mapping = OracleErrorMapper.Map(number);

        Assert.Equal(500, mapping.StatusCode);
        Assert.Equal("Database error", mapping.Title);
    }
}
