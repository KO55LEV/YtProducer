import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type { Playlist, PlaylistMediaResponse, PlaylistTrackMedia, Track } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function getEnergyColor(energyLevel?: number | null): string {
  if (!energyLevel) return "#6b7280";
  if (energyLevel >= 8) return "#ef4444";
  if (energyLevel >= 6) return "#f97316";
  if (energyLevel >= 4) return "#eab308";
  return "#10b981";
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
    if (!id) return;

    async function fetchMedia(): Promise<void> {
      try {
        setMediaLoading(true);
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
        setMediaViewByPosition(defaultView);
      } catch {
        setMediaByPosition({});
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
                  const audios = media?.audios ?? [];
                  const currentIndex = imageIndexByPosition[track.playlistPosition] ?? 0;
                  const viewMode = mediaViewByPosition[track.playlistPosition] ?? (videos.length > 0 ? "video" : "image");

                  if (videos.length > 0 && viewMode === "video") {
                    const videoUrl = resolveMediaUrl(videos[0].url);
                    return (
                      <div className="track-media-wrapper">
                        <video className="track-media track-media-video" src={videoUrl} controls />
                        {images.length > 0 && (
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

                  if (images.length > 0) {
                    const safeIndex = currentIndex % images.length;
                    const imageUrl = resolveMediaUrl(images[safeIndex].url);
                    return (
                      <div className="track-media-wrapper">
                        <img className="track-media track-media-image" src={imageUrl} alt={track.title} />
                        {videos.length > 0 && (
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
                        {images.length > 1 && (
                          <div className="track-media-controls">
                            <button
                              type="button"
                              className="track-media-button"
                              onClick={() =>
                                setImageIndexByPosition((prev) => ({
                                  ...prev,
                                  [track.playlistPosition]:
                                    (safeIndex - 1 + images.length) % images.length
                                }))
                              }
                              aria-label="Previous image"
                            >
                              ←
                            </button>
                            <span className="track-media-counter">
                              {safeIndex + 1}/{images.length}
                            </span>
                            <button
                              type="button"
                              className="track-media-button"
                              onClick={() =>
                                setImageIndexByPosition((prev) => ({
                                  ...prev,
                                  [track.playlistPosition]: (safeIndex + 1) % images.length
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
                {track.duration && (
                  <span className="track-duration">{track.duration}</span>
                )}
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
                  const audios = media?.audios ?? [];
                  if (audios.length === 0) {
                    return null;
                  }

                  return (
                    <div className="track-audio-list">
                      {audios.map((audio) => (
                        <TrackAudioPlayer
                          key={audio.fileName}
                          src={resolveMediaUrl(audio.url)}
                          label={audio.fileName}
                        />
                      ))}
                    </div>
                  );
                })()}
                <div className="track-status">
                  <span className={`badge badge-${track.status.toLowerCase()}`}>
                    {track.status}
                  </span>
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
    </div>
  );
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return "0:00";
  const mins = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${mins}:${secs.toString().padStart(2, "0")}`;
}

function TrackAudioPlayer({ src, label }: { src: string; label: string }) {
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);

  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const handleTime = () => setCurrentTime(audio.currentTime);
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
  }, []);

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
  };

  const progress = duration > 0 ? (currentTime / duration) * 100 : 0;

  return (
    <div className="track-audio-shell">
      <audio ref={audioRef} src={src} preload="metadata" />
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
        value={progress}
        onChange={(event) => handleSeek(Number(event.target.value))}
        aria-label={`Scrub ${label}`}
      />
    </div>
  );
}
