namespace YtProducer.ReasoningAI.Abstractions;

public interface IReasoningClientFactory
{
    IReasoningClient GetClient(ReasoningProvider provider);
}
