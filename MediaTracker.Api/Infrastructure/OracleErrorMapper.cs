namespace MediaTracker.Api.Infrastructure;

public sealed record OracleErrorMapping(int StatusCode, string Title);

public static class OracleErrorMapper
{
    public static OracleErrorMapping Map(int oracleErrorNumber) => oracleErrorNumber switch
    {
        1 => new(409, "Duplicate value"),
        2290 => new(400, "Invalid value"),
        2291 => new(400, "Missing related record"),
        1400 => new(400, "Missing required value"),
        12899 => new(400, "Value too long"),
        _ => new(500, "Database error")
    };
}
