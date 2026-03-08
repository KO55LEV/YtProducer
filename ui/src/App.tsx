import { BrowserRouter, Routes, Route } from "react-router-dom";
import Header from "./components/Header";
import AlbumTemplates from "./pages/AlbumTemplates";
import CreateLoop from "./pages/CreateLoop";
import JobsMonitor from "./pages/JobsMonitor";
import ListManager from "./pages/ListManager";
import PlaylistDetail from "./pages/PlaylistDetail";
import YoutubePlaylists from "./pages/YoutubePlaylists";

export default function App() {
  return (
    <BrowserRouter>
      <div className="app">
        <Header />
        <main className="main-content">
          <Routes>
            <Route path="/" element={<ListManager />} />
            <Route path="/album-templates" element={<AlbumTemplates />} />
            <Route path="/album-templates/:id" element={<AlbumTemplates />} />
            <Route path="/playlists/:id" element={<PlaylistDetail />} />
            <Route path="/create-loop" element={<CreateLoop />} />
            <Route path="/jobs" element={<JobsMonitor />} />
            <Route path="/youtube-playlists" element={<YoutubePlaylists />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
