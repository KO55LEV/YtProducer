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
          <Link to="/" className="nav-link">Playlists</Link>
          <Link to="/youtube-playlists" className="nav-link">YouTube Playlists</Link>
        </nav>
      </div>
    </header>
  );
}
