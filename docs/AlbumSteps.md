
// get playlists
dotnet run   -- playlists

// create folder 
dotnet run   -- playlist_init

//images
dotnet run -- generate-image 170461a0-fd23-4f94-a5ec-914bb4668deb 1 grok-imagine/text-to-image

//music
dotnet run   -- generate-music 170461a0-fd23-4f94-a5ec-914bb4668deb 1

//generate media
dotnet run   -- generate-media

//generate video-thumbnail for playlist 
dotnet run -- track-create-youtube-video-thumbnail 170461a0-fd23-4f94-a5ec-914bb4668deb


//create channel 
dotnet run -- generate-youtube-playlist 170461a0-fd23-4f94-a5ec-914bb4668deb unlisted

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-videos <playlistId>
dotnet run -- upload-youtube-videos 170461a0-fd23-4f94-a5ec-914bb4668deb

// add. videos to the list 
dotnet run   -- add-youtube-videos-to-playlist 170461a0-fd23-4f94-a5ec-914bb4668deb