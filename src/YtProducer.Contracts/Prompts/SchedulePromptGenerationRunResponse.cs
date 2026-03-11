namespace YtProducer.Contracts.Prompts;

public sealed record SchedulePromptGenerationRunResponse(
    Guid PromptGenerationId,
    Guid JobId,
    string JobType,
    string JobStatus);
