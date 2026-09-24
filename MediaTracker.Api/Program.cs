using MediaTracker.Api.Repositories;
using MediaTracker.Api.Endpoints;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddScoped<IMediaRepository, MediaRepository>();

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
app.Run();

