
// get playlists draft optional  without it will return all 
dotnet run   -- playlists draft 

// create folder 
dotnet run   -- playlist_init d8624fb8-da29-41a7-b072-5260b1443689

//images
dotnet run -- generate-all-images d8624fb8-da29-41a7-b072-5260b1443689

//music
dotnet run   -- generate-all-music d8624fb8-da29-41a7-b072-5260b1443689

//generate media
dotnet run   -- generate-media d8624fb8-da29-41a7-b072-5260b1443689

//generate video-thumbnail for playlist 
dotnet run -- track-create-youtube-video-thumbnail_v2 d8624fb8-da29-41a7-b072-5260b1443689

//create channel 
dotnet run -- generate-youtube-playlist d8624fb8-da29-41a7-b072-5260b1443689 unlisted

// upload video and then upload thumbnail for same track position
dotnet run -- upload-youtube-videos d8624fb8-da29-41a7-b072-5260b1443689

// add. videos to the list 
dotnet run   -- add-youtube-videos-to-playlist d8624fb8-da29-41a7-b072-5260b1443689