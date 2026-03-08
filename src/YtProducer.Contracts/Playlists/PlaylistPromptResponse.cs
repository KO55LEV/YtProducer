namespace YtProducer.Contracts.Playlists;

public sealed record PlaylistPromptResponse(
    Guid PlaylistId,
    string PromptType,
    string? SourceFileName,
    IReadOnlyList<PlaylistPromptItemResponse> Prompts);

public sealed record PlaylistPromptItemResponse(
    int PlaylistPosition,
    string TrackTitle,
    string Prompt);
