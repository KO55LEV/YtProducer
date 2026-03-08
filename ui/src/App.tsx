import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import Header from "./components/Header";
import AlbumTemplates, { AlbumTemplateEditor, AlbumTemplateGenerations, AlbumTemplateManualUpload } from "./pages/AlbumTemplates";
import CreateLoop from "./pages/CreateLoop";
import Home from "./pages/Home";
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
            <Route path="/" element={<Home />} />
            <Route path="/playlists" element={<ListManager />} />
            <Route path="/album-templates" element={<AlbumTemplates />} />
            <Route path="/album-templates/new" element={<AlbumTemplateEditor />} />
            <Route path="/album-templates/:id" element={<Navigate to="generations" replace />} />
            <Route path="/album-templates/:id/edit" element={<AlbumTemplateEditor />} />
            <Route path="/album-templates/:id/generations" element={<AlbumTemplateGenerations />} />
            <Route path="/album-templates/:id/manual" element={<AlbumTemplateManualUpload />} />
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
