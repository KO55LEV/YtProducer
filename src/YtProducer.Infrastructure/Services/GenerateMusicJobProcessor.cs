using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class GenerateMusicJobProcessor : IJobProcessor
{
    private readonly ILogger<GenerateMusicJobProcessor> _logger;

    public JobType Type => JobType.GenerateMusic;

    public GenerateMusicJobProcessor(ILogger<GenerateMusicJobProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting music generation for job {JobId}", job.Id);

        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            job.Progress = (i + 1) * 10;
        }

        job.ResultJson = "{\"audioUrl\": \"https://example.com/audio/track123.mp3\", \"duration\": 180}";
        
        _logger.LogInformation("Completed music generation for job {JobId}", job.Id);
    }
}
