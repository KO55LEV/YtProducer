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
        var connectionString = configuration.GetConnectionString("YtProducerDb")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__YtProducerDb")
            ?? "Host=localhost;Port=5432;Database=ytproducer;Username=ytproducer;Password=ytproducer";

        services.AddDbContext<YtProducerDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IMcpClient, McpClient>();
        services.AddScoped<IJobQueueService, JobQueueService>();

        return services;
    }
}
