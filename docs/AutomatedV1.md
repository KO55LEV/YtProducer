
// get playlists draft optional  without it will return all 
dotnet run   -- playlists draft 

// create folder 
dotnet run   -- playlist_init 5a83430f-e473-407e-8b9b-30f6f9a45d44 

//images
dotnet run -- generate-all-images 5a83430f-e473-407e-8b9b-30f6f9a45d44 

//music
dotnet run   -- generate-all-music 5a83430f-e473-407e-8b9b-30f6f9a45d44

//generate media
dotnet run   -- generate-media 5a83430f-e473-407e-8b9b-30f6f9a45d44

//generate video-thumbnail for playlist 
dotnet run -- track-create-youtube-video-thumbnail 5a83430f-e473-407e-8b9b-30f6f9a45d44

//create channel 
dotnet run -- generate-youtube-playlist 5a83430f-e473-407e-8b9b-30f6f9a45d44 unlisted

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-videos 5a83430f-e473-407e-8b9b-30f6f9a45d44

// add. videos to the list 
dotnet run   -- add-youtube-videos-to-playlist 5a83430f-e473-407e-8b9b-30f6f9a45d44