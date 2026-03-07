namespace YtProducer.Contracts.Loops;

public sealed record CreateTrackLoopByYoutubeVideoRequest(
    string YoutubeVideoId,
    int LoopCount);
