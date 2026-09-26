using System.Net;
using System.Net.Sockets;
using MediaTracker.Api.Repositories;
using MediaTracker.Api.Endpoints;
using MediaTracker.Api.Services;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IMediaRepository, MediaRepository>();
builder.Services.AddScoped<IGenreRepository, GenreRepository>();
builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
builder.Services.AddScoped<IEpisodeRepository, EpisodeRepository>();
builder.Services.AddScoped<IEpisodeProgressRepository, EpisodeProgressRepository>();
builder.Services.AddScoped<IWatchStatusRepository, WatchStatusRepository>();
builder.Services.AddScoped<IRatingRepository, RatingRepository>();
builder.Services.AddScoped<ITmdbService, TmdbService>();
builder.Services.AddHttpClient("Tmdb", client =>
{
    client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    ConnectCallback = async (context, cancellationToken) =>
    {
        var entry = await Dns.GetHostEntryAsync(context.DnsEndPoint.Host, AddressFamily.InterNetwork, cancellationToken);
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(entry.AddressList[0], context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
});
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMediaEndpoints();
app.MapGenreEndpoints();
app.MapSeasonEndpoints();
app.MapEpisodeEndpoints();
app.MapEpisodeProgressEndpoints();
app.MapWatchStatusEndpoints();
app.MapRatingEndpoints();
app.MapTmdbEndpoints();
app.Run();

