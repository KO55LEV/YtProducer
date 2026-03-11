using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using YtProducer.ReasoningAI.DependencyInjection;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var useMockData = false;
        var useMockDataValue = configuration["USE_MOCK_DATA"];
        if (!string.IsNullOrWhiteSpace(useMockDataValue))
        {
            _ = bool.TryParse(useMockDataValue, out useMockData);
        }
        
        // Build connection string from environment-backed configuration
        var host = configuration["POSTGRES_HOST"] ?? "localhost";
        var port = configuration["POSTGRES_PORT"] ?? "5432";
        var database = configuration["POSTGRES_DATABASE"] ?? "ytproducer";
        var username = configuration["POSTGRES_USER"] ?? "ytproducer";
        var password = configuration["POSTGRES_PASSWORD"] ?? "ytproducer";
        
        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";

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
            services.AddScoped<IPlaylistRepository, PlaylistRepository>();
            Console.WriteLine("✓ Using PostgreSQL database with job processing");
        }

        services.AddScoped<IYoutubePlaylistRepository, YoutubePlaylistRepository>();
        services.AddScoped<IYoutubeUploadQueueService, YoutubeUploadQueueService>();

        services.AddScoped<IMcpClient, McpClient>();
        services.AddReasoningAI(configuration);

        return services;
    }
}
