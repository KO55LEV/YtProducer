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
          <div className="nav-dropdown">
            <button type="button" className="nav-link nav-dropdown-trigger" aria-haspopup="true">
              Music
            </button>
            <div className="nav-dropdown-menu">
              <Link to="/playlists" className="nav-dropdown-link">Playlists</Link>
              <Link to="/prompts" className="nav-dropdown-link">Prompts</Link>
              <Link to="/youtube-engagements" className="nav-dropdown-link">Engagements</Link>
              <Link to="/create-loop" className="nav-dropdown-link">Create Loop</Link>
            </div>
          </div>
          <Link to="/jobs" className="nav-link">Jobs</Link>
        </nav>
      </div>
    </header>
  );
}
