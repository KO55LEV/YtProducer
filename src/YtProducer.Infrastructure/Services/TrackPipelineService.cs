using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class TrackPipelineService
{
    private readonly IJobService _jobService;
    private readonly ILogger<TrackPipelineService> _logger;

    private static readonly JobType[] Pipeline = 
    {
        JobType.GenerateMusic,
        JobType.GenerateImage,
        JobType.GenerateVisualizer,
        JobType.UploadYoutube
    };

    public TrackPipelineService(IJobService jobService, ILogger<TrackPipelineService> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    public async Task CreateTrackPipelineAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var groupId = Guid.NewGuid();

        for (var index = 0; index < Pipeline.Length; index++)
        {
            var job = new Job
            {
                Type = Pipeline[index],
                TargetType = "track",
                TargetId = trackId,
                JobGroupId = groupId,
                Sequence = index + 1,
                Status = index == 0 ? JobStatus.Pending : JobStatus.Queued,
                MaxRetries = 3
            };

            await _jobService.CreateAsync(job, cancellationToken);
        }

        _logger.LogInformation("Created pipeline group {GroupId} for track {TrackId}", groupId, trackId);
    }

    public async Task ActivateNextQueuedJobAsync(Guid jobGroupId, CancellationToken cancellationToken = default)
    {
        var jobs = await _jobService.GetByJobGroupIdAsync(jobGroupId, cancellationToken);
        var nextQueued = jobs
            .Where(x => x.Status == JobStatus.Queued)
            .OrderBy(x => x.Sequence)
            .FirstOrDefault();

        if (nextQueued is null)
        {
            return;
        }

        nextQueued.Status = JobStatus.Pending;
        await _jobService.UpdateAsync(nextQueued, cancellationToken);
    }
}
