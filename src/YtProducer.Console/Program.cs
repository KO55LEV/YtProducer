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

// Build configuration from environment variables loaded from .env
var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();

// Setup dependency injection
var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning);
});
services.AddInfrastructure(configuration);

// Add HTTP client for API communication
services.AddHttpClient<ApiClient>();

// Add YT service
services.AddScoped<YtService>();

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
var commandArgs = args.Skip(1).ToArray();
var commands = new Dictionary<string, Func<IServiceProvider, Task>>(StringComparer.OrdinalIgnoreCase)
{
    ["playlist_init"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunPlaylistInitAsync();
    },
    ["playlist"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.PrintPlaylistListAsync();
    },
    ["playlists"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.PrintPlaylistListAsync();
    },
    ["generate-media"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateMediaAsync();
    },
    ["generate_media"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateMediaAsync();
    },
    ["generate-image"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateImageAsync(commandArgs);
    },
    ["generate_image"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateImageAsync(commandArgs);
    },
    ["generate-youtube-playlist"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateYoutubePlaylistAsync(commandArgs);
    },
    ["generate_youtube_playlist"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunGenerateYoutubePlaylistAsync(commandArgs);
    },
    ["upload-youtube-video"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunUploadYoutubeVideoAsync(commandArgs);
    },
    ["upload_youtube_video"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunUploadYoutubeVideoAsync(commandArgs);
    },
    ["upload-youtube-thumbnail"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunUploadYoutubeThumbnailAsync(commandArgs);
    },
    ["upload_youtube_thumbnail"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunUploadYoutubeThumbnailAsync(commandArgs);
    },
    ["add-youtube-videos-to-playlist"] = async scopedServices =>
    {
        var ytService = scopedServices.GetRequiredService<YtService>();
        await ytService.RunAddYoutubeVideosToPlaylistAsync(commandArgs);
    }
};

var command = args.FirstOrDefault()?.Trim();

if (string.IsNullOrWhiteSpace(command))
{
    LogAvailableCommands(commands.Keys);
    return;
}

if (!commands.TryGetValue(command, out var commandHandler))
{
    Console.WriteLine($"Unknown command: {command}");
    LogAvailableCommands(commands.Keys);
    return;
}

try
{
    using var scope = serviceProvider.CreateScope();
    await commandHandler(scope.ServiceProvider);
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running command {Command}: {Message}", command, ex.Message);
    Environment.Exit(1);
}

static void LogAvailableCommands(IEnumerable<string> commandNames)
{
    Console.WriteLine("Available commands:");
    foreach (var commandName in commandNames.OrderBy(name => name))
    {
        Console.WriteLine($"  - {commandName}");
    }
}
