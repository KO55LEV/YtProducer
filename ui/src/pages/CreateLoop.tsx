import { useState } from "react";
import { Link } from "react-router-dom";
import type { ScheduleTrackLoopResponse } from "../types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

export default function CreateLoop() {
  const [youtubeVideoId, setYoutubeVideoId] = useState("");
  const [loopCount, setLoopCount] = useState("7");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<ScheduleTrackLoopResponse | null>(null);

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>): Promise<void> {
    event.preventDefault();

    const videoId = youtubeVideoId.trim();
    const loops = Math.max(2, Number.parseInt(loopCount || "2", 10) || 2);

    if (!videoId) {
      setError("youtubeVideoId is required");
      return;
    }

    try {
      setSaving(true);
      setError(null);
      setResult(null);

      const response = await fetch(`${apiBaseUrl}/track-loops/schedule-by-youtube-video`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          youtubeVideoId: videoId,
          loopCount: loops
        })
      });

      if (!response.ok) {
        throw new Error(`Create loop failed with status ${response.status}`);
      }

      const data = (await response.json()) as ScheduleTrackLoopResponse;
      setResult(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Create loop failed");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="page-content">
      <div className="page-header-section">
        <div>
          <h1 className="page-title">Create Loop</h1>
          <p className="page-subtitle">Paste a YouTube video id to schedule a loop job for the linked track.</p>
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

      {result && (
        <div className="alert alert-success">
          <strong>Scheduled:</strong> job {result.jobId} for playlist {result.loop.playlistId} position {result.loop.trackPosition}
        </div>
      )}

      <section className="youtube-form-card loop-create-card">
        <h2 className="section-title">Schedule Track Loop</h2>
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="form-field">
            <span>YouTube Video Id</span>
            <input
              type="text"
              value={youtubeVideoId}
              onChange={(event) => setYoutubeVideoId(event.target.value)}
              placeholder="dQw4w9WgXcQ"
              required
            />
          </label>

          <label className="form-field">
            <span>Loop Count</span>
            <input
              type="number"
              min={2}
              step={1}
              value={loopCount}
              onChange={(event) => setLoopCount(event.target.value)}
              required
            />
          </label>

          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={saving}>
              {saving ? "Scheduling..." : "Create Loop Job"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
