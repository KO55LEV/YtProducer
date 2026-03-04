import { BrowserRouter, Routes, Route } from "react-router-dom";
import Header from "./components/Header";
import ListManager from "./pages/ListManager";
import PlaylistDetail from "./pages/PlaylistDetail";

export default function App() {
  return (
    <BrowserRouter>
      <div className="app">
        <Header />
        <main className="main-content">
          <Routes>
            <Route path="/" element={<ListManager />} />
            <Route path="/playlists/:id" element={<PlaylistDetail />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  );
}
