import { Link } from "react-router-dom";

export default function Header() {
  return (
    <header className="site-header">
      <div className="header-container">
        <Link to="/" className="logo">
          <span className="logo-icon">▶</span>
          <span className="logo-text">YtProducer</span>
        </Link>
        <nav className="nav">
          <Link to="/" className="nav-link">Home</Link>
          <Link to="/playlists" className="nav-link">Playlists</Link>
          <Link to="/prompts" className="nav-link">Prompts</Link>
          <Link to="/youtube-engagements" className="nav-link">Engagements</Link>
          <Link to="/create-loop" className="nav-link">Create Loop</Link>
          <Link to="/jobs" className="nav-link">Jobs</Link>
        </nav>
      </div>
    </header>
  );
}
