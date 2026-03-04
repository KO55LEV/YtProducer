namespace YtProducer.Contracts.Jobs;

public sealed record UpdateProgressRequest(int Progress, string? WorkerId);
