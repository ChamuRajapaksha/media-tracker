using MediaTracker.Api.Repositories;
using MediaTracker.Api.Endpoints;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IMediaRepository, MediaRepository>();
builder.Services.AddScoped<IGenreRepository, GenreRepository>();
builder.Services.AddScoped<ISeasonRepository, SeasonRepository>();
builder.Services.AddScoped<IEpisodeRepository, EpisodeRepository>();
builder.Services.AddScoped<IWatchStatusRepository, WatchStatusRepository>();
builder.Services.AddScoped<IRatingRepository, RatingRepository>();

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
app.MapWatchStatusEndpoints();
app.MapRatingEndpoints();
app.Run();

