import { useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import type { Playlist, Track } from "../types";

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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
                <div className="thumbnail-placeholder">
                  <span className="play-icon">▶</span>
                </div>
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
                <div className="track-status">
                  <span className={`badge badge-${track.status.toLowerCase()}`}>
                    {track.status}
                  </span>
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
