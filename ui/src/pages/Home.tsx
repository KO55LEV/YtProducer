import { Link } from "react-router-dom";
import heroImage from "../../../docs/ytProducerHome.png";

const workflowSteps = [
  {
    title: "Create",
    description: "Start from album templates or uploaded JSON and shape the playlist concept with structured metadata."
  },
  {
    title: "Generate Images",
    description: "Create track artwork, curate backgrounds, and prepare thumbnail-ready visual assets."
  },
  {
    title: "Generate Music",
    description: "Run music generation prompts per track and collect multiple audio candidates for each position."
  },
  {
    title: "Generate Video",
    description: "Render visualizers, track generation progress, and produce media variants fit for publishing."
  },
  {
    title: "Publish",
    description: "Create YouTube playlists, upload assets, apply thumbnails, and orchestrate release scheduling."
  }
] as const;

const quickLinks = [
  {
    title: "Playlists",
    description: "Manage album pipelines, track assets, prompts, and publishing state.",
    to: "/playlists"
  },
  {
    title: "Jobs",
    description: "Monitor scheduled work, execution logs, failures, and background processing.",
    to: "/jobs"
  },
  {
    title: "Prompts",
    description: "Define reusable LLM prompts, provider settings, and prompt runs for generation and SEO workflows.",
    to: "/prompts"
  },
  {
    title: "YouTube Playlists",
    description: "Review synced YouTube playlist metadata and publication state.",
    to: "/youtube-playlists"
  }
] as const;

export default function Home() {
  return (
    <div className="home-page">
      <section className="home-hero">
        <div className="home-hero-media">
          <img src={heroImage} alt="YtProducer content production control room" className="home-hero-image" />
          <div className="home-hero-overlay" />
        </div>

        <div className="home-hero-content">
          <div className="home-hero-kicker">AI Content Production Studio</div>
          <h1 className="home-hero-title">Create, render, and publish YouTube content from one command center.</h1>
          <p className="home-hero-description">
            YtProducer orchestrates playlists, prompt templates, image generation, music generation, video rendering,
            job execution, and YouTube publishing in one production flow.
          </p>

          <div className="home-hero-actions">
            <Link to="/playlists" className="btn btn-primary">
              Open Playlists
            </Link>
            <Link to="/jobs" className="btn btn-secondary">
              Monitor Jobs
            </Link>
          </div>

          <div className="home-hero-panels">
            <div className="home-panel">
              <span className="home-panel-label">Core Flow</span>
              <p>Template-driven creation, asset generation, render orchestration, and YouTube publishing.</p>
            </div>
            <div className="home-panel">
              <span className="home-panel-label">Operational Focus</span>
              <p>Built for repeatable production, background jobs, logs, retries, and media lifecycle control.</p>
            </div>
          </div>
        </div>
      </section>

      <section className="home-section">
        <div className="home-section-header">
          <div>
            <div className="home-section-kicker">Production Flow</div>
            <h2 className="home-section-title">From idea to published asset</h2>
          </div>
          <p className="home-section-copy">
            The platform is structured as a production pipeline instead of isolated tools, so every step can be scheduled,
            monitored, and repeated cleanly.
          </p>
        </div>

        <div className="home-flow-grid">
          {workflowSteps.map((step, index) => (
            <article key={step.title} className="home-flow-card">
              <div className="home-flow-index">{String(index + 1).padStart(2, "0")}</div>
              <h3>{step.title}</h3>
              <p>{step.description}</p>
            </article>
          ))}
        </div>
      </section>

      <section className="home-section">
        <div className="home-section-header">
          <div>
            <div className="home-section-kicker">Workspace</div>
            <h2 className="home-section-title">Jump into the production surfaces</h2>
          </div>
        </div>

        <div className="home-links-grid">
          {quickLinks.map((item) => (
            <Link key={item.title} to={item.to} className="home-link-card">
              <div className="home-link-top">
                <h3>{item.title}</h3>
                <span className="home-link-arrow">→</span>
              </div>
              <p>{item.description}</p>
            </Link>
          ))}
        </div>
      </section>
    </div>
  );
}
