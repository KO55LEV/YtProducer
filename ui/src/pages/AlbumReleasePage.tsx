import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type { AlbumRelease, Playlist, ScheduleAlbumReleaseJobResponse, ScheduleDeleteAlbumReleaseTempFilesResponse } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function formatDuration(totalSeconds: number): string {
  if (!Number.isFinite(totalSeconds) || totalSeconds <= 0) {
    return "0:00";
  }

  const safe = Math.max(0, Math.round(totalSeconds));
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  const seconds = safe % 60;
  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
  }

  return `${minutes}:${seconds.toString().padStart(2, "0")}`;
}

export default function AlbumReleasePage() {
  const { id } = useParams<{ id: string }>();
  const [playlist, setPlaylist] = useState<Playlist | null>(null);
  const [albumRelease, setAlbumRelease] = useState<AlbumRelease | null>(null);
  const [coverLightboxOpen, setCoverLightboxOpen] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [regeneratingThumbnail, setRegeneratingThumbnail] = useState(false);
  const [generatingAssets, setGeneratingAssets] = useState(false);
  const [uploadingRelease, setUploadingRelease] = useState(false);
  const [deleteTempBusy, setDeleteTempBusy] = useState(false);
  const [generateJobId, setGenerateJobId] = useState<string | null>(null);
  const [uploadJobId, setUploadJobId] = useState<string | null>(null);
  const [deleteTempJobId, setDeleteTempJobId] = useState<string | null>(null);

  const generatedThumbnailUrl = useMemo(() => {
    if (!albumRelease?.thumbnailUrl) {
      return null;
    }

    const baseUrl = albumRelease.thumbnailUrl.startsWith("http")
      ? albumRelease.thumbnailUrl
      : `${apiBaseUrl}${albumRelease.thumbnailUrl}`;
    const separator = baseUrl.includes("?") ? "&" : "?";
    return `${baseUrl}${separator}v=${encodeURIComponent(albumRelease.updatedAtUtc)}`;
  }, [albumRelease]);

  async function loadPage(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setLoading(true);
      const [playlistResponse, albumReleaseResponse] = await Promise.all([
        fetch(`${apiBaseUrl}/playlists/${id}`),
        fetch(`${apiBaseUrl}/playlists/${id}/album-release`)
      ]);

      if (!playlistResponse.ok) {
        throw new Error(`Playlist request failed with status ${playlistResponse.status}`);
      }

      if (!albumReleaseResponse.ok) {
        throw new Error(`Album release request failed with status ${albumReleaseResponse.status}`);
      }

      const playlistData = (await playlistResponse.json()) as Playlist;
      const releaseData = (await albumReleaseResponse.json()) as AlbumRelease;

      setPlaylist(playlistData);
      setAlbumRelease(releaseData);
      setTitle(releaseData.title);
      setDescription(releaseData.description ?? "");
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load album release");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadPage();
  }, [id]);

  async function handleSaveDraft(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setSaving(true);
      setError(null);
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/album-release`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ title, description })
      });

      if (!response.ok) {
        throw new Error(`Save draft failed with status ${response.status}`);
      }

      const data = (await response.json()) as AlbumRelease;
      setAlbumRelease(data);
      setTitle(data.title);
      setDescription(data.description ?? "");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save draft failed");
    } finally {
      setSaving(false);
    }
  }

  async function handleRegenerateThumbnail(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setRegeneratingThumbnail(true);
      setError(null);
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/album-release/regenerate-thumbnail`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Regenerate thumbnail failed with status ${response.status}`);
      }

      const data = (await response.json()) as AlbumRelease;
      setAlbumRelease(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Regenerate thumbnail failed");
    } finally {
      setRegeneratingThumbnail(false);
    }
  }

  async function handleDeleteTempFiles(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setDeleteTempBusy(true);
      setError(null);
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/album-release/delete-temp-files`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Delete temp files failed with status ${response.status}`);
      }

      const data = (await response.json()) as ScheduleDeleteAlbumReleaseTempFilesResponse;
      setDeleteTempJobId(data.jobId);
      await loadPage();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Delete temp files failed");
    } finally {
      setDeleteTempBusy(false);
    }
  }

  async function handleGenerateAssets(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setGeneratingAssets(true);
      setError(null);
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/album-release/generate-assets`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Generate album assets failed with status ${response.status}`);
      }

      const data = (await response.json()) as ScheduleAlbumReleaseJobResponse;
      setGenerateJobId(data.jobId);
      await loadPage();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Generate album assets failed");
    } finally {
      setGeneratingAssets(false);
    }
  }

  async function handleUploadRelease(): Promise<void> {
    if (!id) {
      return;
    }

    try {
      setUploadingRelease(true);
      setError(null);
      const response = await fetch(`${apiBaseUrl}/playlists/${id}/album-release/upload-youtube`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Upload album release failed with status ${response.status}`);
      }

      const data = (await response.json()) as ScheduleAlbumReleaseJobResponse;
      setUploadJobId(data.jobId);
      await loadPage();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload album release failed");
    } finally {
      setUploadingRelease(false);
    }
  }

  if (loading) {
    return (
      <div className="page-content">
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading album release...</p>
        </div>
      </div>
    );
  }

  if (error || !playlist || !albumRelease) {
    return (
      <div className="page-content">
        <div className="alert alert-error">
          <strong>Error:</strong> {error || "Album release not found"}
        </div>
        <Link to={id ? `/playlists/${id}` : "/playlists"} className="btn btn-secondary">← Back to Playlist</Link>
      </div>
    );
  }

  return (
    <div className="page-content">
      <Link to={`/playlists/${playlist.id}`} className="breadcrumb">← Back to Playlist</Link>

      <section className="album-release-hero">
        <div className="album-release-hero-copy">
          <span className="album-release-kicker">Album Release</span>
          <h1 className="page-title">{playlist.title}</h1>
          <p className="page-subtitle">
            Build a longform release draft from your full playlist, preview the cover concept, and prepare the album before generating the merged video.
          </p>
          <div className="album-release-stats">
            <span>{albumRelease.trackCount} tracks</span>
            <span>{formatDuration(albumRelease.totalDurationSeconds)} total length</span>
            <span>Status: {albumRelease.status}</span>
            {playlist.youtubePlaylistId && <span>Playlist linked on YouTube</span>}
          </div>
        </div>

        <div className="album-release-cover-shell">
          <div className="album-release-cover">
            {generatedThumbnailUrl ? (
              <button
                type="button"
                className="album-release-cover-generated album-release-cover-trigger"
                onClick={() => setCoverLightboxOpen(true)}
                aria-label="Open album thumbnail preview"
              >
                <img src={generatedThumbnailUrl} alt={`${title} thumbnail`} />
              </button>
            ) : (
              <div className="album-release-cover-empty">
                <span>No album thumbnail generated yet</span>
              </div>
            )}
          </div>
          <div className="album-release-cover-actions">
            <button
              type="button"
              className="playlist-action-btn"
              onClick={() => void handleRegenerateThumbnail()}
              disabled={regeneratingThumbnail}
            >
              {regeneratingThumbnail ? "Regenerating..." : "Regenerate Thumbnail"}
            </button>
            <button
              type="button"
              className="playlist-action-btn playlist-action-btn-secondary"
              onClick={() => void handleDeleteTempFiles()}
              disabled={deleteTempBusy}
            >
              {deleteTempBusy ? "Scheduling..." : "Delete Temp Files"}
            </button>
            {deleteTempJobId && <span className="playlist-action-status">Cleanup scheduled</span>}
          </div>
        </div>
      </section>

      {coverLightboxOpen && generatedThumbnailUrl && (
        <div className="media-lightbox" role="dialog" aria-modal="true" onClick={() => setCoverLightboxOpen(false)}>
          <div className="media-lightbox-content" onClick={(event) => event.stopPropagation()}>
            <button
              type="button"
              className="media-lightbox-close"
              onClick={() => setCoverLightboxOpen(false)}
              aria-label="Close album thumbnail preview"
            >
              ×
            </button>
            <img
              className="media-lightbox-image"
              src={generatedThumbnailUrl}
              alt={`${title} full preview`}
              onClick={() => setCoverLightboxOpen(false)}
            />
          </div>
        </div>
      )}

      <section className="album-release-layout">
        <div className="album-release-main">
          <section className="prompt-card-shell album-release-card">
            <div className="prompt-card-header album-release-card-header">
              <div>
                <h2 className="section-title">Release Draft</h2>
                <p className="prompt-card-subtitle">
                  Lock in the title and release copy before the album is generated.
                </p>
              </div>
              <button type="button" className="btn btn-primary" onClick={() => void handleSaveDraft()} disabled={saving}>
                {saving ? "Saving..." : "Save Draft"}
              </button>
            </div>

            <div className="album-release-form-grid">
              <label className="prompt-field">
                <span>YouTube Title</span>
                <input value={title} onChange={(event) => setTitle(event.target.value)} />
              </label>
              <label className="prompt-field prompt-field-full">
                <span>Description</span>
                <textarea rows={10} value={description} onChange={(event) => setDescription(event.target.value)} />
              </label>
            </div>
          </section>

          <section className="prompt-card-shell album-release-card">
            <div className="prompt-card-header album-release-card-header">
              <div>
                <h2 className="section-title">Generated Album</h2>
                <p className="prompt-card-subtitle">
                  Once prepared, this is the merged longform video that will be uploaded as the album release.
                </p>
              </div>
              <button type="button" className="btn btn-secondary" onClick={() => void loadPage()}>
                Refresh
              </button>
            </div>

            {albumRelease.outputVideoUrl ? (
              <video className="album-release-video" controls src={`${apiBaseUrl}${albumRelease.outputVideoUrl}`} />
            ) : (
              <div className="album-release-video-empty">
                Generate album assets to preview the full merged release here.
              </div>
            )}
          </section>

          <section className="prompt-card-shell album-release-card">
            <div className="prompt-card-header album-release-card-header">
              <div>
                <h2 className="section-title">Release Timeline</h2>
                <p className="prompt-card-subtitle">
                  This combined track order becomes the album timestamps and longform tracklist.
                </p>
              </div>
            </div>

            <div className="album-release-tracklist">
              {albumRelease.tracks.map((track) => (
                <article key={track.trackId} className="album-release-track-row">
                  <div className="album-release-track-time">{track.startOffsetLabel}</div>
                  <div className="album-release-track-body">
                    <strong>{track.playlistPosition}. {track.title}</strong>
                    <span>{track.duration || formatDuration(track.durationSeconds)}</span>
                  </div>
                </article>
              ))}
            </div>
          </section>
        </div>

        <aside className="album-release-sidebar">
          <section className="prompt-card-shell album-release-card">
            <div className="prompt-card-header album-release-card-header">
              <div>
                <h2 className="section-title">Preparation Stage</h2>
                <p className="prompt-card-subtitle">
                  Prepare the merged album, review the generated media, then publish it when the release looks right.
                </p>
              </div>
            </div>

            <div className="album-release-stage-list">
              <div className="album-release-stage-item">
                <strong>Thumbnail Draft</strong>
                <span>Available now with regenerate support.</span>
              </div>
              <div className="album-release-stage-item">
                <strong>Album Video</strong>
                <span>{albumRelease.outputVideoPath ? "Generated file available for playback." : "Generate assets to build the merged release video."}</span>
              </div>
              <div className="album-release-stage-item">
                <strong>Publishing</strong>
                <span>{albumRelease.youtubeVideoId ? "Uploaded to YouTube." : "Upload is enabled once the merged video exists."}</span>
              </div>
            </div>

            <div className="album-release-stage-actions">
              <button type="button" className="btn btn-primary" onClick={() => void handleGenerateAssets()} disabled={generatingAssets}>
                {generatingAssets ? "Scheduling..." : "Generate Album Video"}
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={() => void handleUploadRelease()}
                disabled={uploadingRelease || !albumRelease.outputVideoPath}
              >
                {uploadingRelease ? "Scheduling..." : "Upload Album"}
              </button>
            </div>
            {generateJobId && <span className="playlist-action-status">Generation scheduled</span>}
            {uploadJobId && <span className="playlist-action-status">Upload scheduled</span>}

            <div className="album-release-temp-card">
              <span className="album-release-temp-label">Temp Workspace</span>
              <strong>{albumRelease.tempFilesExist ? `${albumRelease.tempFileCount} files ready` : "No temp files currently stored"}</strong>
              <p>{albumRelease.tempRootPath || "Temp root will be used once album generation starts."}</p>
            </div>
          </section>
        </aside>
      </section>
    </div>
  );
}
