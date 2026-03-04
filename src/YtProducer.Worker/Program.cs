using YtProducer.Infrastructure.DependencyInjection;
using YtProducer.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<JobWorker>();

var host = builder.Build();
await host.RunAsync();
