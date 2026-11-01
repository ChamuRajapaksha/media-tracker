using MediaTracker.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MediaTracker.Api.Tests;

public class OracleExceptionHandlerTests
{
    [Fact]
    public async Task Non_oracle_exceptions_are_left_for_the_next_handler()
    {
        var problemDetails = Substitute.For<IProblemDetailsService>();
        var handler = new OracleExceptionHandler(
            NullLogger<OracleExceptionHandler>.Instance,
            problemDetails);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException("not a database error"),
            CancellationToken.None);

        Assert.False(handled);
        await problemDetails.DidNotReceive().TryWriteAsync(Arg.Any<ProblemDetailsContext>());
    }
}
