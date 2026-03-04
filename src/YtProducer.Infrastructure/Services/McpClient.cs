using YtProducer.Domain.Entities;

namespace YtProducer.Infrastructure.Services;

public sealed class McpClient : IMcpClient
{
    public Task<string> ExecuteJobAsync(Job job, CancellationToken cancellationToken)
    {
        var message = $"MCP integration is not wired yet for job {job.Id}.";
        return Task.FromResult(message);
    }
}
