using DotNetEnv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YtProducer.Console.Services;
using YtProducer.Infrastructure.DependencyInjection;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "../../.env");
if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
    Console.WriteLine("✓ Environment variables loaded from .env file\n");
}

// Build configuration
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["UseMockData"] = Environment.GetEnvironmentVariable("USE_MOCK_DATA") ?? "false"
    })
    .Build();

// Setup dependency injection
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddInfrastructure(configuration);

// Add HTTP client for API communication
services.AddHttpClient<ApiClient>();

// Add YT service
services.AddScoped<YtService>();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

logger.LogInformation("╔══════════════════════════════════════════════════╗");
logger.LogInformation("║     YtProducer Console - Database Demo Tool      ║");
logger.LogInformation("╚══════════════════════════════════════════════════╝\n");

// Run YT service
try
{
    var ytService = serviceProvider.GetRequiredService<YtService>();
    await ytService.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running demo: {Message}", ex.Message);
    Environment.Exit(1);
}

logger.LogInformation("\n✓ Demo completed successfully!");
