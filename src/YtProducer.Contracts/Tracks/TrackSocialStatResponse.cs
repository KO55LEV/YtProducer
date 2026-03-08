namespace YtProducer.Contracts.Tracks;

public sealed record TrackSocialStatResponse(
    Guid TrackId,
    Guid PlaylistId,
    int LikesCount,
    int DislikesCount);
