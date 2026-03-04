# Quick Start Guide - Testing UI Without Database

## Overview
The API now supports **Mock Data Mode** which loads playlists from JSON files instead of PostgreSQL. This allows you to test the UI immediately without setting up a database.

## 🚀 Start the API (Mock Mode)

### Option 1: Using dotnet CLI
```powershell
cd src\YtProducer.Api
dotnet run
```

The API will:
- ✅ Start on `http://localhost:8080`
- ✅ Load all JSON files from `docs/Playlist/Outputs/`
- ✅ Serve mock data through `/playlists` endpoints
- ✅ Enable CORS for UI at `http://localhost:5173`

You should see:
```
✓ Using MockPlaylistRepository (JSON files from docs/Playlist/Outputs)
Loading 20 mock playlist files from ...
Successfully loaded 20 mock playlists
```

### Option 2: Using Visual Studio
1. Open `YtProducer.sln`
2. Set `YtProducer.Api` as startup project
3. Press F5 or click Run

## 🎨 Start the UI

### Terminal 1 (API):
```powershell
cd src\YtProducer.Api
dotnet run
```

### Terminal 2 (UI):
```powershell
cd ui
npm install  # first time only
npm run dev
```

The UI will open at: **http://localhost:5173**

## ✨ Features Ready to Test

### 1. Playlist Manager (Home Page)
- View all 20 playlists from your JSON files
- Click any playlist card to see details
- Upload additional JSON files (they'll be added to mock data)

### 2. Playlist Detail Page
- See all tracks as YouTube-style video tiles
- View track metadata (BPM, key, energy, duration)
- Energy level visualization bars
- Style tags and YouTube titles

### 3. API Endpoints
- `GET /playlists` - All playlists
- `GET /playlists/{id}` - Single playlist with tracks
- `POST /playlists` - Create new playlist from JSON
- `GET /health` - Health check
- `GET /swagger` - API documentation

## 🔧 Configuration

### Toggle Mock Mode
Edit `src/YtProducer.Api/appsettings.json`:
```json
{
  "UseMockData": true,  // true = JSON files, false = PostgreSQL
  "MockData": {
    "JsonPath": "../../../../docs/Playlist/Outputs"
  }
}
```

### Custom JSON Path
If your JSON files are elsewhere:
```json
{
  "MockData": {
    "JsonPath": "C:\\path\\to\\your\\json\\files"
  }
}
```

## 📊 Mock Data Details

**Source:** All `.json` files from `docs/Playlist/Outputs/`

**Loaded Playlists:**
- Beast Mode Training Music
- Cardio Energy Music
- Cyberpunk Gym Music
- Epic Cinematic Workout Music
- ...and 16 more!

**Each playlist includes:**
- Theme and strategy
- 8-12 tracks per playlist
- Full metadata (BPM, energy, style, etc.)
- YouTube titles and descriptions
- Track positions and durations

## 🔄 Switch to Real Database Later

When PostgreSQL is ready:
1. Set `"UseMockData": false` in appsettings.json
2. Ensure PostgreSQL is running
3. Run migrations (if needed)
4. Restart API

## 🐛 Troubleshooting

### Mock data not loading
- Check console output for file count
- Verify path: `docs/Playlist/Outputs/` relative to API project
- Ensure JSON files are valid format

### CORS errors in browser
- API must be running on port 8080
- UI must be on port 5173 or 3000
- Check browser console for specific error

### Cannot connect to API
- Verify API is running: http://localhost:8080/health
- Should return: `{"status":"ok"}`
- Check firewall/antivirus blocking port 8080

## 📝 Example: Upload New Playlist

1. Open UI at http://localhost:5173
2. Click "Upload JSON"
3. Select any file from `docs/Playlist/Outputs/`
4. Playlist appears immediately in the list
5. Click to view all tracks

The upload temporarily adds to in-memory mock data (resets on API restart).

---

**You're all set! No database required for testing. 🎉**
