using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var useMockData = configuration.GetValue<bool>("UseMockData");
        var connectionString = configuration.GetConnectionString("YtProducerDb")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__YtProducerDb")
            ?? "Host=localhost;Port=5432;Database=ytproducer;Username=ytproducer;Password=ytproducer";

        services.AddDbContext<YtProducerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IJobService, JobService>();
        services.AddScoped<TrackPipelineService>();

        services.AddScoped<IJobProcessor, GenerateMusicJobProcessor>();
        services.AddScoped<IJobProcessor, GenerateImageJobProcessor>();
        services.AddScoped<IJobProcessor, GenerateVisualizerJobProcessor>();
        services.AddScoped<IJobProcessor, UploadYoutubeJobProcessor>();
        services.AddScoped<JobProcessorRegistry>();

        if (useMockData)
        {
            services.AddSingleton<IPlaylistRepository, MockPlaylistRepository>();
            Console.WriteLine("✓ Using MockPlaylistRepository (JSON files from docs/Playlist/Outputs)");
        }
        else
        {
            services.AddScoped<IPlaylistRepository, MockPlaylistRepository>(); // Temporary
            Console.WriteLine("✓ Using PostgreSQL database with job processing");
        }

        services.AddScoped<IYoutubePlaylistRepository, YoutubePlaylistRepository>();

        services.AddScoped<IMcpClient, McpClient>();

        return services;
    }
}
