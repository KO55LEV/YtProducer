using System.Net;
using YtProducer.ReasoningAI.Abstractions;

namespace YtProducer.ReasoningAI;

public sealed class ReasoningClientException : Exception
{
    public ReasoningClientException(
        ReasoningProvider provider,
        string message,
        HttpStatusCode? statusCode = null,
        string? responseBody = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Provider = provider;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public ReasoningProvider Provider { get; }

    public HttpStatusCode? StatusCode { get; }

    public string? ResponseBody { get; }
}
