using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class GenerateImageJobProcessor : IJobProcessor
{
    private readonly ILogger<GenerateImageJobProcessor> _logger;

    public JobType Type => JobType.GenerateImage;

    public GenerateImageJobProcessor(ILogger<GenerateImageJobProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting image generation for job {JobId}", job.Id);

        for (int i = 0; i < 8; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
            job.Progress = (int)Math.Round(((i + 1) / 8.0) * 100);
        }

        job.ResultJson = "{\"imageUrl\": \"https://example.com/images/cover123.jpg\", \"width\": 1920, \"height\": 1080}";
        
        _logger.LogInformation("Completed image generation for job {JobId}", job.Id);
    }
}
