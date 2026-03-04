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

        if (useMockData)
        {
            // Use mock data from JSON files (no database required)
            services.AddSingleton<IPlaylistRepository, MockPlaylistRepository>();
            Console.WriteLine("✓ Using MockPlaylistRepository (JSON files from docs/Playlist/Outputs)");
        }
        else
        {
            // Use real database
            var connectionString = configuration.GetConnectionString("YtProducerDb")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__YtProducerDb")
                ?? "Host=localhost;Port=5432;Database=ytproducer;Username=ytproducer;Password=ytproducer";

            services.AddDbContext<YtProducerDbContext>(options => options.UseNpgsql(connectionString));
            // TODO: Add real repository implementation when database is ready
            services.AddScoped<IPlaylistRepository, MockPlaylistRepository>(); // Temporary
            
            // Job queue services only needed with database
            services.AddScoped<IJobQueueService, JobQueueService>();
            
            Console.WriteLine("✓ Using PostgreSQL database");
        }

        services.AddScoped<IMcpClient, McpClient>();

        return services;
    }
}
