
// get playlists draft optional  without it will return all 
dotnet run   -- playlists draft 

// create folder 
dotnet run   -- playlist_init f36cdc64-488e-4642-b854-907d6a0cda85

//images
dotnet run -- generate-all-images f36cdc64-488e-4642-b854-907d6a0cda85

//music
dotnet run   -- generate-all-music f36cdc64-488e-4642-b854-907d6a0cda85

//generate media
dotnet run   -- generate-media f36cdc64-488e-4642-b854-907d6a0cda85

//generate video-thumbnail for playlist 
dotnet run -- track-create-youtube-video-thumbnail f36cdc64-488e-4642-b854-907d6a0cda85

//create channel 
dotnet run -- generate-youtube-playlist f36cdc64-488e-4642-b854-907d6a0cda85 unlisted

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-videos f36cdc64-488e-4642-b854-907d6a0cda85

// add. videos to the list 
dotnet run   -- add-youtube-videos-to-playlist f36cdc64-488e-4642-b854-907d6a0cda85