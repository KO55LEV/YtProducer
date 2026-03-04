using YtProducer.Api.Endpoints;
using YtProducer.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", async (CancellationToken cancellationToken) =>
{
    await Task.Yield();
    cancellationToken.ThrowIfCancellationRequested();
    return Results.Ok(new { status = "ok" });
});

app.MapPlaylistEndpoints();

app.Run();
