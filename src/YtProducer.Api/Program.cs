using DotNetEnv;
using YtProducer.Api.Endpoints;
using YtProducer.Infrastructure.DependencyInjection;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "../../.env");
if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
    Console.WriteLine("✓ Environment variables loaded from .env file");
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

// Add CORS for local development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.MapGet("/health", async (CancellationToken cancellationToken) =>
{
    await Task.Yield();
    cancellationToken.ThrowIfCancellationRequested();
    return Results.Ok(new { status = "ok" });
});

app.MapPlaylistEndpoints();
app.MapAlbumReleaseEndpoints();
app.MapJobEndpoints();
app.MapLoopEndpoints();
app.MapPromptTemplateEndpoints();
app.MapReasoningEndpoints();
app.MapYoutubePlaylistEndpoints();
app.MapYoutubeUploadQueueEndpoints();
app.MapYoutubeVideoEngagementEndpoints();
app.MapYoutubePublishingEndpoints();

app.Run();
