using MediaTracker.Api.Repositories;
using MediaTracker.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace MediaTracker.Api.Tests;

public class MediaApiFactory : WebApplicationFactory<Program>
{
    public const string EnvironmentName = "Testing";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddUserSecrets(TestConfiguration.UserSecretsId, true));
    }
}

/// <summary>
/// Runs the real routing and the real endpoint delegates, but swaps every repository and the
/// TMDB service for a substitute. That leaves only the orchestration in the endpoint to test,
/// which is the part that can be checked without a database or a network call.
///
/// One of these per test, not per class. NSubstitute keeps the Returns setup from a test
/// after that test ends, so a shared instance would let a later test pass on a stub the
/// earlier one installed.
/// </summary>
public sealed class EndpointApiFactory : MediaApiFactory
{
    public IMediaRepository Media { get; } = Substitute.For<IMediaRepository>();
    public IGenreRepository Genres { get; } = Substitute.For<IGenreRepository>();
    public ISeasonRepository Seasons { get; } = Substitute.For<ISeasonRepository>();
    public IEpisodeRepository Episodes { get; } = Substitute.For<IEpisodeRepository>();
    public IEpisodeProgressRepository Progress { get; } = Substitute.For<IEpisodeProgressRepository>();
    public IWatchStatusRepository WatchStatus { get; } = Substitute.For<IWatchStatusRepository>();
    public IRatingRepository Ratings { get; } = Substitute.For<IRatingRepository>();
    public ITmdbService Tmdb { get; } = Substitute.For<ITmdbService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton(Media));
            services.Replace(ServiceDescriptor.Singleton(Genres));
            services.Replace(ServiceDescriptor.Singleton(Seasons));
            services.Replace(ServiceDescriptor.Singleton(Episodes));
            services.Replace(ServiceDescriptor.Singleton(Progress));
            services.Replace(ServiceDescriptor.Singleton(WatchStatus));
            services.Replace(ServiceDescriptor.Singleton(Ratings));
            services.Replace(ServiceDescriptor.Singleton(Tmdb));
        });
    }
}
