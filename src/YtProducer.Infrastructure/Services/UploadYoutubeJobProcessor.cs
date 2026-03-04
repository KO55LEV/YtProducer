using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class UploadYoutubeJobProcessor : IJobProcessor
{
    private readonly ILogger<UploadYoutubeJobProcessor> _logger;

    public JobType Type => JobType.UploadYoutube;

    public UploadYoutubeJobProcessor(ILogger<UploadYoutubeJobProcessor> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting YouTube upload for job {JobId}", job.Id);

        for (int i = 0; i < 10; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            job.Progress = (i + 1) * 10;
        }

        job.ResultJson = "{\"youtubeId\": \"dQw4w9WgXcQ\", \"url\": \"https://youtube.com/watch?v=dQw4w9WgXcQ\"}";
        
        _logger.LogInformation("Completed YouTube upload for job {JobId}", job.Id);
    }
}
