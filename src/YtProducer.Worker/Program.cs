using DotNetEnv;
using YtProducer.Infrastructure.DependencyInjection;
using YtProducer.Worker.Services;

// Load environment variables from .env file
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "../../.env");
if (File.Exists(envPath))
{
    DotNetEnv.Env.Load(envPath);
    Console.WriteLine("✓ Environment variables loaded from .env file");
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<JobWorker>();

var host = builder.Build();
await host.RunAsync();
