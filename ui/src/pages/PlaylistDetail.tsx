import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type {
  Playlist,
  PlaylistMediaResponse,
  PlaylistTrackMedia,
  ScheduleTrackLoopResponse,
  Track,
  TrackVideoGeneration
} from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function getEnergyColor(energyLevel?: number | null): string {
  if (!energyLevel) return "#6b7280";
  if (energyLevel >= 8) return "#ef4444";
  if (energyLevel >= 6) return "#f97316";
  if (energyLevel >= 4) return "#eab308";
  return "#10b981";
}

function isMasterImageForPosition(fileName: string, position: number): boolean {
  const escapedPosition = String(position).replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(`^${escapedPosition}\\.[^.]+$`, "i").test(fileName);
}

export default function PlaylistDetail() {
  const { id } = useParams<{ id: string }>();
  const [playlist, setPlaylist] = useState<Playlist | null>(null);
  const [mediaByPosition, setMediaByPosition] = useState<Record<number, PlaylistTrackMedia>>({});
  const [mediaLoading, setMediaLoading] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [imageIndexByPosition, setImageIndexByPosition] = useState<Record<number, number>>({});
  const [mediaViewByPosition, setMediaViewByPosition] = useState<Record<number, "video" | "image">>({});
  const [showThumbnail, setShowThumbnail] = useState(false);
  const [setBackgroundBusyByPosition, setSetBackgroundBusyByPosition] = useState<Record<number, boolean>>({});
  const [moveImageBusyByPosition, setMoveImageBusyByPosition] = useState<Record<number, boolean>>({});
  const [deleteThumbnailBusyByPosition, setDeleteThumbnailBusyByPosition] = useState<Record<number, boolean>>({});
  const [moveAudioBusyByKey, setMoveAudioBusyByKey] = useState<Record<string, boolean>>({});
  const [deleteAudioBusyByKey, setDeleteAudioBusyByKey] = useState<Record<string, boolean>>({});
  const [lightboxState, setLightboxState] = useState<{ position: number; index: number } | null>(null);
  const [videoGenerationsByPosition, setVideoGenerationsByPosition] = useState<Record<number, TrackVideoGeneration>>({});
  const [loopCountByTrackId, setLoopCountByTrackId] = useState<Record<string, number>>({});
  const [createLoopBusyByTrackId, setCreateLoopBusyByTrackId] = useState<Record<string, boolean>>({});
  const [createLoopJobIdByTrackId, setCreateLoopJobIdByTrackId] = useState<Record<string, string>>({});

  const resolveMediaUrl = useMemo(() => {
    return (url: string) => (url.startsWith("http") ? url : `${apiBaseUrl}${url}`);
  }, []);

  useEffect(() => {
    if (!id) return;

    async function fetchPlaylist(): Promise<void> {
      try {
        setLoading(true);
        const response = await fetch(`${apiBaseUrl}/playlists/${id}`);

        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }

        const data = (await response.json()) as Playlist;
        setPlaylist(data);
        setError(null);
      } catch (err) {
        setError(err instanceof Error ? err.message : "Unknown error");
      } finally {
        setLoading(false);
      }
    }

    fetchPlaylist();
  }, [id]);

  useEffect(() => {
    if (!lightboxState) return;

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setLightboxState(null);
        return;
      }

      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        event.preventDefault();
        setLightboxState((prev) => {
          if (!prev) {
            return prev;
          }

          const media = mediaByPosition[prev.position];
          const images = media?.images ?? [];
          const thumbnailImages = images.filter((image) =>
            /_thumbnail(?:_\d+)?\.[^.]+$/i.test(image.fileName)
          );
          const displayImages = showThumbnail ? thumbnailImages : images;
          const count = displayImages.length;
          if (count <= 1) {
            return prev;
          }

          const safeIndex = ((prev.index % count) + count) % count;
          const nextIndex =
            event.key === "ArrowLeft"
              ? (safeIndex - 1 + count) % count
              : (safeIndex + 1) % count;

          return { ...prev, index: nextIndex };
        });
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [lightboxState, mediaByPosition, showThumbnail]);

  async function reloadMedia(): Promise<void> {
    if (!id) return;
    try {
      await Promise.all([reloadMediaFiles(), reloadVideoGenerations()]);
    } catch {
      // Keep existing media state when refresh fails.
    }
  }

  async function reloadMediaFiles(): Promise<void> {
    if (!id) return;

    const response = await fetch(`${apiBaseUrl}/playlists/${id}/media`);
    if (!response.ok) {
      throw new Error(`Media request failed with status ${response.status}`);
    }

    const data = (await response.json()) as PlaylistMediaResponse;
    const map: Record<number, PlaylistTrackMedia> = {};
    const defaultView: Record<number, "video" | "image"> = {};
    for (const track of data.tracks) {
      map[track.playlistPosition] = track;
      defaultView[track.playlistPosition] = track.videos.length > 0 ? "video" : "image";
    }

    setMediaByPosition(map);
    setMediaViewByPosition((prev) => ({ ...defaultView, ...prev }));
  }

  async function reloadVideoGenerations(): Promise<void> {
    if (!id) return;

    const response = await fetch(`${apiBaseUrl}/playlists/${id}/video-generations`);
    if (!response.ok) {
      throw new Error(`Video generation request failed with status ${response.status}`);
    }

    const data = (await response.json()) as TrackVideoGeneration[];
    const map: Record<number, TrackVideoGeneration> = {};
    for (const item of data) {
      map[item.playlistPosition] = item;
    }

    setVideoGenerationsByPosition(map);
  }

  async function handleSetBackground(position: number, fileName: string): Promise<boolean> {
    if (!id) return false;
    if (isMasterImageForPosition(fileName, position)) {
      return false;
    }

    try {
      setSetBackgroundBusyByPosition((prev) => ({ ...prev, [position]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/media/set-background`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playlistPosition: position,
          fileName
        })
      });

      if (!response.ok) {
        throw new Error(`Set background failed with status ${response.status}`);
      }

      setImageIndexByPosition((prev) => ({ ...prev, [position]: 0 }));
      await reloadMedia();
      return true;
    } catch {
      // Keep UI stable and allow retry.
      return false;
    } finally {
      setSetBackgroundBusyByPosition((prev) => ({ ...prev, [position]: false }));
    }
  }

  async function handleMoveImage(position: number, fileName: string): Promise<boolean> {
    if (!id) return false;

    try {
      setMoveImageBusyByPosition((prev) => ({ ...prev, [position]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/media/move-image`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playlistPosition: position,
          fileName
        })
      });

      if (!response.ok) {
        throw new Error(`Move image failed with status ${response.status}`);
      }

      setImageIndexByPosition((prev) => ({ ...prev, [position]: 0 }));
      await reloadMedia();
      return true;
    } catch {
      return false;
    } finally {
      setMoveImageBusyByPosition((prev) => ({ ...prev, [position]: false }));
    }
  }

  async function handleDeleteThumbnail(position: number, fileName: string): Promise<boolean> {
    if (!id) return false;

    try {
      setDeleteThumbnailBusyByPosition((prev) => ({ ...prev, [position]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/media/delete-thumbnail`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playlistPosition: position,
          fileName
        })
      });

      if (!response.ok) {
        throw new Error(`Delete thumbnail failed with status ${response.status}`);
      }

      setImageIndexByPosition((prev) => ({ ...prev, [position]: 0 }));
      await reloadMedia();
      return true;
    } catch {
      return false;
    } finally {
      setDeleteThumbnailBusyByPosition((prev) => ({ ...prev, [position]: false }));
    }
  }

  async function handleMoveAudio(position: number, fileName: string): Promise<boolean> {
    if (!id) return false;
    const key = `${position}:${fileName}`;
    try {
      setMoveAudioBusyByKey((prev) => ({ ...prev, [key]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/media/move-audio`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playlistPosition: position,
          fileName
        })
      });

      if (!response.ok) {
        throw new Error(`Move audio failed with status ${response.status}`);
      }

      await reloadMedia();
      return true;
    } catch {
      return false;
    } finally {
      setMoveAudioBusyByKey((prev) => ({ ...prev, [key]: false }));
    }
  }

  async function handleDeleteAudio(position: number, fileName: string): Promise<boolean> {
    if (!id) return false;
    const key = `${position}:${fileName}`;
    try {
      setDeleteAudioBusyByKey((prev) => ({ ...prev, [key]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/media/delete-audio`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          playlistPosition: position,
          fileName
        })
      });

      if (!response.ok) {
        throw new Error(`Delete audio failed with status ${response.status}`);
      }

      await reloadMedia();
      return true;
    } catch {
      return false;
    } finally {
      setDeleteAudioBusyByKey((prev) => ({ ...prev, [key]: false }));
    }
  }

  async function handleCreateLoop(track: Track): Promise<boolean> {
    if (!id) return false;

    const loopCount = Math.max(2, Math.trunc(loopCountByTrackId[track.id] ?? 2));
    try {
      setCreateLoopBusyByTrackId((prev) => ({ ...prev, [track.id]: true }));
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/track-loops`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          trackId: track.id,
          loopCount
        })
      });

      if (!response.ok) {
        throw new Error(`Create loop failed with status ${response.status}`);
      }

      const data = (await response.json()) as ScheduleTrackLoopResponse;
      setCreateLoopJobIdByTrackId((prev) => ({ ...prev, [track.id]: data.jobId }));
      return true;
    } catch {
      return false;
    } finally {
      setCreateLoopBusyByTrackId((prev) => ({ ...prev, [track.id]: false }));
    }
  }

  useEffect(() => {
    if (!id) return;

    async function fetchMedia(): Promise<void> {
      try {
        setMediaLoading(true);
        await Promise.all([reloadMediaFiles(), reloadVideoGenerations()]);
      } catch {
        setMediaByPosition({});
        setVideoGenerationsByPosition({});
      } finally {
        setMediaLoading(false);
      }
    }

    fetchMedia();
  }, [id]);

  if (loading) {
    return (
      <div className="page-content">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading playlist...</p>
        </div>
      </div>
    );
  }

  if (error || !playlist) {
    return (
      <div className="page-content">
        <div className="alert alert-error">
          <strong>Error:</strong> {error || "Playlist not found"}
        </div>
        <Link to="/" className="btn btn-secondary">← Back to Playlists</Link>
      </div>
    );
  }

  return (
    <div className="page-content">
      <Link to="/" className="breadcrumb">← Back to Playlists</Link>

      <div className="playlist-header">
        <div className="playlist-info">
          <h1 className="playlist-title-large">{playlist.title}</h1>
          {playlist.theme && (
            <div className="playlist-theme-badge">🎵 {playlist.theme}</div>
          )}
          {playlist.description && (
            <p className="playlist-description-large">{playlist.description}</p>
          )}
          {playlist.playlistStrategy && (
            <div className="playlist-strategy">
              <strong>Strategy:</strong> {playlist.playlistStrategy}
            </div>
          )}
          <div className="playlist-meta">
            <span>{playlist.trackCount} tracks</span>
            <span>Created {new Date(playlist.createdAtUtc).toLocaleDateString()}</span>
            {playlist.publishedAtUtc && (
              <span>Published {new Date(playlist.publishedAtUtc).toLocaleDateString()}</span>
            )}
          </div>
          <div className="playlist-toolbar">
            <button
              type="button"
              className={`playlist-toggle ${showThumbnail ? "is-active" : ""}`}
              onClick={() => setShowThumbnail((prev) => !prev)}
              aria-pressed={showThumbnail}
            >
              Show Thumbnail
            </button>
          </div>
        </div>
      </div>

      <div className="tracks-section">
        <h2 className="section-title">Tracks</h2>
        <div className="tracks-grid">
          {playlist.tracks.map((track: Track) => (
            <div key={track.id} className="track-card">
              <div className="track-thumbnail">
                {(() => {
                  const media = mediaByPosition[track.playlistPosition];
                  const videos = media?.videos ?? [];
                  const images = media?.images ?? [];
                  const thumbnailImages = images.filter((image) =>
                    /_thumbnail(?:_\d+)?\.[^.]+$/i.test(image.fileName)
                  );
                  const displayImages = showThumbnail ? thumbnailImages : images;
                  const currentIndex = imageIndexByPosition[track.playlistPosition] ?? 0;
                  const viewMode = showThumbnail
                    ? "image"
                    : mediaViewByPosition[track.playlistPosition] ??
                      (videos.length > 0 ? "video" : "image");

                  if (showThumbnail && displayImages.length === 0) {
                    return (
                      <div className="thumbnail-placeholder thumbnail-missing">
                        <span className="thumbnail-missing-text">Thumbnail missing</span>
                      </div>
                    );
                  }

                  if (videos.length > 0 && viewMode === "video") {
                    const videoUrl = resolveMediaUrl(videos[0].url);
                    return (
                      <div className="track-media-wrapper">
                        <video className="track-media track-media-video" src={videoUrl} controls />
                        {displayImages.length > 0 && (
                          <button
                            type="button"
                            className="track-media-toggle"
                            onClick={() =>
                              setMediaViewByPosition((prev) => ({
                                ...prev,
                                [track.playlistPosition]: "image"
                              }))
                            }
                          >
                            Photo
                          </button>
                        )}
                      </div>
                    );
                  }

                  if (displayImages.length > 0) {
                    const safeIndex = currentIndex % displayImages.length;
                    const selectedImage = displayImages[safeIndex];
                    const imageUrl = resolveMediaUrl(selectedImage.url);
                    const isMaster = isMasterImageForPosition(selectedImage.fileName, track.playlistPosition);
                    const isSetBackgroundBusy = setBackgroundBusyByPosition[track.playlistPosition] === true;
                    const isDeleteThumbnailBusy = deleteThumbnailBusyByPosition[track.playlistPosition] === true;
                    return (
                      <div className="track-media-wrapper">
                        <img
                          className="track-media track-media-image"
                          src={imageUrl}
                          alt={track.title}
                          onClick={() =>
                            setLightboxState({
                              position: track.playlistPosition,
                              index: safeIndex
                            })
                          }
                        />
                        {!showThumbnail && videos.length > 0 && (
                          <button
                            type="button"
                            className="track-media-toggle"
                            onClick={() =>
                              setMediaViewByPosition((prev) => ({
                                ...prev,
                                [track.playlistPosition]: "video"
                              }))
                            }
                          >
                            Video
                          </button>
                        )}
                        {!showThumbnail && (
                          <button
                            type="button"
                            className="track-media-set-background"
                            onClick={() => handleSetBackground(track.playlistPosition, selectedImage.fileName)}
                            disabled={isMaster || isSetBackgroundBusy}
                          >
                            {isMaster ? "Master" : isSetBackgroundBusy ? "Setting..." : "Set Background"}
                          </button>
                        )}
                        {showThumbnail && (
                          <button
                            type="button"
                            className="track-media-delete-thumbnail"
                            onClick={() => handleDeleteThumbnail(track.playlistPosition, selectedImage.fileName)}
                            disabled={isDeleteThumbnailBusy}
                          >
                            {isDeleteThumbnailBusy ? "Deleting..." : "Delete Thumbnail"}
                          </button>
                        )}
                        {displayImages.length > 1 && (
                          <div className="track-media-controls">
                            <button
                              type="button"
                              className="track-media-button"
                              onClick={() =>
                                setImageIndexByPosition((prev) => ({
                                  ...prev,
                                  [track.playlistPosition]:
                                    (safeIndex - 1 + displayImages.length) % displayImages.length
                                }))
                              }
                              aria-label="Previous image"
                            >
                              ←
                            </button>
                            <span className="track-media-counter">
                              {safeIndex + 1}/{displayImages.length}
                            </span>
                            <button
                              type="button"
                              className="track-media-button"
                              onClick={() =>
                                setImageIndexByPosition((prev) => ({
                                  ...prev,
                                  [track.playlistPosition]: (safeIndex + 1) % displayImages.length
                                }))
                              }
                              aria-label="Next image"
                            >
                              →
                            </button>
                          </div>
                        )}
                      </div>
                    );
                  }

                  return (
                    <div className="thumbnail-placeholder">
                      <span className="play-icon">▶</span>
                    </div>
                  );
                })()}
              </div>
              <div className="track-content">
                <div className="track-position">#{track.playlistPosition}</div>
                <h3 className="track-title">{track.title}</h3>
                {track.youTubeTitle && (
                  <p className="track-youtube-title">📺 {track.youTubeTitle}</p>
                )}
                <div className="track-details">
                  {track.style && (
                    <span className="track-tag tag-style">{track.style}</span>
                  )}
                  {track.key && (
                    <span className="track-tag tag-key">{track.key}</span>
                  )}
                  {track.tempoBpm && (
                    <span className="track-tag tag-bpm">{track.tempoBpm} BPM</span>
                  )}
                </div>
                {track.energyLevel && (
                  <div className="energy-bar">
                    <div className="energy-label">Energy</div>
                    <div className="energy-track">
                      <div
                        className="energy-fill"
                        style={{
                          width: `${track.energyLevel * 10}%`,
                          backgroundColor: getEnergyColor(track.energyLevel)
                        }}
                      ></div>
                    </div>
                    <div className="energy-value">{track.energyLevel}/10</div>
                  </div>
                )}
                {(() => {
                  const media = mediaByPosition[track.playlistPosition];
                  const videoGeneration = videoGenerationsByPosition[track.playlistPosition];
                  const audios = media?.audios ?? [];
                  const showVideoProgress =
                    playlist.status === "VideoInProgress" &&
                    videoGeneration &&
                    videoGeneration.status.toLowerCase() === "in_progress";
                  if (audios.length === 0) {
                    return showVideoProgress ? (
                      <div className="track-video-progress">
                        <div className="track-video-progress-header">
                          <span>Video {videoGeneration.progressPercent}%</span>
                          <span>
                            {videoGeneration.progressCurrentFrame ?? 0}
                            /
                            {videoGeneration.progressTotalFrames ?? 0}
                          </span>
                        </div>
                        <div className="track-video-progress-track">
                          <div
                            className="track-video-progress-fill"
                            style={{ width: `${Math.max(0, Math.min(100, videoGeneration.progressPercent))}%` }}
                          />
                        </div>
                      </div>
                    ) : null;
                  }

                  return (
                    <div className="track-audio-list">
                      {audios.map((audio) => (
                        <TrackAudioPlayer
                          key={audio.fileName}
                          src={resolveMediaUrl(audio.url)}
                          label={audio.fileName}
                          onMove={() => handleMoveAudio(track.playlistPosition, audio.fileName)}
                          onDelete={() => handleDeleteAudio(track.playlistPosition, audio.fileName)}
                          moveBusy={moveAudioBusyByKey[`${track.playlistPosition}:${audio.fileName}`] === true}
                          deleteBusy={deleteAudioBusyByKey[`${track.playlistPosition}:${audio.fileName}`] === true}
                        />
                      ))}
                      {showVideoProgress && (
                        <div className="track-video-progress">
                          <div className="track-video-progress-header">
                            <span>Video {videoGeneration.progressPercent}%</span>
                            <span>
                              {videoGeneration.progressCurrentFrame ?? 0}
                              /
                              {videoGeneration.progressTotalFrames ?? 0}
                            </span>
                          </div>
                          <div className="track-video-progress-track">
                            <div
                              className="track-video-progress-fill"
                              style={{ width: `${Math.max(0, Math.min(100, videoGeneration.progressPercent))}%` }}
                            />
                          </div>
                        </div>
                      )}
                    </div>
                  );
                })()}
                <div className="track-status">
                  {(() => {
                    const videoGeneration = videoGenerationsByPosition[track.playlistPosition];
                    const showVideoProgress =
                      playlist.status === "VideoInProgress" &&
                      videoGeneration &&
                      videoGeneration.status.toLowerCase() === "in_progress";

                    if (showVideoProgress) {
                      return null;
                    }

                    if (playlist.youtubePlaylistId) {
                      const loopCount = loopCountByTrackId[track.id] ?? 2;
                      const loopBusy = createLoopBusyByTrackId[track.id] === true;
                      const scheduledJobId = createLoopJobIdByTrackId[track.id];

                      return (
                        <div className="track-loop-controls">
                          <label className="track-loop-input-wrap">
                            <span className="track-loop-label">Loops</span>
                            <input
                              type="number"
                              min={2}
                              step={1}
                              value={loopCount}
                              onChange={(event) =>
                                setLoopCountByTrackId((prev) => ({
                                  ...prev,
                                  [track.id]: Math.max(2, Number.parseInt(event.target.value || "2", 10) || 2)
                                }))
                              }
                              className="track-loop-input"
                            />
                          </label>
                          <button
                            type="button"
                            className="track-loop-btn"
                            onClick={() => handleCreateLoop(track)}
                            disabled={loopBusy}
                          >
                            {loopBusy ? "Creating..." : "Create Loop"}
                          </button>
                          {scheduledJobId && (
                            <span className="track-loop-scheduled">Scheduled</span>
                          )}
                        </div>
                      );
                    }

                    return (
                      <span className={`badge badge-${track.status.toLowerCase()}`}>
                        {track.status}
                      </span>
                    );
                  })()}
                  {mediaLoading && (
                    <span className="track-media-status">Loading media...</span>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="additional-info">
        <div className="info-card">
          <h3>Production Pipeline</h3>
          <p>Track generation and video rendering status will appear here.</p>
        </div>
        <div className="info-card">
          <h3>Publishing</h3>
          <p>YouTube upload progress and metadata will be displayed here.</p>
        </div>
      </div>

      {lightboxState &&
        (() => {
          const media = mediaByPosition[lightboxState.position];
          const images = media?.images ?? [];
          const thumbnailImages = images.filter((image) =>
            /_thumbnail(?:_\d+)?\.[^.]+$/i.test(image.fileName)
          );
          const displayImages = showThumbnail ? thumbnailImages : images;
          if (displayImages.length === 0) {
            return null;
          }

          const safeIndex = ((lightboxState.index % displayImages.length) + displayImages.length) % displayImages.length;
          const selectedImage = displayImages[safeIndex];
          const imageUrl = resolveMediaUrl(selectedImage.url);
          const busy = setBackgroundBusyByPosition[lightboxState.position] === true;
          const moveBusy = moveImageBusyByPosition[lightboxState.position] === true;
          const deleteBusy = deleteThumbnailBusyByPosition[lightboxState.position] === true;
          const isMaster = isMasterImageForPosition(selectedImage.fileName, lightboxState.position);

          return (
            <div
              className="media-lightbox"
              role="dialog"
              aria-modal="true"
              onClick={() => setLightboxState(null)}
            >
              <div className="media-lightbox-content" onClick={(event) => event.stopPropagation()}>
                <button
                  type="button"
                  className="media-lightbox-close"
                  onClick={() => setLightboxState(null)}
                  aria-label="Close image preview"
                >
                  ×
                </button>
                <img
                  className="media-lightbox-image"
                  src={imageUrl}
                  alt={`Track ${lightboxState.position}`}
                  onClick={() => setLightboxState(null)}
                />
                <div className="media-lightbox-toolbar">
                  <button
                    type="button"
                    className="media-lightbox-button"
                    onClick={() =>
                      setLightboxState((prev) =>
                        prev
                          ? {
                              ...prev,
                              index: (safeIndex - 1 + displayImages.length) % displayImages.length
                            }
                          : prev
                      )
                    }
                    disabled={displayImages.length <= 1}
                  >
                    ←
                  </button>
                  <span className="media-lightbox-counter">
                    {safeIndex + 1}/{displayImages.length}
                  </span>
                  <button
                    type="button"
                    className="media-lightbox-button"
                    onClick={() =>
                      setLightboxState((prev) =>
                        prev
                          ? {
                              ...prev,
                              index: (safeIndex + 1) % displayImages.length
                            }
                          : prev
                      )
                    }
                    disabled={displayImages.length <= 1}
                  >
                    →
                  </button>
                  {!showThumbnail && (
                    <button
                      type="button"
                      className="media-lightbox-button media-lightbox-primary"
                      onClick={async () => {
                        const changed = await handleSetBackground(lightboxState.position, selectedImage.fileName);
                        if (changed) {
                          setLightboxState((prev) =>
                            prev
                              ? {
                                  ...prev,
                                  index: 0
                                }
                              : prev
                          );
                        }
                      }}
                      disabled={isMaster || busy}
                    >
                      {isMaster ? "Master" : busy ? "Setting..." : "Set Background"}
                    </button>
                  )}
                  {!showThumbnail && (
                    <button
                      type="button"
                      className="media-lightbox-button"
                      onClick={async () => {
                        const moved = await handleMoveImage(lightboxState.position, selectedImage.fileName);
                        if (moved) {
                          setLightboxState((prev) =>
                            prev
                              ? {
                                  ...prev,
                                  index: 0
                                }
                              : prev
                          );
                        }
                      }}
                      disabled={moveBusy}
                    >
                      {moveBusy ? "Moving..." : "Move"}
                    </button>
                  )}
                  {showThumbnail && (
                    <button
                      type="button"
                      className="media-lightbox-button"
                      onClick={async () => {
                        const deleted = await handleDeleteThumbnail(lightboxState.position, selectedImage.fileName);
                        if (deleted) {
                          setLightboxState((prev) =>
                            prev
                              ? {
                                  ...prev,
                                  index: 0
                                }
                              : prev
                          );
                        }
                      }}
                      disabled={deleteBusy}
                    >
                      {deleteBusy ? "Deleting..." : "Delete Thumbnail"}
                    </button>
                  )}
                </div>
              </div>
            </div>
          );
        })()}
    </div>
  );
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "0:00";
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

function TrackAudioPlayer({
  src,
  label,
  onMove,
  onDelete,
  moveBusy,
  deleteBusy
}: {
  src: string;
  label: string;
  onMove: () => Promise<boolean>;
  onDelete: () => Promise<boolean>;
  moveBusy: boolean;
  deleteBusy: boolean;
}) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [isSeeking, setIsSeeking] = useState(false);
  const [seekValue, setSeekValue] = useState(0);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const handleTime = () => {
      if (!isSeeking) {
        setCurrentTime(audio.currentTime);
      }
    };
    const handleLoaded = () => setDuration(audio.duration || 0);
    const handleEnded = () => setIsPlaying(false);

    audio.addEventListener("timeupdate", handleTime);
    audio.addEventListener("loadedmetadata", handleLoaded);
    audio.addEventListener("ended", handleEnded);

    return () => {
      audio.removeEventListener("timeupdate", handleTime);
      audio.removeEventListener("loadedmetadata", handleLoaded);
      audio.removeEventListener("ended", handleEnded);
    };
  }, [isSeeking]);

  const togglePlay = async () => {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      await audio.play();
      setIsPlaying(true);
    } else {
      audio.pause();
      setIsPlaying(false);
    }
  };

  const handleSeek = (value: number) => {
    const audio = audioRef.current;
    if (!audio || !Number.isFinite(audio.duration)) return;
    const nextTime = (value / 100) * audio.duration;
    audio.currentTime = nextTime;
    setCurrentTime(nextTime);
    setSeekValue(value);
  };

  const progress = duration > 0 ? (currentTime / duration) * 100 : 0;
  const displayedProgress = isSeeking ? seekValue : progress;

  return (
    <div className="track-audio-shell">
      <audio ref={audioRef} src={src} preload="metadata" />
      <div className="track-audio-actions track-audio-actions-top">
        <button
          type="button"
          className="track-audio-action-btn"
          onClick={() => {
            void onMove();
          }}
          disabled={moveBusy || deleteBusy}
        >
          {moveBusy ? "Moving..." : "Move"}
        </button>
        <button
          type="button"
          className="track-audio-action-btn track-audio-action-danger"
          onClick={() => {
            void onDelete();
          }}
          disabled={deleteBusy || moveBusy}
        >
          {deleteBusy ? "Deleting..." : "Delete"}
        </button>
      </div>
      <div className="track-audio-main">
        <button
          type="button"
          className="track-audio-play"
          onClick={togglePlay}
          aria-label={isPlaying ? "Pause" : "Play"}
        >
          {isPlaying ? (
            <span className="track-audio-icon track-audio-icon-pause">❚❚</span>
          ) : (
            <span className="track-audio-icon track-audio-icon-play">▶</span>
          )}
        </button>
        <div className="track-audio-time">
          {formatTime(currentTime)} / {formatTime(duration)}
        </div>
        <input
          className="track-audio-scrub"
          type="range"
          min={0}
          max={100}
          step={0.1}
          value={displayedProgress}
          onPointerDown={() => {
            setIsSeeking(true);
            setSeekValue(progress);
          }}
          onPointerUp={() => setIsSeeking(false)}
          onTouchStart={() => {
            setIsSeeking(true);
            setSeekValue(progress);
          }}
          onTouchEnd={() => setIsSeeking(false)}
          onBlur={() => setIsSeeking(false)}
          onInput={(event) => handleSeek(Number((event.target as HTMLInputElement).value))}
          onChange={(event) => handleSeek(Number(event.target.value))}
          aria-label={`Scrub ${label}`}
        />
      </div>
    </div>
  );
}
