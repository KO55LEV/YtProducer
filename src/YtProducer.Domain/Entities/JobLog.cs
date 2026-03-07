namespace YtProducer.Domain.Entities;

public sealed class JobLog
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string Level { get; set; } = "Info";

    public string Message { get; set; } = string.Empty;

    public string? Metadata { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
