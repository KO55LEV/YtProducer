import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { Playlist } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function statusClassName(status: string): string {
  switch (status.toLowerCase()) {
    case "completed":
      return "badge badge-success";
    case "active":
      return "badge badge-primary";
    case "failed":
      return "badge badge-error";
    default:
      return "badge badge-secondary";
  }
}

export default function ListManager() {
  const [playlists, setPlaylists] = useState<Playlist[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  useEffect(() => {
    fetchPlaylists();
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
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  async function handleFileUpload(event: React.ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.target.files?.[0];
    if (!file) return;

    try {
      setUploading(true);
      setError(null);

      const text = await file.text();
      const json = JSON.parse(text);

      // Map JSON structure to API contract
      const payload = {
        title: json.playlist_title || json.theme || "Untitled Playlist",
        theme: json.theme,
        description: json.playlist_description ?? json.playlist_strategy ?? null,
        playlistStrategy: json.playlist_strategy,
        metadata: JSON.stringify({
          targetPlatform: json.target_platform
        }),
        tracks: json.tracks?.map((track: any) => ({
          playlistPosition: track.playlist_position,
          title: track.title,
          youTubeTitle: track.youtube_title,
          style: track.style_summary ?? track.style,
          duration: track.duration_seconds != null ? String(track.duration_seconds) : track.duration,
          tempoBpm: track.tempo_bpm,
          key: track.key,
          energyLevel: track.energy_level,
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
            musicGenerationPrompt: track.music_generation_prompt,
            imagePrompt: track.image_prompt,
            youtubeDescription: track.youtube_description,
            youtubeTags: track.youtube_tags
          })
        }))
      };

      const response = await fetch(`${apiBaseUrl}/playlists`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        throw new Error(`Upload failed with status ${response.status}`);
      }

      await fetchPlaylists();
      event.target.value = "";
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setUploading(false);
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section">
        <div>
          <h1 className="page-title">Playlist Manager</h1>
          <p className="page-subtitle">Manage and orchestrate your YouTube playlist production</p>
        </div>
        <div className="upload-section">
          <label htmlFor="file-upload" className="btn btn-primary">
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
            <Link
              key={playlist.id}
              to={`/playlists/${playlist.id}`}
              className="playlist-card"
            >
              <div className="playlist-card-header">
                <h3 className="playlist-title">{playlist.title}</h3>
                <span className={statusClassName(playlist.status)}>{playlist.status}</span>
              </div>
              {playlist.theme && (
                <div className="playlist-theme">🎵 {playlist.theme}</div>
              )}
              <p className="playlist-description">
                {playlist.description ?? "No description"}
              </p>
              <div className="playlist-footer">
                <span className="track-count">
                  <strong>{playlist.trackCount}</strong> tracks
                </span>
                <span className="playlist-date">
                  {new Date(playlist.createdAtUtc).toLocaleDateString()}
                </span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}
