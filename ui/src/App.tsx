import { BrowserRouter, Routes, Route } from "react-router-dom";
import Header from "./components/Header";
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
            <Route path="/playlists/:id" element={<PlaylistDetail />} />
            <Route path="/youtube-playlists" element={<YoutubePlaylists />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
