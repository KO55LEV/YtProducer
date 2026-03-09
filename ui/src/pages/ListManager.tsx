import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import type { Playlist, PlaylistMediaResponse } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function resolveMediaUrl(url: string): string {
  return url.startsWith("http") ? url : `${apiBaseUrl}${url}`;
}

function pickPlaylistPreviewImages(media: PlaylistMediaResponse): string[] {
  return media.tracks
    .sort((a, b) => a.playlistPosition - b.playlistPosition)
    .flatMap((track) =>
      track.images
        .filter((image) => !/_thumbnail(?:_\d+)?\.[^.]+$/i.test(image.fileName))
        .map((image) => resolveMediaUrl(image.url))
    )
    .slice(0, 4);
}

function deriveTrackTitle(track: Record<string, unknown>, fallbackPosition: number): string | null {
  const directTitle = typeof track.title === "string" ? track.title.trim() : "";
  if (directTitle) {
    return directTitle;
  }

  const youtubeTitle = typeof track.youtube_title === "string" ? track.youtube_title.trim() : "";
  if (youtubeTitle) {
    const cleaned = youtubeTitle
      .split("|")[0]
      .split("⚡")[0]
      .replace(/\s+/g, " ")
      .trim();

    if (cleaned) {
      return cleaned;
    }
  }

  return `Track ${fallbackPosition}`;
}

type NormalizedPlaylistUpload = {
  title: string;
  theme: string | null;
  description: string | null;
  playlistStrategy: string | null;
  metadata: string;
  tracks: Array<{
    playlistPosition: number;
    title: string;
    youTubeTitle: string | null;
    style: string | null;
    duration: string | null;
    tempoBpm: number | null;
    key: string | null;
    energyLevel: number | null;
    metadata: string;
  }>;
};

function readText(value: unknown): string | null {
  return typeof value === "string" && value.trim() ? value.trim() : null;
}

function normalizePlaylistUpload(json: Record<string, unknown>): NormalizedPlaylistUpload {
  const sourceTracks = Array.isArray(json.tracks) ? json.tracks : [];
  if (sourceTracks.length === 0) {
    throw new Error("Playlist JSON must contain a non-empty tracks array.");
  }

  const normalizedTracks = sourceTracks.map((item, index) => {
    if (!item || typeof item !== "object") {
      throw new Error(`Track ${index + 1} is not a valid JSON object.`);
    }

    const track = item as Record<string, unknown>;
    const playlistPosition =
      typeof track.playlist_position === "number"
        ? track.playlist_position
        : typeof track.track_number === "number"
          ? track.track_number
          : index + 1;

    const title = deriveTrackTitle(track, playlistPosition);
    const youTubeTitle = readText(track.youtube_title);
    const musicGenerationPrompt = readText(track.music_generation_prompt);
    const imagePrompt = readText(track.image_prompt) ?? readText(track.thumbnail_image_prompt);

    if (!title) {
      throw new Error(`Track ${playlistPosition} is missing title data.`);
    }

    if (!musicGenerationPrompt) {
      throw new Error(`Track ${playlistPosition} is missing music_generation_prompt.`);
    }

    if (!imagePrompt) {
      throw new Error(`Track ${playlistPosition} is missing image_prompt or thumbnail_image_prompt.`);
    }

    return {
      playlistPosition,
      title,
      youTubeTitle,
      style: readText(track.style_summary) ?? readText(track.style),
      duration:
        track.duration_seconds != null
          ? String(track.duration_seconds)
          : readText(track.duration),
      tempoBpm: typeof track.tempo_bpm === "number" ? track.tempo_bpm : null,
      key: readText(track.key),
      energyLevel: typeof track.energy_level === "number" ? track.energy_level : null,
      metadata: JSON.stringify({
        titleViralityScore: track.title_virality_score,
        hookStrengthScore: track.hook_strength_score,
        thumbnailCtrScore: track.thumbnail_ctr_score,
        hookType: track.hook_type,
        songStructure: track.song_structure,
        energyCurve: track.energy_curve,
        listeningScenario: track.listening_scenario,
        targetAudience: track.target_audience,
        thumbnailEmotion: track.thumbnail_emotion,
        thumbnailColorPalette: track.thumbnail_color_palette,
        thumbnailTextHint: track.thumbnail_text_hint,
        playlistCategory: track.playlist_category,
        instruments: track.instruments,
        visualStyleHint: track.visual_style_hint,
        lyrics: track.lyrics,
        musicGenerationPrompt,
        imagePrompt,
        youtubeDescription: track.youtube_description,
        youtubeTags: track.youtube_tags
      })
    };
  });

  return {
    title: readText(json.playlist_title) ?? readText(json.theme) ?? "Untitled Playlist",
    theme: readText(json.theme),
    description: readText(json.playlist_description) ?? readText(json.playlist_strategy),
    playlistStrategy: readText(json.playlist_strategy),
    metadata: JSON.stringify({
      targetPlatform: readText(json.target_platform)
    }),
    tracks: normalizedTracks
  };
}

function statusClassName(status: string): string {
  switch (status.toLowerCase()) {
    case "completed":
      return "badge badge-success";
    case "active":
      return "badge badge-primary";
    case "failed":
      return "badge badge-error";
    case "foldercreated":
      return "badge badge-foldercreated";
    case "imagesgenerated":
      return "badge badge-imagesgenerated";
    case "musicgenerated":
      return "badge badge-musicgenerated";
    case "thumbnailgenerated":
      return "badge badge-thumbnailgenerated";
    case "onyoutube":
      return "badge badge-onyoutube";
    default:
      return "badge badge-secondary";
  }
}

export default function ListManager() {
  const navigate = useNavigate();
  const [playlists, setPlaylists] = useState<Playlist[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const [startingByPlaylistId, setStartingByPlaylistId] = useState<Record<string, boolean>>({});
  const [startJobIdByPlaylistId, setStartJobIdByPlaylistId] = useState<Record<string, string>>({});
  const [previewImagesByPlaylistId, setPreviewImagesByPlaylistId] = useState<Record<string, string[]>>({});

  useEffect(() => {
    void fetchPlaylists();
  }, []);

  async function fetchPlaylists(): Promise<void> {
    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/playlists`);

      if (!response.ok) {
        throw new Error(`Request failed with status ${response.status}`);
      }

      const data = (await response.json()) as Playlist[];
      setPlaylists(data);
      void fetchPlaylistPreviewImages(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  async function fetchPlaylistPreviewImages(items: Playlist[]): Promise<void> {
    const previews = await Promise.all(
      items.map(async (playlist) => {
        try {
          const response = await fetch(`${apiBaseUrl}/playlists/${playlist.id}/media`);
          if (!response.ok) {
            return [playlist.id, []] as const;
          }

          const media = (await response.json()) as PlaylistMediaResponse;
          return [playlist.id, pickPlaylistPreviewImages(media)] as const;
        } catch {
          return [playlist.id, []] as const;
        }
      })
    );

    setPreviewImagesByPlaylistId(Object.fromEntries(previews));
  }

  async function handleFileUpload(event: React.ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      setUploading(true);
      setError(null);

      const text = await file.text();
      const parsed = JSON.parse(text) as Record<string, unknown>;
      const payload = normalizePlaylistUpload(parsed);

      const response = await fetch(`${apiBaseUrl}/playlists`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        let message = `Upload failed with status ${response.status}`;

        try {
          const data = (await response.json()) as { message?: string };
          if (typeof data.message === "string" && data.message.trim()) {
            message = data.message;
          }
        } catch {
          // Ignore non-JSON error bodies.
        }

        throw new Error(message);
      }

      await fetchPlaylists();
      event.target.value = "";
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  }

  async function handleStart(playlistId: string): Promise<void> {
    try {
      setStartingByPlaylistId((current) => ({ ...current, [playlistId]: true }));
      setError(null);

      const response = await fetch(`${apiBaseUrl}/playlists/${playlistId}/start`, {
        method: "POST"
      });

      if (!response.ok) {
        throw new Error(`Start failed with status ${response.status}`);
      }

      const data = (await response.json()) as { jobId: string };
      setStartJobIdByPlaylistId((current) => ({ ...current, [playlistId]: data.jobId }));
    } catch (err) {
      setError(err instanceof Error ? err.message : "Start failed");
    } finally {
      setStartingByPlaylistId((current) => ({ ...current, [playlistId]: false }));
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section">
        <div>
          <h1 className="page-title">Playlist Manager</h1>
          <p className="page-subtitle">Manage and orchestrate your YouTube playlist production</p>
        </div>
        <div className="upload-section playlist-manager-actions">
          <Link to="/youtube-playlists" className="btn btn-secondary playlist-manager-btn playlist-manager-btn-secondary">
            YouTube Playlists
          </Link>
          <label htmlFor="file-upload" className="btn btn-primary playlist-manager-btn playlist-manager-btn-primary">
            {uploading ? "Uploading..." : "Upload JSON"}
          </label>
          <input
            id="file-upload"
            type="file"
            accept=".json"
            onChange={handleFileUpload}
            disabled={uploading}
            style={{ display: "none" }}
          />
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <strong>Error:</strong> {error}
        </div>
      )}

      {loading ? (
        <div className="loading-state">
          <div className="spinner"></div>
          <p>Loading playlists...</p>
        </div>
      ) : playlists.length === 0 ? (
        <div className="empty-state">
          <div className="empty-icon">📁</div>
          <h3>No playlists yet</h3>
          <p>Upload a JSON file to create your first playlist</p>
        </div>
      ) : (
        <div className="playlist-grid">
          {playlists.map((playlist) => (
            <article
              key={playlist.id}
              className="playlist-card"
              onClick={() => navigate(`/playlists/${playlist.id}`)}
              role="button"
              tabIndex={0}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  navigate(`/playlists/${playlist.id}`);
                }
              }}
            >
              <div className="playlist-card-top">
                <div className="playlist-card-header">
                  <h3 className="playlist-title">{playlist.title}</h3>
                </div>
                <div className="playlist-card-side">
                  <span className={statusClassName(playlist.status)}>{playlist.status}</span>
                  {previewImagesByPlaylistId[playlist.id]?.length ? (
                    <div className="playlist-card-media">
                      <div
                        className={`playlist-card-collage playlist-card-collage-count-${previewImagesByPlaylistId[playlist.id].length}`}
                      >
                        {previewImagesByPlaylistId[playlist.id].map((imageUrl, index) => (
                          <div
                            key={`${playlist.id}-${index}`}
                            className={`playlist-card-collage-tile playlist-card-collage-tile-${index + 1}`}
                          >
                            <img src={imageUrl} alt="" loading="lazy" />
                          </div>
                        ))}
                        <div className="playlist-card-collage-overlay" />
                      </div>
                    </div>
                  ) : null}
                </div>
              </div>
              {playlist.theme && (
                <div className="playlist-theme">
                  <span className="playlist-theme-icon" aria-hidden="true">🎵</span>
                  <span>{playlist.theme}</span>
                </div>
              )}
              <p className="playlist-description">
                {playlist.description ?? "No description"}
              </p>
              <div className="playlist-footer">
                <span className="track-count">
                  <strong>{playlist.trackCount}</strong> tracks
                </span>
                <div className="playlist-footer-actions">
                  {playlist.status.toLowerCase() === "draft" && (
                    <>
                      {startJobIdByPlaylistId[playlist.id] && (
                        <span className="playlist-start-scheduled">Scheduled</span>
                      )}
                      <button
                        type="button"
                        className="playlist-start-btn"
                        onClick={(event) => {
                          event.stopPropagation();
                          void handleStart(playlist.id);
                        }}
                        disabled={startingByPlaylistId[playlist.id] === true}
                      >
                        {startingByPlaylistId[playlist.id] === true ? "Starting..." : "Start"}
                      </button>
                    </>
                  )}
                  <span className="playlist-date">
                    {new Date(playlist.createdAtUtc).toLocaleDateString()}
                  </span>
                </div>
              </div>
            </article>
          ))}
        </div>
      )}
    </div>
  );
}
