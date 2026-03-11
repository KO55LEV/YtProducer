namespace YtProducer.ReasoningAI.Abstractions;

public interface IReasoningClient
{
    ReasoningProvider Provider { get; }

    Task<ReasoningResponse> CompleteAsync(ReasoningRequest request, CancellationToken cancellationToken = default);
}
