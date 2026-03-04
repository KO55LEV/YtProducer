import { useEffect, useMemo, useState } from "react";
import type { Playlist } from "./types";

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:8080";

function statusClassName(status: string): string {
  switch (status.toLowerCase()) {
    case "completed":
    case "ready":
      return "status status-completed";
    case "active":
    case "inprogress":
    case "processing":
      return "status status-active";
    case "failed":
      return "status status-failed";
    default:
      return "status status-pending";
  }
}

export default function App() {
  const [playlists, setPlaylists] = useState<Playlist[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let isMounted = true;

    async function fetchPlaylists(): Promise<void> {
      try {
        const response = await fetch(`${apiBaseUrl}/playlists`);

        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }

        const data = (await response.json()) as Playlist[];

        if (isMounted) {
          setPlaylists(data);
          setError(null);
        }
      } catch (err) {
        if (isMounted) {
          setError(err instanceof Error ? err.message : "Unknown error");
        }
      } finally {
        if (isMounted) {
          setLoading(false);
        }
      }
    }

    fetchPlaylists();

    return () => {
      isMounted = false;
    };
  }, []);

  const title = useMemo(() => "YtProducer Dashboard", []);

  return (
    <main className="page">
      <header className="page-header">
        <h1>{title}</h1>
        <p>Long-running orchestration overview for playlist production jobs.</p>
      </header>

      {loading ? <p className="muted">Loading playlists...</p> : null}
      {error ? <p className="error">Failed to load playlists: {error}</p> : null}

      <section className="card-grid">
        {playlists.map((playlist) => (
          <article key={playlist.id} className="playlist-card">
            <div className="playlist-card-header">
              <h2>{playlist.title}</h2>
              <span className={statusClassName(playlist.status)}>{playlist.status}</span>
            </div>
            <p>{playlist.description ?? "No description"}</p>
            <p className="muted">Tracks: {playlist.tracks.length}</p>
          </article>
        ))}
      </section>
    </main>
  );
}
