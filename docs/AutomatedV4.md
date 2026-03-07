
// get playlists draft optional  without it will return all 
dotnet run   -- playlists draft 

// create folder 
dotnet run   -- playlist_init e8607302-328c-441f-8afe-b10364abe5f2

//images
dotnet run -- generate-all-images e8607302-328c-441f-8afe-b10364abe5f2

//music
dotnet run   -- generate-all-music e8607302-328c-441f-8afe-b10364abe5f2

//generate media
dotnet run   -- generate-media-local e8607302-328c-441f-8afe-b10364abe5f2 fast

dotnet run -- generate-media-local <playlistId> legacy
dotnet run -- generate-media-local <playlistId> quality
dotnet run -- generate-media-local <playlistId> fast


//generate video-thumbnail for playlist 
dotnet run -- track-create-youtube-video-thumbnail_v2 e8607302-328c-441f-8afe-b10364abe5f2

//create channel 
dotnet run -- generate-youtube-playlist e8607302-328c-441f-8afe-b10364abe5f2 unlisted

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-videos e8607302-328c-441f-8afe-b10364abe5f2

// add. videos to the list 
dotnet run   -- add-youtube-videos-to-playlist e8607302-328c-441f-8afe-b10364abe5f2