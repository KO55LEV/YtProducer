using YtProducer.Infrastructure.Services;

namespace YtProducer.Worker.Services;

public sealed class JobWorker : BackgroundService
{
    private readonly ILogger<JobWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _workerId;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _leaseDuration;

    public JobWorker(
        ILogger<JobWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
        
        var pollIntervalSeconds = configuration.GetValue<int?>("Worker:PollIntervalSeconds") ?? 5;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds));
        
        _heartbeatInterval = TimeSpan.FromSeconds(30);
        _leaseDuration = TimeSpan.FromMinutes(10);

        _logger.LogInformation("JobWorker initialized with ID: {WorkerId}", _workerId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobWorker {WorkerId} started", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverStuckJobsAsync(stoppingToken);
                await ProcessNextJobAsync(stoppingToken);
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in JobWorker {WorkerId}", _workerId);
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("JobWorker {WorkerId} stopped", _workerId);
    }

    private async Task ProcessNextJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var processorRegistry = scope.ServiceProvider.GetRequiredService<JobProcessorRegistry>();
        var pipelineService = scope.ServiceProvider.GetRequiredService<TrackPipelineService>();

        var job = await jobService.AcquireNextJobAsync(_workerId, _leaseDuration, stoppingToken);
        
        if (job == null)
        {
            return;
        }

        _logger.LogInformation("Worker {WorkerId} processing leased job {JobId} ({JobType})", 
            _workerId, job.Id, job.Type);

        IJobProcessor processor;
        try
        {
            processor = processorRegistry.GetProcessor(job.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get processor for job type {JobType}", job.Type);
            await jobService.MarkFailedAsync(job.Id, "ProcessorNotFound", "No processor available for job type", _workerId, stoppingToken);
            return;
        }

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCts.Token.IsCancellationRequested)
            {
                await Task.Delay(_heartbeatInterval, heartbeatCts.Token);
                
                try
                {
                    await jobService.TryUpdateProgressAsync(job.Id, job.Progress, _workerId, heartbeatCts.Token);
                    var updated = await jobService.TryUpdateHeartbeatAsync(job.Id, _workerId, _leaseDuration, heartbeatCts.Token);
                    if (!updated)
                    {
                        _logger.LogWarning("Heartbeat update skipped for job {JobId} (ownership/status changed)", job.Id);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update heartbeat for job {JobId}", job.Id);
                }
            }
        }, heartbeatCts.Token);

        try
        {
            await processor.ExecuteAsync(job, stoppingToken);

            await jobService.MarkCompletedAsync(job.Id, job.ResultJson, _workerId, stoppingToken);

            _logger.LogInformation("Worker {WorkerId} completed job {JobId} of type {JobType}", 
                _workerId, job.Id, job.Type);

            if (job.JobGroupId is Guid groupId)
            {
                await pipelineService.ActivateNextQueuedJobAsync(groupId, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("Job {JobId} cancelled due to worker shutdown", job.Id);
            await jobService.MarkFailedAsync(job.Id, "WorkerShutdown", "Worker shutdown", _workerId, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker {WorkerId} failed to process job {JobId}", _workerId, job.Id);
            await jobService.MarkFailedAsync(job.Id, "ProcessorError", ex.Message, _workerId, stoppingToken);
        }
        finally
        {
            heartbeatCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task RecoverStuckJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobService = scope.ServiceProvider.GetRequiredService<IJobService>();
        var recovered = await jobService.RecoverExpiredLeasesAsync(cancellationToken);
        if (recovered > 0)
        {
            _logger.LogWarning("Recovered {RecoveredCount} stuck jobs with expired leases", recovered);
        }
    }
}
