using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using YtProducer.McpRunner.CLI;
using YtProducer.McpRunner.Models;
using YtProducer.McpRunner.Services;

// Setup dependency injection
var services = new ServiceCollection();

// Add logging
services.AddLogging(logger => logger
    .AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    })
    .SetMinimumLevel(LogLevel.Information)
);

// Add core services
services.AddSingleton<ServiceRegistry>();
services.AddSingleton<ResponseNormalizer>();
services.AddSingleton<McpRunner>(provider => 
    new McpRunner(
        provider.GetRequiredService<ServiceRegistry>(),
        provider.GetRequiredService<ILogger<McpRunner>>(),
        provider.GetRequiredService<ILoggerFactory>()
    )
);

var serviceProvider = services.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Program");

try
{
    // Configure services
    var serviceRegistry = serviceProvider.GetRequiredService<ServiceRegistry>();
    ConfigureServices(serviceRegistry);

    // Get the runner
    var runner = serviceProvider.GetRequiredService<McpRunner>();

    // Start all MCP services
    await runner.StartAllAsync();

    // Check service status
    var status = runner.GetServiceStatus();
    logger.LogInformation("Service status: {@Status}", status);

    if (!runner.AreAllServicesHealthy)
    {
        logger.LogError("Not all services started successfully. Aborting.");
        return 1;
    }

    // Discover tools from all services
    var toolRegistry = new ToolRegistry(runner.GetClients(), loggerFactory.CreateLogger<ToolRegistry>());
    await toolRegistry.DiscoverToolsAsync();

    // Setup routing
    var normalizer = serviceProvider.GetRequiredService<ResponseNormalizer>();
    var toolRouter = new ToolRouter(toolRegistry, runner.GetClients(), normalizer, loggerFactory.CreateLogger<ToolRouter>());

    // Setup and execute CLI handler
    var cliHandler = new CliCommandHandler(toolRegistry, toolRouter, loggerFactory.CreateLogger<CliCommandHandler>());
    var exitCode = await cliHandler.ExecuteAsync(args);

    return exitCode;
}
catch (Exception ex)
{
    logger.LogError(ex, "Fatal error");
    return 1;
}

/// <summary>
/// Configure the MCP service definitions.
/// Update paths as needed for your environment.
/// </summary>
void ConfigureServices(ServiceRegistry registry)
{
    // KieAi Service
    registry.Register(new ServiceDefinition
    {
        Name = "KieAi",
        FileName = "dotnet",
        Arguments = "run --project OnlineTeamTools.MCP.KieAi",
        WorkingDirectory = null, // Set to your project root if needed
        EnvironmentVariables = new()
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" }
        }
    });

    // Media Service
    registry.Register(new ServiceDefinition
    {
        Name = "Media",
        FileName = "dotnet",
        Arguments = "run --project OnlineTeamTools.MCP.Media",
        WorkingDirectory = null,
        EnvironmentVariables = new()
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" }
        }
    });

    // YouTube Service
    registry.Register(new ServiceDefinition
    {
        Name = "YouTube",
        FileName = "dotnet",
        Arguments = "run --project OnlineTeamTools.MCP.YouTube",
        WorkingDirectory = null,
        EnvironmentVariables = new()
        {
            { "ASPNETCORE_ENVIRONMENT", "Development" }
        }
    });
}
