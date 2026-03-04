using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public interface IMcpClient
{
    Task<string> ExecuteJobAsync(Job job, CancellationToken cancellationToken);
}
