import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import Header from "./components/Header";
import AlbumReleasePage from "./pages/AlbumReleasePage";
import CreateLoop from "./pages/CreateLoop";
import Home from "./pages/Home";
import JobsMonitor from "./pages/JobsMonitor";
import ListManager from "./pages/ListManager";
import PlaylistDetail from "./pages/PlaylistDetail";
import PromptsPage, { PromptEditorPage, PromptGenerationsPage, PromptManualRunPage, PromptRunPage } from "./pages/PromptsPage";
import YoutubeEngagementsPage from "./pages/YoutubeEngagementsPage";
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
            <Route path="/prompts" element={<PromptsPage />} />
            <Route path="/prompts/new" element={<PromptEditorPage />} />
            <Route path="/prompts/:id" element={<Navigate to="generations" replace />} />
            <Route path="/prompts/:id/edit" element={<PromptEditorPage />} />
            <Route path="/prompts/:id/generations" element={<PromptGenerationsPage />} />
            <Route path="/prompts/:id/run" element={<PromptRunPage />} />
            <Route path="/prompts/:id/manual" element={<PromptManualRunPage />} />
            <Route path="/album-templates" element={<Navigate to="/prompts" replace />} />
            <Route path="/album-templates/new" element={<Navigate to="/prompts/new" replace />} />
            <Route path="/album-templates/:id" element={<Navigate to="generations" replace />} />
            <Route path="/album-templates/:id/edit" element={<PromptEditorPage />} />
            <Route path="/album-templates/:id/generations" element={<PromptGenerationsPage />} />
            <Route path="/album-templates/:id/run" element={<PromptRunPage />} />
            <Route path="/album-templates/:id/manual" element={<Navigate to="../run" replace />} />
            <Route path="/playlists/:id" element={<PlaylistDetail />} />
            <Route path="/playlists/:id/album-release" element={<AlbumReleasePage />} />
            <Route path="/create-loop" element={<CreateLoop />} />
            <Route path="/jobs" element={<JobsMonitor />} />
            <Route path="/youtube-engagements" element={<YoutubeEngagementsPage />} />
            <Route path="/youtube-playlists" element={<YoutubePlaylists />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
