import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import type { YoutubePlaylist } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

type FormState = {
  youtubePlaylistId: string;
  title: string;
  description: string;
  status: string;
  privacyStatus: string;
  channelId: string;
  channelTitle: string;
  itemCount: string;
  publishedAtUtc: string;
  thumbnailUrl: string;
  etag: string;
  lastSyncedAtUtc: string;
  metadata: string;
};

const emptyForm: FormState = {
  youtubePlaylistId: "",
  title: "",
  description: "",
  status: "",
  privacyStatus: "",
  channelId: "",
  channelTitle: "",
  itemCount: "",
  publishedAtUtc: "",
  thumbnailUrl: "",
  etag: "",
  lastSyncedAtUtc: "",
  metadata: ""
};

function toNullableString(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

function parseOptionalInt(value: string): number | null {
  if (!value.trim()) return null;
  const parsed = Number.parseInt(value, 10);
  return Number.isNaN(parsed) ? null : parsed;
}

function parseOptionalDate(value: string): string | null {
  if (!value.trim()) return null;
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return null;
  return parsed.toISOString();
}

export default function YoutubePlaylists() {
  const [playlists, setPlaylists] = useState<YoutubePlaylist[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [form, setForm] = useState<FormState>(emptyForm);

  useEffect(() => {
    fetchPlaylists();
  }, []);

  async function fetchPlaylists(): Promise<void> {
    try {
      setLoading(true);
      const response = await fetch(`${apiBaseUrl}/youtube-playlists`);

      if (!response.ok) {
        throw new Error(`Request failed with status ${response.status}`);
      }

      const data = (await response.json()) as YoutubePlaylist[];
      setPlaylists(data);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  }

  function updateField(field: keyof FormState, value: string): void {
    setForm((current) => ({ ...current, [field]: value }));
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();

    if (!form.youtubePlaylistId.trim()) {
      setError("youtubePlaylistId is required");
      return;
    }

    const publishedAtUtc = parseOptionalDate(form.publishedAtUtc);
    const lastSyncedAtUtc = parseOptionalDate(form.lastSyncedAtUtc);

    if (form.publishedAtUtc.trim() && !publishedAtUtc) {
      setError("publishedAtUtc must be a valid date");
      return;
    }

    if (form.lastSyncedAtUtc.trim() && !lastSyncedAtUtc) {
      setError("lastSyncedAtUtc must be a valid date");
      return;
    }

    try {
      setSaving(true);
      setError(null);

      const payload = {
        youtubePlaylistId: form.youtubePlaylistId.trim(),
        title: toNullableString(form.title),
        description: toNullableString(form.description),
        status: toNullableString(form.status),
        privacyStatus: toNullableString(form.privacyStatus),
        channelId: toNullableString(form.channelId),
        channelTitle: toNullableString(form.channelTitle),
        itemCount: parseOptionalInt(form.itemCount),
        publishedAtUtc,
        thumbnailUrl: toNullableString(form.thumbnailUrl),
        etag: toNullableString(form.etag),
        lastSyncedAtUtc,
        metadata: toNullableString(form.metadata)
      };

      const response = await fetch(`${apiBaseUrl}/youtube-playlists`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });

      if (!response.ok) {
        throw new Error(`Create failed with status ${response.status}`);
      }

      setForm(emptyForm);
      await fetchPlaylists();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Create failed");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section">
        <div>
          <h1 className="page-title">YouTube Playlists</h1>
          <p className="page-subtitle">Track published playlists and YouTube metadata</p>
        </div>
        <div className="header-actions">
          <Link to="/playlists" className="btn btn-secondary">Back to Playlists</Link>
        </div>
      </div>

      {error && (
        <div className="alert alert-error">
          <strong>Error:</strong> {error}
        </div>
      )}

      <div className="youtube-layout">
        <section className="youtube-form-card">
          <h2 className="section-title">Add YouTube Playlist</h2>
          <form className="form-grid" onSubmit={handleSubmit}>
            <label className="form-field">
              <span>YouTube Playlist Id</span>
              <input
                type="text"
                value={form.youtubePlaylistId}
                onChange={(event) => updateField("youtubePlaylistId", event.target.value)}
                placeholder="PLxxxxxxxx"
                required
              />
            </label>

            <label className="form-field">
              <span>Title</span>
              <input
                type="text"
                value={form.title}
                onChange={(event) => updateField("title", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Description</span>
              <textarea
                rows={3}
                value={form.description}
                onChange={(event) => updateField("description", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Status</span>
              <input
                type="text"
                value={form.status}
                onChange={(event) => updateField("status", event.target.value)}
                placeholder="Published"
              />
            </label>

            <label className="form-field">
              <span>Privacy Status</span>
              <input
                type="text"
                value={form.privacyStatus}
                onChange={(event) => updateField("privacyStatus", event.target.value)}
                placeholder="public | unlisted | private"
              />
            </label>

            <label className="form-field">
              <span>Channel Id</span>
              <input
                type="text"
                value={form.channelId}
                onChange={(event) => updateField("channelId", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Channel Title</span>
              <input
                type="text"
                value={form.channelTitle}
                onChange={(event) => updateField("channelTitle", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Item Count</span>
              <input
                type="number"
                value={form.itemCount}
                onChange={(event) => updateField("itemCount", event.target.value)}
                min={0}
              />
            </label>

            <label className="form-field">
              <span>Published At (UTC)</span>
              <input
                type="datetime-local"
                value={form.publishedAtUtc}
                onChange={(event) => updateField("publishedAtUtc", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Thumbnail Url</span>
              <input
                type="url"
                value={form.thumbnailUrl}
                onChange={(event) => updateField("thumbnailUrl", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>ETag</span>
              <input
                type="text"
                value={form.etag}
                onChange={(event) => updateField("etag", event.target.value)}
              />
            </label>

            <label className="form-field">
              <span>Last Synced At (UTC)</span>
              <input
                type="datetime-local"
                value={form.lastSyncedAtUtc}
                onChange={(event) => updateField("lastSyncedAtUtc", event.target.value)}
              />
            </label>

            <label className="form-field form-field-full">
              <span>Metadata (JSON)</span>
              <textarea
                rows={4}
                value={form.metadata}
                onChange={(event) => updateField("metadata", event.target.value)}
                placeholder='{"raw": "payload"}'
              />
            </label>

            <div className="form-actions">
              <button className="btn btn-primary" type="submit" disabled={saving}>
                {saving ? "Saving..." : "Create Playlist"}
              </button>
            </div>
          </form>
        </section>

        <section className="youtube-list">
          <div className="section-header">
            <h2 className="section-title">All YouTube Playlists</h2>
            <button className="btn btn-secondary" onClick={fetchPlaylists}>
              Refresh
            </button>
          </div>

          {loading ? (
            <div className="loading-state">
              <div className="spinner"></div>
              <p>Loading YouTube playlists...</p>
            </div>
          ) : playlists.length === 0 ? (
            <div className="empty-state">
              <div className="empty-icon">📺</div>
              <h3>No YouTube playlists yet</h3>
              <p>Add the first playlist using the form.</p>
            </div>
          ) : (
            <div className="youtube-grid">
              {playlists.map((playlist) => (
                <article key={playlist.id} className="youtube-card">
                  <div className="youtube-card-header">
                    <div>
                      <h3>{playlist.title || "Untitled"}</h3>
                      <p className="muted">{playlist.youtubePlaylistId}</p>
                    </div>
                    {playlist.privacyStatus && (
                      <span className="badge badge-secondary">{playlist.privacyStatus}</span>
                    )}
                  </div>
                  <p className="youtube-card-description">
                    {playlist.description || "No description"}
                  </p>
                  <div className="youtube-card-meta">
                    {playlist.channelTitle && <span>Channel: {playlist.channelTitle}</span>}
                    {playlist.itemCount != null && <span>{playlist.itemCount} items</span>}
                    {playlist.publishedAtUtc && (
                      <span>Published {new Date(playlist.publishedAtUtc).toLocaleDateString()}</span>
                    )}
                  </div>
                </article>
              ))}
            </div>
          )}
        </section>
      </div>
    </div>
  );
}
