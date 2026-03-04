using YtProducer.Infrastructure.Services;

namespace YtProducer.Worker.Services;

public sealed class PendingJobWorker : BackgroundService
{
    private readonly ILogger<PendingJobWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval;

    public PendingJobWorker(
        ILogger<PendingJobWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;

        var pollIntervalSeconds = configuration.GetValue<int?>("Worker:PollIntervalSeconds") ?? 15;
        _pollInterval = TimeSpan.FromSeconds(Math.Max(1, pollIntervalSeconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Pending job worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var queueService = scope.ServiceProvider.GetRequiredService<IJobQueueService>();
                var mcpClient = scope.ServiceProvider.GetRequiredService<IMcpClient>();

                var pendingJobs = await queueService.GetPendingJobsAsync(batchSize: 10, stoppingToken);

                foreach (var job in pendingJobs)
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    await queueService.MarkInProgressAsync(job.Id, stoppingToken);

                    _logger.LogInformation("Executing job {JobId} ({JobType}).", job.Id, job.Type);

                    try
                    {
                        var executionMessage = await mcpClient.ExecuteJobAsync(job, stoppingToken);
                        _logger.LogInformation("Job {JobId} completed with message: {Message}", job.Id, executionMessage);

                        await queueService.MarkCompletedAsync(job.Id, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Job {JobId} failed.", job.Id);
                        await queueService.MarkFailedAsync(job.Id, ex.Message, stoppingToken);
                    }
                }

                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while polling job queue.");
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Pending job worker stopped.");
    }
}
