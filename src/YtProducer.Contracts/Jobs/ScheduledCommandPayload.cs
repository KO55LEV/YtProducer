using System.Text.Json;

namespace YtProducer.Contracts.Jobs;

public sealed record ScheduledCommandPayload(
    string Command,
    int Version,
    JsonElement Arguments);
