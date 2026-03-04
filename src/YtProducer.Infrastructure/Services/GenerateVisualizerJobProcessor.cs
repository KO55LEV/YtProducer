using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class GenerateVisualizerJobProcessor : IJobProcessor
{
    private readonly ILogger<GenerateVisualizerJobProcessor> _logger;

    public JobType Type => JobType.GenerateVisualizer;

    public GenerateVisualizerJobProcessor(ILogger<GenerateVisualizerJobProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting visualizer generation for job {JobId}", job.Id);

        for (int i = 0; i < 20; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            job.Progress = (i + 1) * 5;
        }

        job.ResultJson = "{\"videoUrl\": \"https://example.com/videos/viz123.mp4\", \"duration\": 180, \"resolution\": \"1920x1080\"}";
        
        _logger.LogInformation("Completed visualizer generation for job {JobId}", job.Id);
    }
}
