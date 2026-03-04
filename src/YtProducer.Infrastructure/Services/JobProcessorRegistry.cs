using YtProducer.Domain.Enums;

namespace YtProducer.Infrastructure.Services;

public class JobProcessorRegistry
{
    private readonly IReadOnlyDictionary<JobType, IJobProcessor> _processors;

    public JobProcessorRegistry(IEnumerable<IJobProcessor> processors)
    {
        _processors = processors.ToDictionary(p => p.Type, p => p);
    }

    public IJobProcessor GetProcessor(JobType type)
    {
        if (!_processors.TryGetValue(type, out var processorType))
        {
            throw new InvalidOperationException($"No processor registered for job type: {type}");
        }

        return processorType;
    }
}
