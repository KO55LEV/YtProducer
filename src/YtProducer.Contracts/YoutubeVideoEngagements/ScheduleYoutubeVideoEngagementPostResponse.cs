namespace YtProducer.Contracts.YoutubeVideoEngagements;

public sealed record ScheduleYoutubeVideoEngagementPostResponse(
    Guid YoutubeVideoEngagementId,
    Guid JobId,
    string JobType,
    string JobStatus);
