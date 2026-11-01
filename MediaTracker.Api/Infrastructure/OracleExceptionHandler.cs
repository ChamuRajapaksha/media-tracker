using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Oracle.ManagedDataAccess.Client;

namespace MediaTracker.Api.Infrastructure;

public sealed class OracleExceptionHandler(
    ILogger<OracleExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not OracleException oracleException)
        {
            return false;
        }

        var correlationId = httpContext.TraceIdentifier;
        logger.LogError(
            "Oracle error {OracleNumber} handled for request {CorrelationId}",
            oracleException.Number,
            correlationId);

        var mapping = OracleErrorMapper.Map(oracleException.Number);
        httpContext.Response.StatusCode = mapping.StatusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = mapping.StatusCode,
                Title = mapping.Title,
                Detail = $"The database rejected the request. Correlation id: {correlationId}."
            }
        });
    }
}
