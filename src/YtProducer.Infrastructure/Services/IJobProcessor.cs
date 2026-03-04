using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public interface IJobProcessor
{
    JobType Type { get; }
    
    Task ExecuteAsync(Job job, CancellationToken cancellationToken);
}
