using System.Net;
using System.Text;

namespace MediaTracker.Api.Tests;

public sealed class FakeTmdbHandler : HttpMessageHandler
{
    public static readonly Uri BaseAddress = new("https://api.themoviedb.org/3/");

    private readonly Queue<HttpResponseMessage> _responses = [];

    public List<Uri> Requests { get; } = [];

    public string? LastRequestPathAndQuery =>
        Requests.Count == 0 ? null : Requests[^1].PathAndQuery;

    public static FakeTmdbHandler ReturningJson(string json) => Enqueue(Json(json));

    public static FakeTmdbHandler RespondingWith(params string[] jsonBodies) => Enqueue(
        Array.ConvertAll(jsonBodies, Json));

    public static FakeTmdbHandler Failing(HttpStatusCode statusCode) => Enqueue(
        new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) });

    public HttpClient CreateClient() => new(this, disposeHandler: false) { BaseAddress = BaseAddress };

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);

        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.NotImplemented)
            {
                Content = new StringContent("FakeTmdbHandler ran out of queued responses")
            });
    }

    private static FakeTmdbHandler Enqueue(params HttpResponseMessage[] responses)
    {
        var handler = new FakeTmdbHandler();
        foreach (var response in responses)
        {
            handler._responses.Enqueue(response);
        }

        return handler;
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
}
