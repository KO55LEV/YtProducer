# YtProducer UI

Professional React + TypeScript + Vite application for managing YouTube playlist production.

## Features

- **Playlist Manager**: View all playlists with upload functionality
- **Playlist Detail View**: View tracks as YouTube-style video tiles
- **JSON Upload**: Upload playlist JSON files directly
- **Professional Design**: Modern, responsive UI with consistent styling
- **React Router**: Client-side routing for multi-page experience

## Setup

1. Install dependencies:
```bash
npm install
```

2. Start development server:
```bash
npm run dev
```

3. Open browser at `http://localhost:5173`

## Environment Variables

Create a `.env` file (optional):
```
VITE_API_BASE_URL=http://localhost:8080
```

Default API URL is `http://localhost:8080` if not specified.

## Pages

- **/** - Playlist Manager (home page)
- **/playlists/:id** - Playlist Detail view with track tiles

## Upload JSON Format

Upload JSON files from the `docs/Playlist/Outputs/` directory. The app automatically maps the JSON structure to the API contract.
